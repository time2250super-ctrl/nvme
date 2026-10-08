namespace DriveManager.Core.Models;

/// <summary>How a preview's steps are executed once confirmed.</summary>
public enum ExecutionRunner
{
    /// <summary>Not executable (preview only).</summary>
    None,
    /// <summary>Steps are PowerShell commands.</summary>
    PowerShell,
    /// <summary>Steps are diskpart script lines.</summary>
    Diskpart,
}

/// <summary>
/// A dry-run description of a destructive operation (format, partition, secure erase).
/// Nothing is executed until the user confirms with a matching token.
/// </summary>
public sealed record OperationPreview
{
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>The target disk this operation will affect.</summary>
    public required PhysicalDiskInfo TargetDisk { get; init; }

    /// <summary>Exact commands / steps that will run, shown to the user before execution.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsDestructive { get; init; } = true;

    /// <summary>
    /// How <see cref="Steps"/> are run on confirmation. <see cref="ExecutionRunner.None"/> means
    /// the operation is preview-only (not wired for execution).
    /// </summary>
    public ExecutionRunner Runner { get; init; } = ExecutionRunner.None;

    /// <summary>
    /// The phrase the user must type to confirm. Defaults to the target disk identity so
    /// confirmation cannot be given for the wrong device by muscle memory.
    /// </summary>
    public string ConfirmationPhrase { get; init; } = string.Empty;
}
