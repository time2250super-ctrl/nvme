namespace DriveManager.Core.Models;

public sealed record FilesystemAdvice(string Recommended, string Rationale)
{
    /// <summary>True when the volume's current filesystem already matches the recommendation.</summary>
    public bool MatchesCurrent(string? currentFileSystem)
        => string.Equals(currentFileSystem?.Trim(), Recommended, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Recommends the best filesystem for a volume given the drive type and size. Rules of thumb:
/// internal fixed drives → NTFS; large removable → exFAT; small removable → FAT32 (max
/// compatibility, with the 4 GB per-file caveat).
/// </summary>
public static class FilesystemAdvisor
{
    private const long Gb = 1024L * 1024 * 1024;

    public static FilesystemAdvice Recommend(string busType, long sizeBytes, bool isSystemOrBoot)
    {
        if (isSystemOrBoot)
            return new FilesystemAdvice("NTFS", "System/boot volume - Windows requires NTFS (journaling, permissions, boot support).");

        var removable = busType is "USB" or "SD" or "MMC";

        if (!removable)
            return new FilesystemAdvice("NTFS",
                "Internal fixed drive - NTFS gives journaling, large-file support, compression and permissions.");

        // Removable media.
        if (sizeBytes <= 32 * Gb)
            return new FilesystemAdvice("FAT32",
                "Small removable drive - FAT32 is the most universally compatible (cameras, consoles, BIOS). Note the 4 GB per-file limit.");

        return new FilesystemAdvice("exFAT",
            "Large removable drive - exFAT is cross-platform (Windows/macOS/Linux) with no practical file-size limit.");
    }
}
