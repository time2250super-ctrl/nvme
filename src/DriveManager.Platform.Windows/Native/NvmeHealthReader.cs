using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DriveManager.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Reads the NVMe SMART / Health Information log page (0x02) directly from a physical disk
/// using IOCTL_STORAGE_QUERY_PROPERTY with StorageDeviceProtocolSpecificProperty.
/// Requires administrator privileges.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NvmeHealthReader
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int PropertyStandardQuery = 0;
    private const int ProtocolTypeNvme = 3;
    private const uint NVMeDataTypeLogPage = 2;
    private const uint NvmeLogPageHealth = 0x02;
    private const int LogPageSize = 512;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct STORAGE_PROTOCOL_SPECIFIC_DATA
    {
        public int ProtocolType;
        public uint DataType;
        public uint ProtocolDataRequestValue;
        public uint ProtocolDataRequestSubValue;
        public uint ProtocolDataOffset;
        public uint ProtocolDataLength;
        public uint FixedProtocolReturnData;
        public uint ProtocolDataRequestSubValue2;
        public uint ProtocolDataRequestSubValue3;
        public uint ProtocolDataRequestSubValue4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public STORAGE_PROTOCOL_SPECIFIC_DATA ProtocolSpecific;
    }

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

    public static NvmeHealth? TryRead(int diskNumber)
    {
        var path = $@"\\.\PhysicalDrive{diskNumber}";
        using var handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        int specificSize = Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
        int querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        // Descriptor header (Version + Size = 8 bytes) overlays the query's first 8 bytes layout,
        // so total buffer = query struct + log page payload.
        int bufferSize = querySize + LogPageSize;

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++) Marshal.WriteByte(buffer, i, 0);

            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceProtocolSpecificProperty,
                QueryType = PropertyStandardQuery,
                ProtocolSpecific = new STORAGE_PROTOCOL_SPECIFIC_DATA
                {
                    ProtocolType = ProtocolTypeNvme,
                    DataType = NVMeDataTypeLogPage,
                    ProtocolDataRequestValue = NvmeLogPageHealth,
                    ProtocolDataRequestSubValue = 0,
                    ProtocolDataOffset = (uint)specificSize,
                    ProtocolDataLength = LogPageSize,
                },
            };

            Marshal.StructureToPtr(query, buffer, false);

            bool ok = DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                buffer, bufferSize, buffer, bufferSize, out _, IntPtr.Zero);

            if (!ok)
                return null;

            // Log page payload begins right after the returned descriptor
            // (8-byte Version+Size header + STORAGE_PROTOCOL_SPECIFIC_DATA).
            int payloadOffset = 8 + specificSize;
            var log = new byte[LogPageSize];
            Marshal.Copy(IntPtr.Add(buffer, payloadOffset), log, 0, LogPageSize);

            return Parse(log);
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static NvmeHealth Parse(byte[] log)
    {
        double kelvin = BitConverter.ToUInt16(log, 1);
        double celsius = kelvin > 0 ? Math.Round(kelvin - 273.15, 1) : 0;

        return new NvmeHealth
        {
            CriticalWarning = log[0],
            TemperatureCelsius = celsius,
            AvailableSpare = log[3],
            AvailableSpareThreshold = log[4],
            PercentageUsed = log[5],
            DataUnitsRead = ReadU64(log, 32),
            DataUnitsWritten = ReadU64(log, 48),
            PowerCycles = ReadU64(log, 112),
            PowerOnHours = ReadU64(log, 128),
            UnsafeShutdowns = ReadU64(log, 144),
            MediaErrors = ReadU64(log, 160),
        };
    }

    // NVMe counters are 128-bit little-endian; the low 64 bits are more than sufficient here.
    private static ulong ReadU64(byte[] buffer, int offset) => BitConverter.ToUInt64(buffer, offset);
}
