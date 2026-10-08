namespace DriveManager.Core.Models;

/// <summary>
/// Describes a mounted volume / partition.
/// </summary>
public sealed record VolumeInfo
{
    public char? DriveLetter { get; init; }
    public string? Label { get; init; }
    public string? FileSystem { get; init; }
    public long SizeBytes { get; init; }
    public long FreeBytes { get; init; }

    public long UsedBytes => Math.Max(0, SizeBytes - FreeBytes);
    public double SizeGb => Math.Round(SizeBytes / 1024d / 1024d / 1024d, 2);
    public double FreeGb => Math.Round(FreeBytes / 1024d / 1024d / 1024d, 2);
    public double UsedGb => Math.Round(UsedBytes / 1024d / 1024d / 1024d, 2);

    public double UsedPercent => SizeBytes == 0 ? 0 : Math.Round(UsedBytes * 100d / SizeBytes, 1);
    public double FreePercent => SizeBytes == 0 ? 0 : Math.Round(FreeBytes * 100d / SizeBytes, 1);
}
