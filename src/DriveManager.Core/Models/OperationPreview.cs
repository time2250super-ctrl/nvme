namespace DriveManager.Core.Models;

/// <summary>How a plan's steps are executed.</summary>
public enum ExecutionRunner
{
    /// <summary>Not executable.</summary>
    None,
    /// <summary>Steps are PowerShell commands.</summary>
    PowerShell,
    /// <summary>Steps are diskpart script lines.</summary>
    Diskpart,
}

/// <summary>
/// The command plan for a disk operation. The UI runs it immediately; it is not a simulation.
/// </summary>
public sealed record OperationPreview
{
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>The target disk this operation will affect.</summary>
    public required PhysicalDiskInfo TargetDisk { get; init; }

    /// <summary>Exact commands that will run.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsDestructive { get; init; } = true;

    /// <summary>
    /// How <see cref="Steps"/> are run. <see cref="ExecutionRunner.None"/> means
    /// the operation has no runner.
    /// </summary>
    public ExecutionRunner Runner { get; init; } = ExecutionRunner.None;
}
