using DriveManager.Ai;
using DriveManager.Platform.Windows;

Console.WriteLine("=== DriveManager Probe (headless functional test) ===");
Console.WriteLine();

var diskService = new WindowsDiskService();
var healthService = new WindowsHealthService();
var analyzer = new RuleBasedHealthAnalyzer();

var disks = await diskService.GetDisksAsync();
Console.WriteLine($"Discovered {disks.Count} physical disk(s):");
foreach (var d in disks)
{
    Console.WriteLine($"  [Disk {d.Number}] {d.FriendlyName}");
    Console.WriteLine($"      Bus={d.BusType}  Size={d.SizeGb} GB  Style={d.PartitionStyle}  " +
                      $"Boot={d.IsBoot}  System={d.IsSystem}  NVMe={d.IsNvme}");
    Console.WriteLine($"      Serial={d.SerialNumber}  Model={d.Model}");
    foreach (var v in d.Volumes)
        Console.WriteLine($"        -> {v.DriveLetter}: \"{v.Label}\" {v.FileSystem}  {v.UsedGb}/{v.SizeGb} GB used");
}

Console.WriteLine($"\nReading health for ALL {disks.Count} disk(s)...");
foreach (var disk in disks)
{
    Console.WriteLine($"\n--- Disk {disk.Number}: {disk.FriendlyName}  (Bus={disk.BusType}, NVMe={disk.IsNvme}) ---");
    var health = await healthService.ReadHealthAsync(disk);
    if (health is null)
    {
        Console.WriteLine("  No health data available (needs Administrator, or device exposes no diagnostics).");
        continue;
    }

    Console.WriteLine($"  Source             : {health.Source}");
    Console.WriteLine($"  Health status      : {health.HealthStatus ?? "(n/a)"}");
    Console.WriteLine($"  Failure predicted  : {FmtBool(health.PredictFailure)}");
    Console.WriteLine($"  Temperature        : {Fmt(health.TemperatureCelsius)} C");
    Console.WriteLine($"  Life used          : {Fmt(health.PercentageUsed)} %");
    Console.WriteLine($"  Available spare    : {Fmt(health.AvailableSpare)} % (threshold {Fmt(health.AvailableSpareThreshold)} %)");
    Console.WriteLine($"  Data written       : {Fmt(health.TerabytesWritten)} TB");
    Console.WriteLine($"  Data read          : {Fmt(health.TerabytesRead)} TB");
    Console.WriteLine($"  Power-on hours     : {Fmt(health.PowerOnHours)}");
    Console.WriteLine($"  Power cycles       : {Fmt(health.PowerCycles)}");
    Console.WriteLine($"  Unsafe shutdowns   : {Fmt(health.UnsafeShutdowns)}");
    Console.WriteLine($"  Media errors       : {Fmt(health.MediaErrors)}");
    Console.WriteLine($"  Read errors        : {Fmt(health.ReadErrorsTotal)}");
    Console.WriteLine($"  Write errors       : {Fmt(health.WriteErrorsTotal)}");
    if (health.CriticalWarning is byte cw)
        Console.WriteLine($"  Critical warning   : 0x{cw:X2}");

    var assessment = analyzer.Assess(health);
    Console.WriteLine($"  AI assessment      : {assessment.Level} (risk {assessment.RiskScore}/100)");
    Console.WriteLine($"    {assessment.Summary}");
    foreach (var f in assessment.Findings)
        Console.WriteLine($"    - {f}");
    if (assessment.Recommendations.Count > 0)
    {
        Console.WriteLine("    Recommendations:");
        foreach (var r in assessment.Recommendations)
            Console.WriteLine($"      * {r}");
    }
}

Console.WriteLine("\n=== Probe complete ===");

static string Fmt(object? v) => v?.ToString() ?? "(n/a)";
static string FmtBool(bool? v) => v is null ? "(n/a)" : (v.Value ? "YES" : "no");
