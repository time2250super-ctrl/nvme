using DriveManager.Core.Models;

namespace DriveManager.Core.Abstractions;

public enum EncryptionStatus
{
    Unknown,
    NotEncrypted,
    Encrypting,
    Encrypted,
    Decrypting,
}

public sealed record VolumeSecurityState
{
    public char? DriveLetter { get; init; }
    public EncryptionStatus Encryption { get; init; }
    public string? Method { get; init; }

    /// <summary>True if BitLocker protection is actively on.</summary>
    public bool? ProtectionOn { get; init; }

    /// <summary>Encryption progress percentage (0-100).</summary>
    public double? EncryptionPercent { get; init; }

    /// <summary>The 48-digit recovery password, if a recovery protector exists.</summary>
    public string? RecoveryPassword { get; init; }

    public string Summary => Encryption switch
    {
        EncryptionStatus.Encrypted => $"Encrypted{(Method is null ? "" : $" ({Method})")}, protection {(ProtectionOn == true ? "ON" : "OFF")}.",
        EncryptionStatus.Encrypting => $"Encrypting... {EncryptionPercent:0.#}% complete.",
        EncryptionStatus.Decrypting => $"Decrypting... {EncryptionPercent:0.#}% remaining.",
        EncryptionStatus.NotEncrypted => "Not encrypted.",
        _ => "Encryption status unknown.",
    };
}

/// <summary>
/// Drive security: encryption status and (phased, guarded) secure-erase previews.
/// </summary>
public interface ISecurityService
{
    Task<VolumeSecurityState> GetEncryptionStateAsync(char driveLetter, CancellationToken ct = default);

    /// <summary>Preview a full-disk secure erase. Execution is gated like formatting.</summary>
    OperationPreview PreviewSecureErase(PhysicalDiskInfo disk);

    /// <summary>
    /// Execute a previously generated secure-erase preview. The <paramref name="typedConfirmation"/>
    /// MUST match the preview's confirmation phrase exactly or the call throws.
    /// </summary>
    Task ExecuteAsync(OperationPreview preview, string typedConfirmation, CancellationToken ct = default);

    /// <summary>Preview turning BitLocker encryption ON for a volume (non-destructive to data).</summary>
    OperationPreview PreviewEnableEncryption(char driveLetter);

    /// <summary>Preview turning BitLocker encryption OFF (decrypt) for a volume.</summary>
    OperationPreview PreviewDisableEncryption(char driveLetter);

    /// <summary>
    /// Execute an encryption enable/disable preview. The <paramref name="typedConfirmation"/> MUST
    /// match the preview's confirmation phrase. Not data-destructive, so system disks are allowed.
    /// </summary>
    Task ExecuteEncryptionAsync(OperationPreview preview, string typedConfirmation, CancellationToken ct = default);
}
