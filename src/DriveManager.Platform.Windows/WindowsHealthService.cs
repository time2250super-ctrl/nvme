using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows.Native;

namespace DriveManager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsHealthService : IHealthService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<NvmeHealth?> ReadHealthAsync(PhysicalDiskInfo disk, CancellationToken ct = default)
        => Task.Run(() => NvmeHealthReader.TryRead(disk.Number), ct);
}
