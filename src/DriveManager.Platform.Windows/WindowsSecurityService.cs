using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Guards;
using DriveManager.Core.Models;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Drive security on Windows (BitLocker status + NVMe secure-erase previews). Reads are safe;
/// destructive previews follow the same confirmation model as formatting.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecurityService : ISecurityService
{
    public Task<VolumeSecurityState> GetEncryptionStateAsync(char driveLetter, CancellationToken ct = default)
    {
        // Placeholder read until Win32_EncryptableVolume / manage-bde integration is wired.
        return Task.FromResult(new VolumeSecurityState
        {
            DriveLetter = char.ToUpperInvariant(driveLetter),
            Encryption = EncryptionStatus.Unknown,
            Method = null,
        });
    }

    public OperationPreview PreviewSecureErase(PhysicalDiskInfo disk)
    {
        var guard = DriveGuard.EnsureSafeTarget(disk);
        var warnings = new List<string>
        {
            "Secure erase irreversibly destroys ALL data on the entire physical disk.",
            "The drive may be briefly unavailable and could require a reboot.",
        };
        if (!guard.Allowed)
            warnings.Insert(0, "BLOCKED: " + guard.Reason);

        return new OperationPreview
        {
            Title = $"NVMe secure erase of disk {disk.Number}",
            Description = $"Issues an NVMe Format/Sanitize (crypto erase) to disk {disk.Number} ('{disk.FriendlyName}'), wiping every partition.",
            TargetDisk = disk,
            IsDestructive = true,
            ConfirmationPhrase = DriveGuard.BuildConfirmationPhrase(disk),
            Steps =
            [
                "# Planned (not yet executed):",
                "# NVMe Format NVM admin command with Secure Erase Settings = 2 (cryptographic erase)",
            ],
            Warnings = warnings,
        };
    }
}
