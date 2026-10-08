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
/// Drive security: encryption status, BitLocker enable/disable, and full-disk secure erase.
/// </summary>
public interface ISecurityService
{
    Task<VolumeSecurityState> GetEncryptionStateAsync(char driveLetter, CancellationToken ct = default);

    /// <summary>Build a full-disk secure erase command plan.</summary>
    OperationPreview PreviewSecureErase(PhysicalDiskInfo disk);

    /// <summary>Run a secure-erase command plan immediately.</summary>
    Task ExecuteAsync(OperationPreview preview, CancellationToken ct = default);

    /// <summary>Build turning BitLocker encryption ON for a volume.</summary>
    OperationPreview PreviewEnableEncryption(char driveLetter);

    /// <summary>Build turning BitLocker encryption OFF (decrypt) for a volume.</summary>
    OperationPreview PreviewDisableEncryption(char driveLetter);

    /// <summary>Run an encryption enable/disable command plan immediately.</summary>
    Task ExecuteEncryptionAsync(OperationPreview preview, CancellationToken ct = default);
}
