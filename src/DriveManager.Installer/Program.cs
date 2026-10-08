using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using DriveManager.Core.Guards;
using DriveManager.Platform.Windows;

namespace DriveManager.Installer;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main()
    {
        try
        {
            var disks = new WindowsDiskService().GetDisksAsync().GetAwaiter().GetResult();
            var vol = disks.Select(TargetDriveLock.FindAppVolume)
                .FirstOrDefault(v => v?.DriveLetter is not null);
            if (vol?.DriveLetter is not char letter)
            {
                Fail("The 5 GB DriveMgr partition was not found. Partition that drive first.");
                return 3;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DriveManager.Payload.App.zip");
            if (stream is null)
            {
                Fail("Installer payload is missing. Rebuild with the pack script.");
                return 4;
            }

            var destDir = Path.Combine(letter + ":\\", "DriveManager");
            Directory.CreateDirectory(destDir);
            Directory.CreateDirectory(Path.Combine(destDir, "models"));
            Directory.CreateDirectory(Path.Combine(destDir, "data"));

            var zipPath = Path.Combine(Path.GetTempPath(), "DriveManager.payload.zip");
            using (var outFile = File.Create(zipPath))
                stream.CopyTo(outFile);

            ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { /* ignore */ }

            var destExe = Path.Combine(destDir, "DriveManager.App.exe");
            if (!File.Exists(destExe))
            {
                Fail("Install finished but DriveManager.App.exe is missing.");
                return 5;
            }

            CreateDesktopShortcut(destExe, destDir);
            MessageBoxW(0, $"Installed to {destExe}\nDesktop icon created.", "Drive Manager Setup", 0x40);
            return 0;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return 1;
        }
    }

    private static void Fail(string message) => MessageBoxW(0, message, "Drive Manager Setup", 0x10);

    private static void CreateDesktopShortcut(string targetExe, string workingDir)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var linkPath = Path.Combine(desktop, "Drive Manager.lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        dynamic shell = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(linkPath);
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = workingDir;
        shortcut.WindowStyle = 1;
        shortcut.Description = "Drive Manager";
        shortcut.IconLocation = targetExe + ",0";
        shortcut.Save();
    }
}
