using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DriveManager.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Enumerates physical disks via kernel32 IOCTL (no PowerShell, no Storage CIM).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeDiskEnumerator
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlDiskGetLengthInfo = 0x0007405C;
    private const uint IoctlDiskGetDriveLayoutEx = 0x00070050;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    public static List<PhysicalDiskInfo> Query()
    {
        var volumesByDisk = MapVolumesToDisks();
        var systemDisk = volumesByDisk
            .FirstOrDefault(kv => kv.Value.Any(v => v.DriveLetter == GetSystemLetter())).Key;

        var disks = new List<PhysicalDiskInfo>();
        for (var n = 0; n < 16; n++)
        {
            var path = $@"\\.\PhysicalDrive{n}";
            using var handle = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                continue;

            var (name, serial, bus, firmware) = QueryIdentity(handle);
            var size = QueryLength(handle);
            var style = QueryPartitionStyle(handle);
            volumesByDisk.TryGetValue(n, out var vols);
            vols ??= [];

            disks.Add(new PhysicalDiskInfo
            {
                Number = n,
                FriendlyName = string.IsNullOrWhiteSpace(name) ? $"Disk {n}" : name,
                SerialNumber = string.IsNullOrWhiteSpace(serial) ? null : serial,
                Model = name,
                FirmwareRevision = firmware,
                SizeBytes = size,
                BusType = bus,
                PartitionStyle = style,
                IsBoot = n == systemDisk,
                IsSystem = n == systemDisk,
                Volumes = vols,
            });
        }

        return disks;
    }

    private static char GetSystemLetter()
    {
        var sd = Environment.GetEnvironmentVariable("SystemDrive");
        return !string.IsNullOrEmpty(sd) ? char.ToUpperInvariant(sd[0]) : 'C';
    }

    private static (string Name, string Serial, string Bus, string? Firmware) QueryIdentity(SafeFileHandle handle)
    {
        var inSize = 12;
        var outSize = 1024;
        var inBuf = Marshal.AllocHGlobal(inSize);
        var outBuf = Marshal.AllocHGlobal(outSize);
        try
        {
            for (var i = 0; i < inSize; i++) Marshal.WriteByte(inBuf, i, 0);
            for (var i = 0; i < outSize; i++) Marshal.WriteByte(outBuf, i, 0);
            // STORAGE_PROPERTY_QUERY: PropertyId=StorageDeviceProperty(0), QueryType=PropertyStandardQuery(0)
            var ok = DeviceIoControl(handle, IoctlStorageQueryProperty, inBuf, inSize, outBuf, outSize, out _, IntPtr.Zero);
            if (!ok)
                return ("", "", "Unknown", null);

            var vendor = ReadAsciiOffset(outBuf, Marshal.ReadInt32(outBuf, 12), outSize);
            var product = ReadAsciiOffset(outBuf, Marshal.ReadInt32(outBuf, 16), outSize);
            var firmware = ReadAsciiOffset(outBuf, Marshal.ReadInt32(outBuf, 20), outSize);
            var serial = ReadAsciiOffset(outBuf, Marshal.ReadInt32(outBuf, 24), outSize);
            var bus = MapBus(Marshal.ReadByte(outBuf, 28));
            var name = string.Join(" ", new[] { vendor, product }.Where(s => s.Length > 0));
            return (name, serial, bus, firmware.Length == 0 ? null : firmware);
        }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
            Marshal.FreeHGlobal(outBuf);
        }
    }

    private static long QueryLength(SafeFileHandle handle)
    {
        var buf = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.WriteInt64(buf, 0);
            if (!DeviceIoControl(handle, IoctlDiskGetLengthInfo, IntPtr.Zero, 0, buf, 8, out _, IntPtr.Zero))
                return 0;
            return Marshal.ReadInt64(buf);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string QueryPartitionStyle(SafeFileHandle handle)
    {
        const int size = 4096;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            if (!DeviceIoControl(handle, IoctlDiskGetDriveLayoutEx, IntPtr.Zero, 0, buf, size, out _, IntPtr.Zero))
                return "Unknown";
            return Marshal.ReadInt32(buf) switch
            {
                0 => "MBR",
                1 => "GPT",
                2 => "RAW",
                _ => "Unknown",
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static Dictionary<int, List<VolumeInfo>> MapVolumesToDisks()
    {
        var map = new Dictionary<int, List<VolumeInfo>>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not DriveType.Fixed and not DriveType.Removable)
                continue;
            var letter = char.ToUpperInvariant(drive.Name[0]);
            var diskNo = QueryDiskNumberForLetter(letter);
            if (diskNo < 0)
                continue;

            long size = 0, free = 0;
            string? label = null, fs = null;
            try
            {
                if (drive.IsReady)
                {
                    size = drive.TotalSize;
                    free = drive.TotalFreeSpace;
                    label = drive.VolumeLabel;
                    fs = drive.DriveFormat;
                }
            }
            catch { /* volume not ready */ }

            if (!map.TryGetValue(diskNo, out var list))
            {
                list = [];
                map[diskNo] = list;
            }

            list.Add(new VolumeInfo
            {
                DriveLetter = letter,
                Label = label,
                FileSystem = fs,
                SizeBytes = size,
                FreeBytes = free,
            });
        }

        return map;
    }

    private static int QueryDiskNumberForLetter(char letter)
    {
        using var handle = CreateFileW($@"\\.\{letter}:", GenericRead, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return -1;

        const int size = 32;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            if (!DeviceIoControl(handle, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0, buf, size, out _, IntPtr.Zero))
                return -1;
            if (Marshal.ReadInt32(buf) < 1)
                return -1;
            return Marshal.ReadInt32(buf, 8);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string ReadAsciiOffset(IntPtr buf, int offset, int bufSize)
    {
        if (offset <= 0 || offset >= bufSize)
            return "";
        var bytes = new List<byte>();
        for (var i = offset; i < bufSize; i++)
        {
            var b = Marshal.ReadByte(buf, i);
            if (b == 0)
                break;
            if (b is >= 32 and < 127)
                bytes.Add(b);
        }

        return Encoding.ASCII.GetString(bytes.ToArray()).Trim();
    }

    private static string MapBus(byte bus) => bus switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        17 => "NVMe",
        _ => "Unknown",
    };
}
