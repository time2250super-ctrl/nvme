using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

/// <summary>
/// Reads NVMe SMART / health telemetry from a physical disk.
/// </summary>
public interface IHealthService
{
    /// <summary>
    /// Returns the current health snapshot, or null if the device does not expose
    /// NVMe health data or access was denied.
    /// </summary>
    Task<NvmeHealth?> ReadHealthAsync(PhysicalDiskInfo disk, CancellationToken ct = default);

    bool IsSupported { get; }
}
