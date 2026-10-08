using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

/// <summary>
/// Enumerates physical disks and volumes. Platform specific implementations live in
/// DriveManager.Platform.* projects.
/// </summary>
public interface IDiskService
{
    Task<IReadOnlyList<PhysicalDiskInfo>> GetDisksAsync(CancellationToken ct = default);

    Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct = default);

    Task<PhysicalDiskInfo?> GetDiskForVolumeAsync(char driveLetter, CancellationToken ct = default);
}
