namespace DriveManager.Core.Models;

public enum DriveOperationKind
{
    SetLabel,
    ChangeDriveLetter,
    RemoveDriveLetter,
    SetDiskOffline,
    SetDiskOnline,
}

/// <summary>
/// A structured, reversible drive-management operation (non-destructive to data). Built via the
/// tools service as a preview first; execution re-derives the exact OS command from these typed
/// parameters (never from free-form strings).
/// </summary>
public sealed record DriveOperation
{
    public required DriveOperationKind Kind { get; init; }
    public required PhysicalDiskInfo Disk { get; init; }

    /// <summary>Existing volume drive letter the operation targets (for volume-scoped kinds).</summary>
    public char? DriveLetter { get; init; }

    /// <summary>New drive letter (for <see cref="DriveOperationKind.ChangeDriveLetter"/>).</summary>
    public char? NewDriveLetter { get; init; }

    /// <summary>New label (for <see cref="DriveOperationKind.SetLabel"/>).</summary>
    public string? NewLabel { get; init; }

    public required OperationPreview Preview { get; init; }
}
