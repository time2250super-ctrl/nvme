using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

/// <summary>
/// Reads health telemetry from a physical disk. NVMe drives return rich SMART data; other
/// drives (SATA/USB) return whatever the OS exposes, unified into <see cref="DriveHealthInfo"/>.
/// </summary>
public interface IHealthService
{
    /// <summary>
    /// Returns the current health snapshot, or null if no health data is available for the device
    /// (or access was denied / elevation required).
    /// </summary>
    Task<DriveHealthInfo?> ReadHealthAsync(PhysicalDiskInfo disk, CancellationToken ct = default);

    bool IsSupported { get; }
}
