using DriveManager.Core.Models;

namespace DriveManager.Core.Guards;

public sealed record GuardResult(bool Allowed, string Reason)
{
    public static GuardResult Allow() => new(true, "OK");
    public static GuardResult Deny(string reason) => new(false, reason);
}

/// <summary>
/// Central safety checks for destructive operations. The golden rule: never touch the
/// boot/system disk, and always require the caller to prove it is targeting the intended device.
/// </summary>
public static class DriveGuard
{
    /// <summary>
    /// Verifies that a destructive operation is permitted on <paramref name="disk"/>.
    /// </summary>
    public static GuardResult EnsureSafeTarget(PhysicalDiskInfo disk)
    {
        if (disk.IsSystem)
            return GuardResult.Deny($"Disk {disk.Number} ('{disk.FriendlyName}') hosts the running OS and can never be formatted by this tool.");

        if (disk.IsBoot)
            return GuardResult.Deny($"Disk {disk.Number} ('{disk.FriendlyName}') is the boot disk and is blocked from destructive operations.");

        return GuardResult.Allow();
    }

    /// <summary>
    /// Builds the exact phrase a user must type to confirm a destructive action against a disk.
    /// Includes the serial so confirmation is unique to the physical device.
    /// </summary>
    public static string BuildConfirmationPhrase(PhysicalDiskInfo disk)
    {
        var serial = string.IsNullOrWhiteSpace(disk.SerialNumber) ? "NOSERIAL" : disk.SerialNumber!.Trim();
        return $"ERASE DISK {disk.Number} {disk.FriendlyName} {serial}".Trim();
    }

    /// <summary>
    /// Throws if the operation is not safe or the typed confirmation does not match.
    /// </summary>
    public static void Validate(PhysicalDiskInfo disk, string? typedConfirmation, string expectedPhrase)
    {
        var guard = EnsureSafeTarget(disk);
        if (!guard.Allowed)
            throw new InvalidOperationException(guard.Reason);

        if (!string.Equals(typedConfirmation?.Trim(), expectedPhrase.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("Confirmation phrase does not match the target disk. Aborting for safety.");
    }
}
