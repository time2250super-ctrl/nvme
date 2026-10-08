using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Platform.Windows;

// Live repartition runner for the SPCC NVMe (Disk 1).
// Uses the app's REAL services (WindowsDiskService + WindowsFormatService + DestructiveExecutor).
//
//   plan   -> prints the exact diskpart steps (no changes)
//   exec   -> wipes Disk 1 and creates [5 GB NTFS] + [rest NTFS]

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

var remaining = disk.SizeBytes - AppPartitionBytes;
var plan = formatService.PreviewRepartition(
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
foreach (var s in plan.Steps) Console.WriteLine("  " + s);
Console.WriteLine("========================================");

if (mode == "plan")
{
    Console.WriteLine();
    Console.WriteLine("Plan only. No changes made. Re-run with:  exec");
    return 0;
}

if (mode == "exec")
{
    Console.WriteLine();
    Console.WriteLine(">>> EXECUTING REPARTITION ON DISK " + disk.Number + " ...");
    try
    {
        await formatService.ExecuteAsync(plan);
        Console.WriteLine(">>> DONE. Disk repartitioned.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(">>> FAILED: " + ex.Message);
        return 1;
    }
}

Console.Error.WriteLine($"Unknown mode '{mode}'. Use 'plan' or 'exec'.");
return 4;
