using DriveManager.Ai;
using DriveManager.Core.Guards;
using DriveManager.Core.Models;
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
public sealed class DriveGuardLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public DriveGuardLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Real_System_Disk_Is_Blocked_From_Destructive_Ops()
    {
        var systemDisk = _fx.Disks.First(d => d.IsSystem);
        Assert.False(DriveGuard.EnsureSafeTarget(systemDisk).Allowed);
    }

    [Fact]
    public void Real_Nvme_Data_Disk_Is_Allowed_When_Not_System()
    {
        Assert.NotNull(_fx.Nvme);
        if (_fx.Nvme!.IsSystem || _fx.Nvme.IsBoot)
            return; // This machine's NVMe is the OS disk; guard correctly blocks it elsewhere.

        Assert.True(DriveGuard.EnsureSafeTarget(_fx.Nvme).Allowed);
    }

    [Fact]
    public void Wrong_Confirmation_Phrase_Throws_For_Real_Disk()
    {
        Assert.NotNull(_fx.Nvme);
        var phrase = DriveGuard.BuildConfirmationPhrase(_fx.Nvme!);
        Assert.Throws<InvalidOperationException>(
            () => DriveGuard.Validate(_fx.Nvme!, "incorrect confirmation", phrase));
    }
}
