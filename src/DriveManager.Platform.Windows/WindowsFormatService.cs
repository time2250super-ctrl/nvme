using System.Runtime.Versioning;
using System.Text.RegularExpressions;
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

    public OperationPreview PreviewRepartition(
        PhysicalDiskInfo disk,
        IReadOnlyList<long> partitionSizesBytes,
        FileSystemType fileSystem,
        IReadOnlyList<string>? volumeLabels = null)
    {
        if (partitionSizesBytes.Count == 0)
            throw new ArgumentException("At least one partition size is required.", nameof(partitionSizesBytes));

        ValidatePartitionSizes(disk, partitionSizesBytes);

        var fs = ToDiskpartFs(fileSystem);
        var steps = new List<string>
        {
            "# diskpart script",
            $"select disk {disk.Number}",
            "online disk",
            "attributes disk clear readonly",
            "clean",
            "convert gpt",
        };

        for (var idx = 0; idx < partitionSizesBytes.Count; idx++)
        {
            var isLast = idx == partitionSizesBytes.Count - 1;
            if (isLast)
                steps.Add("create partition primary");
            else
            {
                var sizeMb = Math.Max(1, partitionSizesBytes[idx] / (1024 * 1024));
                steps.Add($"create partition primary size={sizeMb}");
            }

            var label = ResolveLabel(volumeLabels, idx);
            steps.Add($"format fs={fs} label={label} quick");
            steps.Add("assign");
        }

        var layout = DescribeLayout(partitionSizesBytes, volumeLabels);
        return new OperationPreview
        {
            Title = $"Repartition disk {disk.Number} into {partitionSizesBytes.Count} partition(s)",
            Description = $"Erases ALL partitions on disk {disk.Number} ('{disk.FriendlyName}') and recreates:{Environment.NewLine}{layout}",
            TargetDisk = disk,
            IsDestructive = true,
            Runner = ExecutionRunner.Diskpart,
            Steps = steps,
            Warnings =
            [
                "This operation is destructive and cannot be undone.",
                "The disk is wiped (clean) before new partitions are created.",
            ],
        };
    }

    public Task ExecuteAsync(OperationPreview preview, CancellationToken ct = default)
        => DestructiveExecutor.ExecuteAsync(preview, ct);

    private static void ValidatePartitionSizes(PhysicalDiskInfo disk, IReadOnlyList<long> partitionSizesBytes)
    {
        if (partitionSizesBytes.Count == 1)
            return;

        long fixedTotal = 0;
        for (var i = 0; i < partitionSizesBytes.Count - 1; i++)
        {
            if (partitionSizesBytes[i] <= 0)
                throw new ArgumentException("Partition sizes must be positive.", nameof(partitionSizesBytes));
            fixedTotal += partitionSizesBytes[i];
        }

        var last = partitionSizesBytes[^1];
        if (last <= 0)
            throw new ArgumentException("The final partition must use the remaining space (positive size).", nameof(partitionSizesBytes));

        if (fixedTotal + last > disk.SizeBytes)
            throw new ArgumentException("Partition sizes exceed the disk capacity.", nameof(partitionSizesBytes));

        const long minLast = 64L * 1024 * 1024;
        if (last < minLast)
            throw new ArgumentException("Not enough space left for the final partition.", nameof(partitionSizesBytes));
    }

    private static string DescribeLayout(IReadOnlyList<long> sizes, IReadOnlyList<string>? labels)
    {
        var lines = new List<string>();
        for (var i = 0; i < sizes.Count; i++)
        {
            var gb = sizes[i] / 1024d / 1024d / 1024d;
            var label = ResolveLabel(labels, i);
            var sizeText = i == sizes.Count - 1 && sizes.Count > 1
                ? $"~{gb:0.00} GB (remaining)"
                : $"{gb:0.00} GB";
            lines.Add($"  {i + 1}. {label} — {sizeText}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveLabel(IReadOnlyList<string>? labels, int index)
    {
        if (labels is not null && index < labels.Count && !string.IsNullOrWhiteSpace(labels[index]))
            return SanitizeDiskpartLabel(labels[index]);
        return index == 0 ? "DriveMgr" : "Data";
    }

    private static string SanitizeDiskpartLabel(string label)
    {
        var trimmed = label.Trim();
        if (trimmed.Length == 0) return "Volume";
        var safe = Regex.Replace(trimmed, @"[^\w\-]", "");
        if (safe.Length == 0) safe = "Volume";
        return safe.Length > 32 ? safe[..32] : safe;
    }

    private static string ToDiskpartFs(FileSystemType fileSystem) => fileSystem switch
    {
        FileSystemType.ExFat => "exfat",
        FileSystemType.Fat32 => "fat32",
        _ => "ntfs",
    };
}
