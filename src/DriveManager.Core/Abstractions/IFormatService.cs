using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

public enum FileSystemType
{
    Ntfs,
    ExFat,
    Fat32,
}

/// <summary>
/// Builds and executes formatting / partitioning command plans.
/// </summary>
public interface IFormatService
{
    /// <summary>Build a quick format of a single volume.</summary>
    OperationPreview PreviewQuickFormat(PhysicalDiskInfo disk, char driveLetter, FileSystemType fileSystem, string label);

    /// <summary>Build wiping a disk and creating one or more partitions.</summary>
    OperationPreview PreviewRepartition(
        PhysicalDiskInfo disk,
        IReadOnlyList<long> partitionSizesBytes,
        FileSystemType fileSystem,
        IReadOnlyList<string>? volumeLabels = null);

    /// <summary>Run the command plan immediately.</summary>
    Task ExecuteAsync(OperationPreview preview, CancellationToken ct = default);
}
