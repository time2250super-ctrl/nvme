using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

public enum FileSystemType
{
    Ntfs,
    ExFat,
    Fat32,
}

/// <summary>
/// Builds dry-run previews and (after confirmation) executes formatting / partitioning.
/// Implementations MUST refuse to touch boot/system disks.
/// </summary>
public interface IFormatService
{
    /// <summary>Preview a quick format of a single volume.</summary>
    OperationPreview PreviewQuickFormat(PhysicalDiskInfo disk, char driveLetter, FileSystemType fileSystem, string label);

    /// <summary>Preview wiping a disk and creating one or more partitions.</summary>
    OperationPreview PreviewRepartition(PhysicalDiskInfo disk, IReadOnlyList<long> partitionSizesBytes, FileSystemType fileSystem);

    /// <summary>
    /// Execute a previously generated preview. The <paramref name="typedConfirmation"/> MUST match
    /// <see cref="OperationPreview.ConfirmationPhrase"/> exactly or the call throws.
    /// </summary>
    Task ExecuteAsync(OperationPreview preview, string typedConfirmation, CancellationToken ct = default);
}
