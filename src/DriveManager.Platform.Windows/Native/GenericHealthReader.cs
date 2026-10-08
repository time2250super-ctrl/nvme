using System.Runtime.Versioning;
using DriveManager.Core.Models;
using Microsoft.Management.Infrastructure;

namespace DriveManager.Platform.Windows.Native;

/// <summary>
/// Reads health for non-NVMe drives (SATA / USB / RAID) using the Windows Storage provider:
/// MSFT_PhysicalDisk.HealthStatus plus the associated MSFT_StorageReliabilityCounter
/// (temperature, wear, power-on hours, error totals), combined with IOCTL_STORAGE_PREDICT_FAILURE.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GenericHealthReader
{
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    public static DriveHealthInfo? Read(PhysicalDiskInfo disk)
    {
        string? healthStatus = null;
        double? temperature = null;
        int? wear = null;
        ulong? powerOnHours = null;
        ulong? readErrors = null;
        ulong? writeErrors = null;
        ulong? powerCycles = null;

        try
        {
            using var session = CimSession.Create(null);
            var physicalDisks = session.QueryInstances(
                StorageNamespace, "WQL",
                $"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{disk.Number}'");

            foreach (var pd in physicalDisks)
            {
                healthStatus = MapHealthStatus(GetInt(pd, "HealthStatus"));

                try
                {
                    var counters = session.EnumerateAssociatedInstances(
                        StorageNamespace, pd,
                        "MSFT_PhysicalDiskToStorageReliabilityCounter",
                        "MSFT_StorageReliabilityCounter", null, null);

                    foreach (var rc in counters)
                    {
                        temperature = GetNullableDouble(rc, "Temperature");
                        wear = GetNullableInt(rc, "Wear");
                        powerOnHours = GetNullableULong(rc, "PowerOnHours");
                        readErrors = GetNullableULong(rc, "ReadErrorsTotal");
                        writeErrors = GetNullableULong(rc, "WriteErrorsTotal");
                        powerCycles = GetNullableULong(rc, "StartStopCycleCount");
                    }
                }
                catch
                {
                    // Reliability counters not available for this device; health status still useful.
                }

                break;
            }
        }
        catch
        {
            // Storage provider query failed; fall back to predict-failure only.
        }

        bool? predictFailure = PredictFailureReader.TryRead(disk.Number);

        // A reported temperature/wear of 0 from the counter usually means "not supported".
        if (temperature is 0) temperature = null;
        if (wear is 0) wear = null;

        var hasData = healthStatus is not null || predictFailure is not null ||
                      temperature is not null || wear is not null || powerOnHours is not null ||
                      readErrors is not null || writeErrors is not null;

        if (!hasData)
            return null;

        return new DriveHealthInfo
        {
            Source = "Windows reliability counters",
            HealthStatus = healthStatus,
            PredictFailure = predictFailure,
            TemperatureCelsius = temperature,
            PercentageUsed = wear,
            PowerOnHours = powerOnHours,
            PowerCycles = powerCycles,
            ReadErrorsTotal = readErrors,
            WriteErrorsTotal = writeErrors,
        };
    }

    private static string MapHealthStatus(int value) => value switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => "Unknown",
    };

    private static int GetInt(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is null ? -1 : Convert.ToInt32(v);
    }

    private static int? GetNullableInt(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is null ? null : Convert.ToInt32(v);
    }

    private static double? GetNullableDouble(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is null ? null : Convert.ToDouble(v);
    }

    private static ulong? GetNullableULong(CimInstance i, string name)
    {
        var v = i.CimInstanceProperties[name]?.Value;
        return v is null ? null : Convert.ToUInt64(v);
    }
}
