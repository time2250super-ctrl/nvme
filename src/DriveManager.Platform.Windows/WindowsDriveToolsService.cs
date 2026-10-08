using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Guards;
using DriveManager.Core.Models;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Non-destructive drive management on Windows, implemented by wrapping proven Storage cmdlets
/// (Set-Volume, Set-Partition, Remove-PartitionAccessPath, Set-Disk). Previews are built first;
/// execution re-derives the exact command from typed parameters. System/boot disks are protected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDriveToolsService : IDriveToolsService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    // ---- Builders (previews) -------------------------------------------------

    public DriveOperation BuildSetLabel(PhysicalDiskInfo disk, char driveLetter, string newLabel)
    {
        var letter = NormalizeLetter(driveLetter);
        var warnings = new List<string>();
        if (disk.IsSystem || disk.IsBoot)
            warnings.Add("This volume is on the OS/boot disk; renaming is allowed but affects the system drive.");

        var preview = new OperationPreview
        {
            Title = $"Rename {letter}: to \"{newLabel}\"",
            Description = $"Changes the volume label of {letter}: on disk {disk.Number} ('{disk.FriendlyName}'). No data is affected.",
            TargetDisk = disk,
            IsDestructive = false,
            ConfirmationPhrase = string.Empty,
            Steps = [$"Set-Volume -DriveLetter {letter} -NewFileSystemLabel \"{newLabel}\""],
            Warnings = warnings,
        };

        return new DriveOperation
        {
            Kind = DriveOperationKind.SetLabel,
            Disk = disk,
            DriveLetter = letter,
            NewLabel = newLabel,
            Preview = preview,
        };
    }

    public DriveOperation BuildChangeDriveLetter(PhysicalDiskInfo disk, char currentLetter, char newLetter)
    {
        var from = NormalizeLetter(currentLetter);
        var to = NormalizeLetter(newLetter);
        var blocked = disk.IsSystem || disk.IsBoot;

        var preview = new OperationPreview
        {
            Title = $"Change drive letter {from}: -> {to}:",
            Description = $"Reassigns the drive letter of {from}: (disk {disk.Number}, '{disk.FriendlyName}') to {to}:. No data is affected.",
            TargetDisk = disk,
            IsDestructive = false,
            ConfirmationPhrase = string.Empty,
            Steps = [$"Set-Partition -DriveLetter {from} -NewDriveLetter {to}"],
            Warnings = blocked
                ? ["BLOCKED: this volume is on the OS/boot disk; changing its drive letter is not permitted."]
                : ["Apps referencing the old letter may need reconfiguration."],
        };

        return new DriveOperation
        {
            Kind = DriveOperationKind.ChangeDriveLetter,
            Disk = disk,
            DriveLetter = from,
            NewDriveLetter = to,
            Preview = preview,
        };
    }

    public DriveOperation BuildRemoveDriveLetter(PhysicalDiskInfo disk, char driveLetter)
    {
        var letter = NormalizeLetter(driveLetter);
        var blocked = disk.IsSystem || disk.IsBoot;

        var preview = new OperationPreview
        {
            Title = $"Remove drive letter {letter}:",
            Description = $"Removes the {letter}: mount point (disk {disk.Number}, '{disk.FriendlyName}'). The volume and its data remain, just unmounted.",
            TargetDisk = disk,
            IsDestructive = false,
            ConfirmationPhrase = string.Empty,
            Steps = [$"Get-Partition -DriveLetter {letter} | Remove-PartitionAccessPath -AccessPath \"{letter}:\\\""],
            Warnings = blocked
                ? ["BLOCKED: this volume is on the OS/boot disk; removing its drive letter is not permitted."]
                : ["The volume will no longer be accessible by letter until a letter is reassigned."],
        };

        return new DriveOperation
        {
            Kind = DriveOperationKind.RemoveDriveLetter,
            Disk = disk,
            DriveLetter = letter,
            Preview = preview,
        };
    }

    public DriveOperation BuildSetDiskOffline(PhysicalDiskInfo disk, bool offline)
    {
        var blocked = (disk.IsSystem || disk.IsBoot) && offline;
        var kind = offline ? DriveOperationKind.SetDiskOffline : DriveOperationKind.SetDiskOnline;
        var phrase = offline ? BuildOfflinePhrase(disk) : string.Empty;

        var warnings = new List<string>();
        if (blocked)
            warnings.Add("BLOCKED: the OS/boot disk cannot be taken offline.");
        else if (offline)
            warnings.Add("Taking a disk offline unmounts all its volumes until it is brought back online. Reversible.");

        var preview = new OperationPreview
        {
            Title = offline ? $"Take disk {disk.Number} OFFLINE" : $"Bring disk {disk.Number} ONLINE",
            Description = $"Sets disk {disk.Number} ('{disk.FriendlyName}') {(offline ? "offline" : "online")}. No data is erased.",
            TargetDisk = disk,
            IsDestructive = false,
            ConfirmationPhrase = phrase,
            Steps = [$"Set-Disk -Number {disk.Number} -IsOffline ${offline.ToString().ToLowerInvariant()}"],
            Warnings = warnings,
        };

        return new DriveOperation
        {
            Kind = kind,
            Disk = disk,
            Preview = preview,
        };
    }

    // ---- Execution -----------------------------------------------------------

    public async Task ExecuteAsync(DriveOperation op, string typedConfirmation = "", CancellationToken ct = default)
    {
        var disk = op.Disk;

        // Guardrails.
        switch (op.Kind)
        {
            case DriveOperationKind.ChangeDriveLetter:
            case DriveOperationKind.RemoveDriveLetter:
                if (disk.IsSystem || disk.IsBoot)
                    throw new InvalidOperationException("Operation not permitted on the OS/boot disk.");
                break;

            case DriveOperationKind.SetDiskOffline:
                if (disk.IsSystem || disk.IsBoot)
                    throw new InvalidOperationException("The OS/boot disk cannot be taken offline.");
                var expected = BuildOfflinePhrase(disk);
                if (!string.Equals(typedConfirmation?.Trim(), expected, StringComparison.Ordinal))
                    throw new InvalidOperationException("Confirmation phrase does not match. Aborting taking the disk offline.");
                break;
        }

        var command = BuildCommand(op);
        await RunPowerShellAsync(command, ct).ConfigureAwait(false);
    }

    private static string BuildCommand(DriveOperation op) => op.Kind switch
    {
        DriveOperationKind.SetLabel =>
            $"Set-Volume -DriveLetter {RequireLetter(op.DriveLetter)} -NewFileSystemLabel \"{EscapeLabel(op.NewLabel)}\"",
        DriveOperationKind.ChangeDriveLetter =>
            $"Set-Partition -DriveLetter {RequireLetter(op.DriveLetter)} -NewDriveLetter {RequireLetter(op.NewDriveLetter)}",
        DriveOperationKind.RemoveDriveLetter =>
            $"Get-Partition -DriveLetter {RequireLetter(op.DriveLetter)} | Remove-PartitionAccessPath -AccessPath \"{RequireLetter(op.DriveLetter)}:\\\"",
        DriveOperationKind.SetDiskOffline =>
            $"Set-Disk -Number {op.Disk.Number} -IsOffline $true",
        DriveOperationKind.SetDiskOnline =>
            $"Set-Disk -Number {op.Disk.Number} -IsOffline $false",
        _ => throw new NotSupportedException($"Unsupported operation kind: {op.Kind}"),
    };

    private static async Task RunPowerShellAsync(string command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add($"$ErrorActionPreference='Stop'; {command}");

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginErrorReadLine();
        _ = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Drive operation failed (exit {process.ExitCode}). {stderr.ToString().Trim()}");
    }

    // ---- Helpers -------------------------------------------------------------

    private static string BuildOfflinePhrase(PhysicalDiskInfo disk)
    {
        var serial = string.IsNullOrWhiteSpace(disk.SerialNumber) ? "NOSERIAL" : disk.SerialNumber!.Trim();
        return $"OFFLINE DISK {disk.Number} {serial}";
    }

    private static char NormalizeLetter(char letter)
    {
        var c = char.ToUpperInvariant(letter);
        if (c < 'A' || c > 'Z')
            throw new ArgumentException($"'{letter}' is not a valid drive letter.", nameof(letter));
        return c;
    }

    private static char RequireLetter(char? letter)
        => letter is null ? throw new InvalidOperationException("A drive letter is required for this operation.") : NormalizeLetter(letter.Value);

    private static string EscapeLabel(string? label)
        => (label ?? string.Empty).Replace("\"", "'");
}
