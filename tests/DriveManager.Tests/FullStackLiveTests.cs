using DriveManager.Ai;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Guards;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows;
using Xunit;

namespace DriveManager.Tests;

[Collection("live-hardware")]
public sealed class FullStackLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public FullStackLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Licensed_SPCC_has_DriveMgr_and_Data_volumes()
    {
        var disk = TargetDriveLock.FindLicensedDisk(_fx.Disks);
        Assert.NotNull(disk);
        var app = TargetDriveLock.FindAppVolume(disk!);
        Assert.NotNull(app);
        Assert.True(app!.DriveLetter.HasValue);
        Assert.Contains(disk.Volumes, v =>
            string.Equals(v.Label, "Data", StringComparison.OrdinalIgnoreCase)
            || (v.SizeBytes > 50L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Health_analyzer_scores_live_NVMe()
    {
        Assert.NotNull(_fx.NvmeHealth);
        var assessment = new RuleBasedHealthAnalyzer().Assess(_fx.NvmeHealth!);
        Assert.InRange(assessment.RiskScore, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(assessment.Summary));
    }

    [Fact]
    public void Tools_and_format_previews_target_licensed_disk_only()
    {
        var disk = TargetDriveLock.FindLicensedDisk(_fx.Disks)!;
        var vol = disk.Volumes.First(v => v.DriveLetter.HasValue);
        var letter = vol.DriveLetter!.Value;

        var tools = new WindowsDriveToolsService();
        var rename = tools.BuildSetLabel(disk, letter, "DriveMgr");
        Assert.False(rename.Preview.Warnings.Any(w => w.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase)));

        var format = new WindowsFormatService();
        var preview = format.PreviewQuickFormat(disk, letter, FileSystemType.Ntfs, "DriveMgr");
        Assert.Contains("Format-Volume", string.Join(" ", preview.Steps), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("ERASE DISK", preview.ConfirmationPhrase);
    }

    [Fact]
    public async Task Encryption_status_reads_live_app_volume()
    {
        var disk = TargetDriveLock.FindLicensedDisk(_fx.Disks)!;
        var app = TargetDriveLock.FindAppVolume(disk)!;
        var state = await new WindowsSecurityService().GetEncryptionStateAsync(app.DriveLetter!.Value);
        Assert.False(string.IsNullOrWhiteSpace(state.Summary));
    }

    [Fact]
    public void Filesystem_advisor_recommends_NTFS_for_this_NVMe()
    {
        var disk = TargetDriveLock.FindLicensedDisk(_fx.Disks)!;
        foreach (var vol in disk.Volumes)
        {
            var advice = FilesystemAdvisor.Recommend(disk.BusType, vol.SizeBytes, disk.IsSystem || disk.IsBoot);
            Assert.Equal("NTFS", advice.Recommended, ignoreCase: true);
        }
    }

    [Fact]
    public void Model_manager_folder_is_writable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dm-models-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mgr = new LocalModelManager(dir);
            Assert.True(Directory.Exists(mgr.ModelsDirectory));
            Assert.Empty(mgr.ListModels());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
