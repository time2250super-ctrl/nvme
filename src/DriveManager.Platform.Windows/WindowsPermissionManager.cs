using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using DriveManager.Core.Abstractions;
using DriveManager.Platform.Windows.Native;

namespace DriveManager.Platform.Windows;

/// <summary>
/// Enables the Windows token privileges required for NVMe IOCTLs, diskpart, format,
/// take-ownership, and BitLocker, then verifies each capability.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPermissionManager : IPermissionManager
{
    private static readonly string[] RequiredPrivileges =
    [
        "SeBackupPrivilege",
        "SeRestorePrivilege",
        "SeTakeOwnershipPrivilege",
        "SeSecurityPrivilege",
        "SeManageVolumePrivilege",
        "SeSystemEnvironmentPrivilege",
        "SeChangeNotifyPrivilege",
        "SeIncreaseQuotaPrivilege",
        "SeLoadDriverPrivilege",
    ];

    private readonly List<PermissionItem> _items = [];

    public bool IsElevated { get; private set; }
    public string Summary { get; private set; } = "Permissions not checked yet.";
    public IReadOnlyList<PermissionItem> Items => _items;

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        _items.Clear();

        AddWindows11CompatibilityItems();

        IsElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        _items.Add(new PermissionItem(
            "Administrator elevation",
            IsElevated,
            IsElevated ? "Process is running elevated." : "Not elevated. Relaunch as Administrator (UAC)."));

        foreach (var name in RequiredPrivileges)
        {
            var ok = TryEnablePrivilege(name, out var detail);
            _items.Add(new PermissionItem(name, ok, detail));
        }

        var folder = AppContext.BaseDirectory;
        var aclOk = TryGrantFolderFullControl(folder, out var aclDetail);
        _items.Add(new PermissionItem("App folder full control", aclOk, aclDetail));

        var wmiOk = await TryStorageQueryAsync(ct).ConfigureAwait(false);
        _items.Add(new PermissionItem(
            "Storage enumeration",
            wmiOk.ok,
            wmiOk.detail));

        var blOk = await TryBitLockerQueryAsync(ct).ConfigureAwait(false);
        _items.Add(new PermissionItem("BitLocker cmdlets", blOk.ok, blOk.detail));

        var missing = _items.Count(i => !i.Granted);
        Summary = IsElevated
            ? (missing == 0
                ? "All required permissions are granted."
                : $"{missing} permission(s) still missing. Elevation is OK; see the list.")
            : "NOT ELEVATED — disk tools, health IOCTLs, format and BitLocker will fail.";
    }

    private void AddWindows11CompatibilityItems()
    {
        var v = Environment.OSVersion.Version;
        var win11 = v.Build >= 22000;
        _items.Add(new PermissionItem(
            "Windows 11",
            win11,
            win11
                ? $"Windows 11 compatible (NT {v.Major}.{v.Minor} build {v.Build}, {(Environment.Is64BitProcess ? "x64" : "x86")}). Uses the in-box StorNVMe stack, not a third-party driver."
                : $"This build targets Windows 11 (22000+). Detected NT {v.Major}.{v.Minor} build {v.Build}."));

        var x64 = Environment.Is64BitOperatingSystem && Environment.Is64BitProcess;
        _items.Add(new PermissionItem(
            "64-bit process",
            x64,
            x64 ? "win-x64 self-contained build matches this OS." : "Need a 64-bit Windows 11 process."));

        var nvmeSys = Path.Combine(Environment.SystemDirectory, "drivers", "stornvme.sys");
        var nvmeOk = File.Exists(nvmeSys);
        _items.Add(new PermissionItem(
            "StorNVMe driver (stornvme.sys)",
            nvmeOk,
            nvmeOk
                ? "Microsoft Standard NVM Express Controller driver is installed (Windows 11 in-box)."
                : "stornvme.sys is missing. Install/repair the Standard NVM Express Controller from Device Manager."));
    }

    private static bool TryEnablePrivilege(string name, out string detail)
    {
        const uint TokenAdjustPrivileges = 0x0020;
        const uint TokenQuery = 0x0008;
        const uint SePrivilegeEnabled = 0x00000002;

        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out var token))
        {
            detail = "OpenProcessToken failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
            {
                detail = "Privilege not present on this token.";
                return false;
            }

            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled,
            };

            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                detail = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            var err = Marshal.GetLastWin32Error();
            if (err == 1300) // ERROR_NOT_ALL_ASSIGNED
            {
                detail = "Not assigned to this account (need Administrators).";
                return false;
            }

            detail = "Enabled on this process token.";
            return true;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool TryGrantFolderFullControl(string folder, out string detail)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "data"));
            Directory.CreateDirectory(Path.Combine(folder, "models"));

            var user = WindowsIdentity.GetCurrent().Name;
            RunIcacls($"\"{folder}\" /grant Administrators:(OI)(CI)F /T /C /Q");
            if (!string.IsNullOrWhiteSpace(user))
                RunIcacls($"\"{folder}\" /grant \"{user}\":(OI)(CI)F /T /C /Q");

            var probe = Path.Combine(folder, "data", ".perm");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            detail = "Administrators + current user have Full Control; write probe succeeded.";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static void RunIcacls(string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "icacls.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        p?.WaitForExit(8000);
    }

    private static async Task<(bool ok, string detail)> TryStorageQueryAsync(CancellationToken ct)
    {
        try
        {
            var output = await ProcessRunner.RunPowerShellCaptureAsync(
                ["(Get-Disk | Measure-Object).Count"],
                ct).ConfigureAwait(false);
            if (int.TryParse(output.Trim(), out var n) && n > 0)
                return (true, $"Get-Disk returned {n} disk(s).");
            return (false, "Get-Disk returned no disks.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(bool ok, string detail)> TryBitLockerQueryAsync(CancellationToken ct)
    {
        try
        {
            var output = await ProcessRunner.RunPowerShellCaptureAsync(
                ["if (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue) { 'YES' } else { 'NO' }"],
                ct).ConfigureAwait(false);
            var yes = output.Trim().Contains("YES", StringComparison.OrdinalIgnoreCase);
            return (yes, yes ? "Get-BitLockerVolume is available." : "BitLocker cmdlets not found (Home edition or module missing).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle, bool disableAll, ref TokenPrivileges newState,
        uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
