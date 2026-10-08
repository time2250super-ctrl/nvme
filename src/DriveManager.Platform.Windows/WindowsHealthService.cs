using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows.Native;

namespace DriveManager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsHealthService : IHealthService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<DriveHealthInfo?> ReadHealthAsync(PhysicalDiskInfo disk, CancellationToken ct = default)
        => Task.Run<DriveHealthInfo?>(() =>
        {
            // NVMe: rich SMART health log page.
            if (disk.IsNvme)
            {
                var nvme = NvmeHealthReader.TryRead(disk.Number);
                if (nvme is not null)
                    return DriveHealthInfo.FromNvme(nvme);
            }

            // Everything else (SATA / USB / RAID): OS reliability counters + predict-failure.
            return GenericHealthReader.Read(disk);
        }, ct);
}
