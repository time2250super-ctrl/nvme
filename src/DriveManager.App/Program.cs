using System.Runtime.InteropServices;
using Avalonia;

namespace DriveManager.App;

sealed class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("unhandled", e.ExceptionObject as Exception);

        try
        {
            Log("start", null);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Log("exit", null);
        }
        catch (Exception ex)
        {
            Log("fatal", ex);
            MessageBoxW(0, "Drive Manager failed to start:\n" + ex.Message, "Drive Manager", 0x10);
        }
    }

    private static void Log(string step, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dir);
            var line = DateTime.Now.ToString("o") + " " + step
                       + (ex is null ? "" : " " + ex) + Environment.NewLine;
            File.AppendAllText(Path.Combine(dir, "startup.log"), line);
        }
        catch { /* never block startup on logging */ }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
