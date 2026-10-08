using System.Runtime.Versioning;
using DriveManager.Core.Guards;
using DriveManager.Core.Models;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Runs a command plan (PowerShell or diskpart) exactly as listed in <see cref="OperationPreview.Steps"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DestructiveExecutor
{
    public static async Task ExecuteAsync(OperationPreview preview, CancellationToken ct)
    {
        BootDriveGuard.RequireDestructiveAllowed(preview.TargetDisk);

        var commands = preview.Steps
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.TrimStart().StartsWith('#'))
            .ToList();

        if (commands.Count == 0)
            throw new InvalidOperationException("This operation has no executable plan.");

        switch (preview.Runner)
        {
            case ExecutionRunner.PowerShell:
                await ProcessRunner.RunPowerShellAsync(commands, ct).ConfigureAwait(false);
                break;
            case ExecutionRunner.Diskpart:
                await ProcessRunner.RunDiskpartAsync(commands, ct).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("This operation has no runner and cannot be executed.");
        }
    }
}
