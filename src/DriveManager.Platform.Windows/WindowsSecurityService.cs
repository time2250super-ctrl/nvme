using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows.Native;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Drive security on Windows: live BitLocker status, enable/disable encryption, and
/// full-disk secure erase. Actions run immediately.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecurityService : ISecurityService
{
    public async Task<VolumeSecurityState> GetEncryptionStateAsync(char driveLetter, CancellationToken ct = default)
    {
        var letter = char.ToUpperInvariant(driveLetter);
        var script = new[]
        {
            "try {",
            $"  $v = Get-BitLockerVolume -MountPoint \"{letter}:\" -ErrorAction Stop",
            "  'STATUS=' + $v.VolumeStatus",
            "  'PROTECT=' + $v.ProtectionStatus",
            "  'METHOD=' + $v.EncryptionMethod",
            "  'PERCENT=' + $v.EncryptionPercentage",
            "  $rp = ($v.KeyProtector | Where-Object { $_.KeyProtectorType -eq 'RecoveryPassword' } | Select-Object -First 1).RecoveryPassword",
            "  'RECOVERY=' + $rp",
            "} catch { 'STATUS=Unavailable' }",
        };

        try
        {
            var output = await ProcessRunner.RunPowerShellCaptureAsync(script, ct).ConfigureAwait(false);
            return Parse(letter, output);
        }
        catch
        {
            return new VolumeSecurityState { DriveLetter = letter, Encryption = EncryptionStatus.Unknown };
        }
    }

    private static VolumeSecurityState Parse(char letter, string output)
    {
        string? Get(string key) => output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            ?[(key.Length + 1)..].Trim();

        var status = Get("STATUS");
        var encryption = status switch
        {
            "FullyEncrypted" => EncryptionStatus.Encrypted,
            "FullyDecrypted" => EncryptionStatus.NotEncrypted,
            "EncryptionInProgress" => EncryptionStatus.Encrypting,
            "DecryptionInProgress" => EncryptionStatus.Decrypting,
            _ => EncryptionStatus.Unknown,
        };

        double? percent = double.TryParse(Get("PERCENT"), out var p) ? p : null;
        var method = Get("METHOD");
        if (string.IsNullOrWhiteSpace(method) || method == "None") method = null;
        var protect = Get("PROTECT");

        return new VolumeSecurityState
        {
            DriveLetter = letter,
            Encryption = encryption,
            Method = method,
            ProtectionOn = protect is null ? null : protect.Equals("On", StringComparison.OrdinalIgnoreCase),
            EncryptionPercent = percent,
            RecoveryPassword = string.IsNullOrWhiteSpace(Get("RECOVERY")) ? null : Get("RECOVERY"),
        };
    }

    public OperationPreview PreviewSecureErase(PhysicalDiskInfo disk)
    {
        return new OperationPreview
        {
            Title = $"Secure erase of disk {disk.Number}",
            Description = $"Overwrites every sector of disk {disk.Number} ('{disk.FriendlyName}') with zeros (diskpart 'clean all'), " +
                          "destroying all partitions and data. (A hardware NVMe cryptographic erase requires vendor tooling and is not used here.)",
            TargetDisk = disk,
            IsDestructive = true,
            Runner = ExecutionRunner.Diskpart,
            Steps =
            [
                "# diskpart script - full-disk zero overwrite",
                $"select disk {disk.Number}",
                "clean all",
            ],
            Warnings =
            [
                "Secure erase irreversibly destroys ALL data and partitions on the entire physical disk.",
                "This performs a full single-pass overwrite of every sector; it can take a long time on large drives.",
                "The drive will need a new partition/format afterwards before it can be used.",
            ],
        };
    }

    public Task ExecuteAsync(OperationPreview preview, CancellationToken ct = default)
        => DestructiveExecutor.ExecuteAsync(preview, ct);

    public OperationPreview PreviewEnableEncryption(char driveLetter)
    {
        var letter = char.ToUpperInvariant(driveLetter);
        return new OperationPreview
        {
            Title = $"Enable BitLocker on {letter}:",
            Description = $"Turns on BitLocker (XTS-AES-256) for volume {letter}: and adds a recovery-password protector. " +
                          "Your data is NOT erased; the volume is encrypted in place.",
            TargetDisk = null!,
            IsDestructive = false,
            Runner = ExecutionRunner.PowerShell,
            Steps =
            [
                $"Enable-BitLocker -MountPoint \"{letter}:\" -EncryptionMethod XtsAes256 -RecoveryPasswordProtector -UsedSpaceOnly -SkipHardwareTest",
            ],
            Warnings =
            [
                "SAVE YOUR RECOVERY KEY after enabling - it is shown in the Security tab and is the only way to recover data if locked out.",
                "On the OS/boot drive a TPM or startup key may also be required to boot.",
            ],
        };
    }

    public OperationPreview PreviewDisableEncryption(char driveLetter)
    {
        var letter = char.ToUpperInvariant(driveLetter);
        return new OperationPreview
        {
            Title = $"Disable BitLocker on {letter}:",
            Description = $"Decrypts volume {letter}: and turns BitLocker protection off. Data is preserved.",
            TargetDisk = null!,
            IsDestructive = false,
            Runner = ExecutionRunner.PowerShell,
            Steps = [$"Disable-BitLocker -MountPoint \"{letter}:\""],
            Warnings = ["Decryption can take a while; the volume stays usable during the process."],
        };
    }

    public async Task ExecuteEncryptionAsync(OperationPreview preview, CancellationToken ct = default)
    {
        var commands = preview.Steps
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.TrimStart().StartsWith('#'))
            .ToList();

        await ProcessRunner.RunPowerShellAsync(commands, ct).ConfigureAwait(false);
    }
}
