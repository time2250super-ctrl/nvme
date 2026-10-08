using DriveManager.Core.Models;

namespace DriveManager.Core.Guards;

/// <summary>
/// Blocks destructive and risky volume/disk changes on the Windows system/boot disk.
/// </summary>
public static class BootDriveGuard
{
    public static bool IsProtected(PhysicalDiskInfo disk) => disk.IsSystem || disk.IsBoot;

    public static string? BlockDestructiveReason(PhysicalDiskInfo disk)
        => IsProtected(disk)
            ? $"Disk {disk.Number} ('{disk.FriendlyName}') is the system or boot disk. Destructive operations are blocked."
            : null;

    public static void RequireDestructiveAllowed(PhysicalDiskInfo disk)
    {
        if (BlockDestructiveReason(disk) is { } reason)
            throw new InvalidOperationException(reason);
    }

    public static string? BlockRiskyToolReason(PhysicalDiskInfo disk, DriveOperationKind kind) => kind switch
    {
        DriveOperationKind.SetDiskOffline when IsProtected(disk) =>
            "The system/boot disk cannot be taken offline.",
        DriveOperationKind.ChangeDriveLetter or DriveOperationKind.RemoveDriveLetter when IsProtected(disk) =>
            "Drive-letter changes on the system/boot disk are blocked.",
        _ => null,
    };
}
