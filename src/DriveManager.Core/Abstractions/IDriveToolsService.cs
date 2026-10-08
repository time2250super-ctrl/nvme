using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

/// <summary>
/// Drive management: rename volumes, assign/remove drive letters, take a disk offline/online.
/// Builders return a command plan; <see cref="ExecuteAsync"/> runs it immediately.
/// </summary>
public interface IDriveToolsService
{
    bool IsSupported { get; }

    DriveOperation BuildSetLabel(PhysicalDiskInfo disk, char driveLetter, string newLabel);
    DriveOperation BuildChangeDriveLetter(PhysicalDiskInfo disk, char currentLetter, char newLetter);
    DriveOperation BuildRemoveDriveLetter(PhysicalDiskInfo disk, char driveLetter);
    DriveOperation BuildSetDiskOffline(PhysicalDiskInfo disk, bool offline);

    /// <summary>Runs the operation immediately.</summary>
    Task ExecuteAsync(DriveOperation operation, CancellationToken ct = default);
}
