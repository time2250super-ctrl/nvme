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
    public NvmeHealth? NvmeHealth { get; }

    public LiveHardwareFixture()
    {
        var diskService = new WindowsDiskService();
        Disks = diskService.GetDisksAsync().GetAwaiter().GetResult();
        Nvme = Disks.FirstOrDefault(d => d.IsNvme);

        if (Nvme is not null)
        {
            var healthService = new WindowsHealthService();
            NvmeHealth = healthService.ReadHealthAsync(Nvme).GetAwaiter().GetResult();
        }
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
        Assert.InRange(h.TemperatureCelsius, 0, 120);
        Assert.InRange(h.AvailableSpare, 0, 100);
        Assert.True(h.PercentageUsed >= 0);
        Assert.True(h.PowerOnHours < 1_000_000);
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
