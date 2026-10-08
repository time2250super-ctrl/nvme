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

var nvme = disks.FirstOrDefault(d => d.IsNvme);
if (nvme is null)
{
    Console.WriteLine("\nNo NVMe disk detected; skipping health read.");
    return;
}

Console.WriteLine($"\nReading NVMe SMART/health from Disk {nvme.Number} ({nvme.FriendlyName})...");
var health = await healthService.ReadHealthAsync(nvme);
if (health is null)
{
    Console.WriteLine("  Health read returned NULL (needs Administrator, or device does not expose NVMe health).");
    return;
}

Console.WriteLine($"  Temperature        : {health.TemperatureCelsius} C");
Console.WriteLine($"  Life used          : {health.PercentageUsed} %");
Console.WriteLine($"  Available spare    : {health.AvailableSpare} % (threshold {health.AvailableSpareThreshold} %)");
Console.WriteLine($"  Data written       : {health.TerabytesWritten} TB");
Console.WriteLine($"  Data read          : {health.TerabytesRead} TB");
Console.WriteLine($"  Power-on hours     : {health.PowerOnHours}");
Console.WriteLine($"  Power cycles       : {health.PowerCycles}");
Console.WriteLine($"  Unsafe shutdowns   : {health.UnsafeShutdowns}");
Console.WriteLine($"  Media errors       : {health.MediaErrors}");
Console.WriteLine($"  Critical warning   : 0x{health.CriticalWarning:X2}");

var assessment = analyzer.Assess(health);
Console.WriteLine($"\nAI assessment: {assessment.Level} (risk {assessment.RiskScore}/100)");
Console.WriteLine($"  {assessment.Summary}");
foreach (var f in assessment.Findings)
    Console.WriteLine($"  - {f}");
if (assessment.Recommendations.Count > 0)
{
    Console.WriteLine("  Recommendations:");
    foreach (var r in assessment.Recommendations)
        Console.WriteLine($"    * {r}");
}

Console.WriteLine("\n=== Probe complete ===");
