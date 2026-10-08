using DriveManager.Core.Guards;
using Xunit;

namespace DriveManager.Tests;

[Collection("live-hardware")]
public sealed class TargetDriveLockLiveTests
{
    private readonly LiveHardwareFixture _fx;
    public TargetDriveLockLiveTests(LiveHardwareFixture fx) => _fx = fx;

    [Fact]
    public void Licensed_SPCC_NVMe_is_present_and_unique()
    {
        var match = TargetDriveLock.FindLicensedDisk(_fx.Disks);
        Assert.NotNull(match);
        Assert.Equal(TargetDriveLock.RequiredSerial, match!.SerialNumber?.Trim());
        Assert.Single(_fx.Disks, TargetDriveLock.Matches);
        Assert.False(match.IsSystem);
        Assert.False(match.IsBoot);
    }
}
