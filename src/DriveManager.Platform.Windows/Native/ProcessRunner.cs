using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Runs external OS tools (PowerShell, diskpart) with output capture. Throws on a non-zero exit
/// code, surfacing stderr/stdout so failures are visible rather than silent.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessRunner
{
    public static Task RunPowerShellAsync(IReadOnlyList<string> commands, CancellationToken ct)
        => RunPowerShellCaptureAsync(commands, ct);

    /// <summary>Runs PowerShell and returns captured stdout (throws on non-zero exit).</summary>
    public static async Task<string> RunPowerShellCaptureAsync(IReadOnlyList<string> commands, CancellationToken ct)
    {
        var script = "$ErrorActionPreference='Stop';" + Environment.NewLine +
                     string.Join(Environment.NewLine, commands);

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
        psi.ArgumentList.Add(script);

        return await RunAsync(psi, ct).ConfigureAwait(false);
    }

    public static async Task RunDiskpartAsync(IReadOnlyList<string> scriptLines, CancellationToken ct)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"dm_diskpart_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(scriptPath, scriptLines, ct).ConfigureAwait(false);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "diskpart.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add(scriptPath);

            await RunAsync(psi, ct, TimeSpan.FromMinutes(120)).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* temp file */ }
        }
    }

    private static async Task<string> RunAsync(ProcessStartInfo psi, CancellationToken ct, TimeSpan? timeoutLimit = null)
    {
        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var limit = timeoutLimit ?? TimeSpan.FromSeconds(25);
        linkedCts.CancelAfter(limit);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{psi.FileName} timed out after {limit.TotalSeconds:0}s.");
        }

        if (process.ExitCode != 0)
        {
            var detail = stderr.Length > 0 ? stderr.ToString().Trim() : stdout.ToString().Trim();
            throw new InvalidOperationException(
                $"{psi.FileName} exited with code {process.ExitCode}. {detail}");
        }

        return stdout.ToString();
    }
}
