using System.Runtime.Versioning;
using DriveManager.Core.Guards;
using DriveManager.Core.Models;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Central, guarded execution path for destructive operations. Enforces the same safety rules for
/// every caller: system/boot disks are refused, the typed confirmation must match exactly, and
/// previews flagged BLOCKED never run. "What you see in the preview is exactly what runs."
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DestructiveExecutor
{
    public static async Task ExecuteAsync(OperationPreview preview, string typedConfirmation, CancellationToken ct)
    {
        // Hard safety gate: refuses system/boot disks and verifies the typed confirmation phrase.
        DriveGuard.Validate(preview.TargetDisk, typedConfirmation, preview.ConfirmationPhrase);

        if (preview.Warnings.Any(w => w.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This operation is blocked by safety rules and will not run.");

        // Only the non-comment lines are the actual command plan.
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
                throw new InvalidOperationException("This operation is preview-only and cannot be executed.");
        }
    }
}
