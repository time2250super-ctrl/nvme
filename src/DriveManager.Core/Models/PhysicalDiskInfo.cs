namespace DriveManager.Core.Models;

/// <summary>
/// Describes a physical disk (e.g. the SPCC NVMe SSD) independent of any OS.
/// </summary>
public sealed record PhysicalDiskInfo
{
    public required int Number { get; init; }
    public required string FriendlyName { get; init; }
    public string? SerialNumber { get; init; }
    public string? Model { get; init; }
    public string? FirmwareRevision { get; init; }
    public long SizeBytes { get; init; }
    public string BusType { get; init; } = "Unknown";
    public string PartitionStyle { get; init; } = "Unknown";

    /// <summary>True when this disk hosts the active boot partition.</summary>
    public bool IsBoot { get; init; }

    /// <summary>True when this disk hosts the running OS / system partition.</summary>
    public bool IsSystem { get; init; }

    /// <summary>True when this looks like an NVMe device.</summary>
    public bool IsNvme => string.Equals(BusType, "NVMe", StringComparison.OrdinalIgnoreCase);

    public double SizeGb => Math.Round(SizeBytes / 1024d / 1024d / 1024d, 2);

    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];
}
