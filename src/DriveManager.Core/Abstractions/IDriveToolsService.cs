using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

/// <summary>
/// Non-destructive drive management: rename volumes, assign/remove drive letters, take a disk
/// offline/online. Every builder returns a preview first; <see cref="ExecuteAsync"/> performs the
/// action. Implementations MUST refuse to touch boot/system disks for offline operations and MUST
/// refuse to remove the system drive letter.
/// </summary>
public interface IDriveToolsService
{
    bool IsSupported { get; }

    DriveOperation BuildSetLabel(PhysicalDiskInfo disk, char driveLetter, string newLabel);
    DriveOperation BuildChangeDriveLetter(PhysicalDiskInfo disk, char currentLetter, char newLetter);
    DriveOperation BuildRemoveDriveLetter(PhysicalDiskInfo disk, char driveLetter);
    DriveOperation BuildSetDiskOffline(PhysicalDiskInfo disk, bool offline);

    /// <summary>
    /// Executes the operation. For operations flagged destructive/guarded in their preview, the
    /// <paramref name="typedConfirmation"/> MUST match the preview's confirmation phrase.
    /// </summary>
    Task ExecuteAsync(DriveOperation operation, string typedConfirmation = "", CancellationToken ct = default);
}
