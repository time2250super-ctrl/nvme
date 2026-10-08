using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows;

// Guarded live repartition runner for the SPCC NVMe (Disk 1).
// Uses the app's REAL services (WindowsDiskService + WindowsFormatService + DestructiveExecutor).
//
//   plan                 -> non-destructive: prints the exact diskpart steps + confirmation phrase
//   exec "<phrase>"      -> DESTRUCTIVE: wipes Disk 1 and creates [5 GB NTFS] + [rest NTFS]
//
// Strict identity assertions prevent targeting any disk other than the SPCC NVMe.

const int TargetDiskNumber = 1;
const long AppPartitionBytes = 5L * 1024 * 1024 * 1024; // 5 GB app partition.

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "plan";

var diskService = new WindowsDiskService();
var formatService = new WindowsFormatService();

var disks = await diskService.GetDisksAsync();
var disk = disks.FirstOrDefault(d => d.Number == TargetDiskNumber);

if (disk is null)
{
    Console.Error.WriteLine($"ABORT: Disk {TargetDiskNumber} not found.");
    return 2;
}

// --- Hard safety assertions: must be the SPCC NVMe data disk, never system/boot. ---
var problems = new List<string>();
if (!disk.IsNvme) problems.Add($"Disk {disk.Number} bus is {disk.BusType}, expected NVMe.");
if (!disk.FriendlyName.Contains("SPCC", StringComparison.OrdinalIgnoreCase))
    problems.Add($"Disk {disk.Number} name '{disk.FriendlyName}' does not contain 'SPCC'.");
if (disk.IsSystem) problems.Add("Target is the SYSTEM disk — refused.");
if (disk.IsBoot) problems.Add("Target is the BOOT disk — refused.");
if (disk.SizeGb is < 100 or > 140) problems.Add($"Disk size {disk.SizeGb} GB is outside the expected ~119 GB range.");

if (problems.Count > 0)
{
    Console.Error.WriteLine("ABORT: target safety checks failed:");
    foreach (var p in problems) Console.Error.WriteLine("  - " + p);
    return 3;
}

var remaining = disk.SizeBytes - AppPartitionBytes;
var preview = formatService.PreviewRepartition(
    disk,
    new[] { AppPartitionBytes, remaining },
    FileSystemType.Ntfs);

Console.WriteLine("================ TARGET ================");
Console.WriteLine($"Disk {disk.Number}: {disk.FriendlyName}  ({disk.BusType}, {disk.SizeGb} GB)");
Console.WriteLine($"Serial: {disk.SerialNumber ?? "(none)"}");
Console.WriteLine($"Current volumes: {string.Join(", ", disk.Volumes.Select(v => (v.DriveLetter?.ToString() ?? "-") + ":"))}");
Console.WriteLine();
Console.WriteLine("================ PLAN ==================");
Console.WriteLine($"Partition 1: {AppPartitionBytes / 1024 / 1024} MB  NTFS  (app + AI model)");
Console.WriteLine($"Partition 2: remaining (~{Math.Round(remaining / 1024d / 1024d / 1024d, 1)} GB)  NTFS  (data)");
Console.WriteLine();
foreach (var s in preview.Steps) Console.WriteLine("  " + s);
Console.WriteLine();
Console.WriteLine("================ CONFIRMATION PHRASE ===");
Console.WriteLine(preview.ConfirmationPhrase);
Console.WriteLine("========================================");

if (mode == "plan")
{
    Console.WriteLine();
    Console.WriteLine("Plan only. No changes made. Re-run with:  exec \"<phrase>\"");
    return 0;
}

if (mode == "exec")
{
    var phrase = args.Length > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;
    Console.WriteLine();
    Console.WriteLine(">>> EXECUTING DESTRUCTIVE REPARTITION ON DISK " + disk.Number + " ...");
    try
    {
        await formatService.ExecuteAsync(preview, phrase);
        Console.WriteLine(">>> DONE. Disk repartitioned.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(">>> FAILED: " + ex.Message);
        return 1;
    }
}

Console.Error.WriteLine($"Unknown mode '{mode}'. Use 'plan' or 'exec \"<phrase>\"'.");
return 4;
