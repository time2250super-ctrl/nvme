using System.Runtime.Versioning;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using Microsoft.Management.Infrastructure;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Enumerates disks and volumes using the Windows Storage Management WMI provider
/// (root/Microsoft/Windows/Storage).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskService : IDiskService
{
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    public Task<IReadOnlyList<PhysicalDiskInfo>> GetDisksAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<PhysicalDiskInfo>>(() => QueryDisks(ct), ct);

    public Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<VolumeInfo>>(() => QueryVolumes(ct), ct);

    public async Task<PhysicalDiskInfo?> GetDiskForVolumeAsync(char driveLetter, CancellationToken ct = default)
    {
        var disks = await GetDisksAsync(ct).ConfigureAwait(false);
        return disks.FirstOrDefault(d => d.Volumes.Any(v =>
            v.DriveLetter.HasValue &&
            char.ToUpperInvariant(v.DriveLetter.Value) == char.ToUpperInvariant(driveLetter)));
    }

    private static List<PhysicalDiskInfo> QueryDisks(CancellationToken ct)
    {
        using var session = CimSession.Create(null);
        var volumesByLetter = QueryVolumes(ct)
            .Where(v => v.DriveLetter.HasValue)
            .ToDictionary(v => char.ToUpperInvariant(v.DriveLetter!.Value));

        // Map disk number -> drive letters via partitions.
        var lettersByDisk = new Dictionary<int, List<char>>();
        foreach (var part in session.QueryInstances(StorageNamespace, "WQL", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
        {
            ct.ThrowIfCancellationRequested();
            var diskNo = GetInt(part, "DiskNumber");
            var letterObj = part.CimInstanceProperties["DriveLetter"]?.Value;
            if (diskNo < 0 || letterObj is null) continue;
            var ch = Convert.ToChar(letterObj);
            if (ch == '\0' || char.IsWhiteSpace(ch)) continue;
            if (!lettersByDisk.TryGetValue(diskNo, out var list))
                lettersByDisk[diskNo] = list = [];
            list.Add(char.ToUpperInvariant(ch));
        }

        var disks = new List<PhysicalDiskInfo>();
        foreach (var d in session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_Disk"))
        {
            ct.ThrowIfCancellationRequested();
            var number = GetInt(d, "Number");
            var letters = lettersByDisk.TryGetValue(number, out var l) ? l : [];
            var vols = letters
                .Where(volumesByLetter.ContainsKey)
                .Select(ch => volumesByLetter[ch])
                .ToList();

            disks.Add(new PhysicalDiskInfo
            {
                Number = number,
                FriendlyName = GetString(d, "FriendlyName") ?? GetString(d, "Model") ?? $"Disk {number}",
                Model = GetString(d, "Model"),
                SerialNumber = GetString(d, "SerialNumber")?.Trim(),
                FirmwareRevision = GetString(d, "FirmwareVersion"),
                SizeBytes = GetLong(d, "Size"),
                BusType = MapBusType(GetInt(d, "BusType")),
                PartitionStyle = MapPartitionStyle(GetInt(d, "PartitionStyle")),
                IsBoot = GetBool(d, "IsBoot"),
                IsSystem = GetBool(d, "IsSystem"),
                Volumes = vols,
            });
        }

        return disks.OrderBy(d => d.Number).ToList();
    }

    private static List<VolumeInfo> QueryVolumes(CancellationToken ct)
    {
        using var session = CimSession.Create(null);
        var result = new List<VolumeInfo>();
        foreach (var v in session.QueryInstances(StorageNamespace, "WQL", "SELECT * FROM MSFT_Volume"))
        {
            ct.ThrowIfCancellationRequested();
            var letterObj = v.CimInstanceProperties["DriveLetter"]?.Value;
            char? letter = null;
            if (letterObj is not null)
            {
                var ch = Convert.ToChar(letterObj);
                if (ch != '\0' && !char.IsWhiteSpace(ch)) letter = char.ToUpperInvariant(ch);
            }

            result.Add(new VolumeInfo
            {
                DriveLetter = letter,
                Label = GetString(v, "FileSystemLabel"),
                FileSystem = GetString(v, "FileSystem"),
                SizeBytes = GetLong(v, "Size"),
                FreeBytes = GetLong(v, "SizeRemaining"),
            });
        }

        return result;
    }

    private static string MapBusType(int value) => value switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA",
        6 => "FibreChannel", 7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS",
        11 => "SATA", 12 => "SD", 13 => "MMC", 14 => "Virtual",
        15 => "FileBackedVirtual", 16 => "StorageSpaces", 17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => "Unknown",
    };

    private static string MapPartitionStyle(int value) => value switch
    {
        1 => "MBR", 2 => "GPT", _ => "Unknown",
    };

    private static string? GetString(CimInstance i, string name)
        => i.CimInstanceProperties[name]?.Value as string;

    private static int GetInt(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is null ? -1 : Convert.ToInt32(v);
    }

    private static long GetLong(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is null ? 0 : Convert.ToInt64(v);
    }

    private static bool GetBool(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is bool b && b;
    }
}
