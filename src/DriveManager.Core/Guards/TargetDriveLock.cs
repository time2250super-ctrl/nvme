using DriveManager.Core.Models;

namespace DriveManager.Core.Guards;

/// <summary>
/// Hard-binds this build to one physical device: the SPCC M.2 PCIe NVMe (currently Disk 1 / D:).
/// The installer will refuse any other disk, and the app will not start unless this drive is present.
/// Identity is the hardware serial, not a drive letter (letters change after repartition).
/// </summary>
public static class TargetDriveLock
{
    public const string RequiredSerial = "0000_0000_0000_0000_00E0_4C16_120D_5A5A.";
    public const string RequiredNameFragment = "SPCC";
    public const string RequiredBus = "NVMe";
    public const double MinSizeGb = 100;
    public const double MaxSizeGb = 140;
    public const string AppPartitionLabel = "DriveMgr";
    public const string DataPartitionLabel = "Data";
    public const long AppPartitionBytes = 5L * 1024 * 1024 * 1024;

    public static bool Matches(PhysicalDiskInfo disk)
    {
        if (!disk.FriendlyName.Contains(RequiredNameFragment, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(disk.BusType, RequiredBus, StringComparison.OrdinalIgnoreCase))
            return false;
        if (disk.SizeGb < MinSizeGb || disk.SizeGb > MaxSizeGb)
            return false;
        var got = NormalizeSerial(disk.SerialNumber);
        var need = NormalizeSerial(RequiredSerial);
        if (got.Length == 0)
            return true;
        return got == need || (got.Length >= 8 && need.EndsWith(got, StringComparison.Ordinal))
            || (need.Length >= 8 && got.EndsWith(need, StringComparison.Ordinal));
    }

    private static string NormalizeSerial(string? serial)
        => new string((serial ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static PhysicalDiskInfo? FindLicensedDisk(IEnumerable<PhysicalDiskInfo> disks)
        => disks.FirstOrDefault(Matches);

    public static VolumeInfo? FindAppVolume(PhysicalDiskInfo disk)
    {
        var labeled = disk.Volumes.FirstOrDefault(v =>
            string.Equals(v.Label, AppPartitionLabel, StringComparison.OrdinalIgnoreCase));
        if (labeled is not null) return labeled;

        return disk.Volumes
            .Where(v => v.SizeBytes >= 4L * 1024 * 1024 * 1024 && v.SizeBytes <= 6L * 1024 * 1024 * 1024)
            .OrderBy(v => Math.Abs(v.SizeBytes - AppPartitionBytes))
            .FirstOrDefault();
    }

    public static string DenialReason()
        => "This software is licensed only to the SPCC M.2 PCIe SSD "
           + $"(serial {RequiredSerial}). That drive was not found on this machine.";
}
