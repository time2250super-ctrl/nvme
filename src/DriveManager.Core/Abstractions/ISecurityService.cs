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
}

/// <summary>
/// Drive security: encryption status and (phased, guarded) secure-erase previews.
/// </summary>
public interface ISecurityService
{
    Task<VolumeSecurityState> GetEncryptionStateAsync(char driveLetter, CancellationToken ct = default);

    /// <summary>Preview an NVMe secure erase / sanitize (crypto erase). Execution is gated like formatting.</summary>
    OperationPreview PreviewSecureErase(PhysicalDiskInfo disk);
}
