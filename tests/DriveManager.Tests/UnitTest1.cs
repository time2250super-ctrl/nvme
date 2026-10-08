using System.IO;
using DriveManager.Ai;
using DriveManager.Core.Models;
using DriveManager.Core.Telemetry;
using DriveManager.Platform.Windows;
using Xunit;

namespace DriveManager.Tests;

/// <summary>
/// Reads the real machine's disks and NVMe health ONCE and shares it with all live tests.
/// Nothing here is simulated: every assertion runs against actual hardware.
/// NVMe health requires the test host to run elevated (Administrator).
/// </summary>
public sealed class LiveHardwareFixture
{
    public IReadOnlyList<PhysicalDiskInfo> Disks { get; }
    public PhysicalDiskInfo? Nvme { get; }

    /// <summary>Unified health for the NVMe device (may be null if not elevated / unsupported).</summary>
    public DriveHealthInfo? NvmeHealth { get; }

    /// <summary>Unified health read for EVERY physical disk, keyed by disk number.</summary>
    public IReadOnlyDictionary<int, DriveHealthInfo?> HealthByDisk { get; }

    public LiveHardwareFixture()
    {
        var diskService = new WindowsDiskService();
        Disks = diskService.GetDisksAsync().GetAwaiter().GetResult();
        Nvme = Disks.FirstOrDefault(d => d.IsNvme);

        var healthService = new WindowsHealthService();
        var map = new Dictionary<int, DriveHealthInfo?>();
        foreach (var d in Disks)
            map[d.Number] = healthService.ReadHealthAsync(d).GetAwaiter().GetResult();
        HealthByDisk = map;

        if (Nvme is not null)
            NvmeHealth = HealthByDisk[Nvme.Number];
    }
}

[CollectionDefinition("live-hardware")]
public sealed class LiveHardwareCollection : ICollectionFixture<LiveHardwareFixture>;

[Collection("live-hardware")]
public sealed class DiskDiscoveryLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public DiskDiscoveryLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Enumerates_At_Least_One_Physical_Disk()
        => Assert.NotEmpty(_fx.Disks);

    [Fact]
    public void Detects_A_Real_Nvme_Disk()
        => Assert.NotNull(_fx.Nvme);

    [Fact]
    public void Nvme_Reports_Serial_And_Plausible_Size()
    {
        Assert.NotNull(_fx.Nvme);
        Assert.False(string.IsNullOrWhiteSpace(_fx.Nvme!.SerialNumber));
        Assert.True(_fx.Nvme.SizeGb > 1, "NVMe size should be a real, non-trivial capacity.");
    }

    [Fact]
    public void A_System_Disk_Is_Present_And_Identified()
        => Assert.Contains(_fx.Disks, d => d.IsSystem);
}

[Collection("live-hardware")]
public sealed class NvmeHealthLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public NvmeHealthLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Live_Health_Was_Read_From_The_Nvme()
    {
        Assert.NotNull(_fx.Nvme);
        Assert.NotNull(_fx.NvmeHealth); // null => not elevated or device unsupported
    }

    [Fact]
    public void Live_Health_Values_Are_Within_Physical_Ranges()
    {
        Assert.NotNull(_fx.NvmeHealth);
        var h = _fx.NvmeHealth!;
        Assert.Equal("NVMe SMART (log 0x02)", h.Source);
        Assert.InRange(h.TemperatureCelsius ?? 0, 0, 120);
        Assert.InRange(h.AvailableSpare ?? 0, 0, 100);
        Assert.True((h.PercentageUsed ?? 0) >= 0);
        Assert.True((h.PowerOnHours ?? 0) < 1_000_000);
    }

    [Fact]
    public void Analyzer_Produces_Valid_Assessment_From_Live_Telemetry()
    {
        Assert.NotNull(_fx.NvmeHealth);
        var assessment = new RuleBasedHealthAnalyzer().Assess(_fx.NvmeHealth!);

        Assert.InRange(assessment.RiskScore, 0, 100);
        Assert.True(Enum.IsDefined(typeof(HealthLevel), assessment.Level));
        Assert.NotEmpty(assessment.Findings);
        Assert.False(string.IsNullOrWhiteSpace(assessment.Summary));
    }
}

