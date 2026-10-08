using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows.Native;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Enumerates disks via kernel32 IOCTL. PowerShell Get-Disk / Storage CIM are not used (they hang on this host).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskService : IDiskService
{
    public Task<IReadOnlyList<PhysicalDiskInfo>> GetDisksAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<PhysicalDiskInfo>>(() =>
        {
            var disks = NativeDiskEnumerator.Query();
            TryLog($"disks {disks.Count}: " + string.Join("; ",
                disks.Select(d => $"#{d.Number} {d.FriendlyName} {d.BusType} {d.SizeGb}GB")));
            return disks;
        }, ct);

    public Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<VolumeInfo>>(() =>
            NativeDiskEnumerator.Query().SelectMany(d => d.Volumes).ToList(), ct);

    public async Task<PhysicalDiskInfo?> GetDiskForVolumeAsync(char driveLetter, CancellationToken ct = default)
    {
        var disks = await GetDisksAsync(ct).ConfigureAwait(false);
        return disks.FirstOrDefault(d => d.Volumes.Any(v =>
            v.DriveLetter.HasValue &&
            char.ToUpperInvariant(v.DriveLetter.Value) == char.ToUpperInvariant(driveLetter)));
    }

    private static void TryLog(string line)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"),
                DateTime.Now.ToString("o") + " " + line + Environment.NewLine);
        }
        catch { /* never block scan on logging */ }
    }
}
