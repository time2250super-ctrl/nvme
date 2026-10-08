using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Issues IOCTL_STORAGE_PREDICT_FAILURE to read the drive's self-reported failure prediction
/// (the classic SMART "will fail / won't fail" bit). Works for SATA/USB drives. Requires admin.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PredictFailureReader
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_PREDICT_FAILURE = 0x002D1100;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PREDICT_FAILURE
    {
        public uint PredictFailure;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] VendorSpecific;
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

    public static bool? TryRead(int diskNumber)
    {
        var path = $@"\\.\PhysicalDrive{diskNumber}";
        SafeFileHandle handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        int size = Marshal.SizeOf<STORAGE_PREDICT_FAILURE>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(buffer, i, 0);

            bool ok = DeviceIoControl(handle, IOCTL_STORAGE_PREDICT_FAILURE,
                IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero);

            if (!ok)
                return null;

            uint predict = unchecked((uint)Marshal.ReadInt32(buffer, 0));
            return predict != 0;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            handle.Dispose();
        }
    }
}