[Collection("live-hardware")]
public sealed class MultiDriveHealthLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public MultiDriveHealthLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Health_Is_Attempted_For_Every_Physical_Disk()
    {
        // Every discovered disk has an entry (value may be null if the device exposes no data).
        foreach (var d in _fx.Disks)
            Assert.True(_fx.HealthByDisk.ContainsKey(d.Number));
    }

    [Fact]
    public void At_Least_One_NonNvme_Or_Nvme_Reports_Real_Health()
    {
        // The whole point of cross-drive support: at least one drive yields usable health data.
        var withData = _fx.HealthByDisk.Values.Where(h => h is not null && h.HasAnyData).ToList();
        Assert.NotEmpty(withData);
    }

    [Fact]
    public void Analyzer_Runs_Cleanly_On_Every_Available_Drive_Health()
    {
        var analyzer = new RuleBasedHealthAnalyzer();
        foreach (var (number, health) in _fx.HealthByDisk)
        {
            if (health is null)
                continue;

            var a = analyzer.Assess(health);
            Assert.InRange(a.RiskScore, 0, 100);
            Assert.True(Enum.IsDefined(typeof(HealthLevel), a.Level),
                $"Disk {number} produced an undefined health level.");
            Assert.NotEmpty(a.Findings);
            Assert.False(string.IsNullOrWhiteSpace(a.Summary));
        }
    }

    [Fact]
    public void Every_Health_Record_Declares_Its_Source()
    {
        foreach (var health in _fx.HealthByDisk.Values)
        {
            if (health is null)
                continue;
            Assert.False(string.IsNullOrWhiteSpace(health.Source));
        }
    }
}

[Collection("live-hardware")]
public sealed class TelemetryLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public TelemetryLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public async Task Saves_And_Reads_Back_A_Real_Health_Snapshot()
    {
        var health = _fx.HealthByDisk.Values.FirstOrDefault(h => h is not null && h.HasAnyData);
        Assert.NotNull(health); // at least one real drive must expose health data

        var path = Path.Combine(Path.GetTempPath(), $"dmtest_{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTelemetryStore(path);
            await store.SaveAsync("live-test-key", health!);

            var history = await store.GetHistoryAsync("live-test-key");
            Assert.Single(history);
            Assert.Equal(health!.Source, history[0].Source);
            Assert.Equal(health.HealthStatus, history[0].HealthStatus);
            Assert.Equal(1, await store.CountAsync("live-test-key"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* temp file */ }
        }
    }
}

[Collection("live-hardware")]
public sealed class DriveToolsLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public DriveToolsLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Set_Label_Builds_A_SetVolume_Command()
    {
        var disk = _fx.Disks.First();
        var svc = new WindowsDriveToolsService();
        var op = svc.BuildSetLabel(disk, 'Z', "LiveTestLabel");
        Assert.Equal(DriveOperationKind.SetLabel, op.Kind);
        Assert.Contains(op.Preview.Steps, s => s.Contains("Set-Volume") && s.Contains("LiveTestLabel"));
        Assert.False(op.Preview.IsDestructive);
    }

    [Fact]
    public void Offline_Builds_A_SetDisk_Command()
    {
        var disk = _fx.Disks.First();
        var svc = new WindowsDriveToolsService();
        var op = svc.BuildSetDiskOffline(disk, offline: true);
        Assert.Equal(DriveOperationKind.SetDiskOffline, op.Kind);
        Assert.Contains(op.Preview.Steps, s => s.Contains("Set-Disk") && s.Contains("IsOffline"));
    }
}
