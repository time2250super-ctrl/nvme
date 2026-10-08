using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows.Native;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Formats and repartitions on Windows by running PowerShell / diskpart immediately.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFormatService : IFormatService
{
    public OperationPreview PreviewQuickFormat(PhysicalDiskInfo disk, char driveLetter, FileSystemType fileSystem, string label)
    {
        var fs = fileSystem.ToString().ToUpperInvariant();
        var letter = char.ToUpperInvariant(driveLetter);

        return new OperationPreview
        {
            Title = $"Quick format {letter}: as {fs}",
            Description = $"Quick-formats volume {letter}: on disk {disk.Number} ('{disk.FriendlyName}'). All data on {letter}: is erased.",
            TargetDisk = disk,
            IsDestructive = true,
            Runner = ExecutionRunner.PowerShell,
            Steps =
            [
                $"# PowerShell",
                $"Format-Volume -DriveLetter {letter} -FileSystem {fs} -NewFileSystemLabel \"{label}\" -Force -Confirm:$false",
            ],
            Warnings =
            [
                "This operation is destructive and cannot be undone.",
            ],
        };
    }

    public OperationPreview PreviewRepartition(PhysicalDiskInfo disk, IReadOnlyList<long> partitionSizesBytes, FileSystemType fileSystem)
    {
        var fs = fileSystem.ToString().ToUpperInvariant();

        var steps = new List<string>
        {
            "# diskpart script",
            $"select disk {disk.Number}",
            "clean",
            "convert gpt",
        };

        for (int idx = 0; idx < partitionSizesBytes.Count; idx++)
        {
            var sizeMb = partitionSizesBytes[idx] / (1024 * 1024);
            var isLast = idx == partitionSizesBytes.Count - 1;
            steps.Add(isLast
                ? "create partition primary"
                : $"create partition primary size={sizeMb}");
            var label = idx == 0 ? "DriveMgr" : "Data";
            steps.Add($"format fs={fs.ToLowerInvariant()} label={label} quick");
            steps.Add("assign");
        }

        return new OperationPreview
        {
            Title = $"Repartition disk {disk.Number} into {partitionSizesBytes.Count} partition(s)",
            Description = $"Erases ALL partitions on disk {disk.Number} ('{disk.FriendlyName}') and recreates the layout below.",
            TargetDisk = disk,
            IsDestructive = true,
            Runner = ExecutionRunner.Diskpart,
            Steps = steps,
            Warnings =
            [
                "This operation is destructive and cannot be undone.",
            ],
        };
    }

    public Task ExecuteAsync(OperationPreview preview, CancellationToken ct = default)
        => DestructiveExecutor.ExecuteAsync(preview, ct);
}
