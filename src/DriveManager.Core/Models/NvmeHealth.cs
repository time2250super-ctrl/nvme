namespace DriveManager.Core.Models;

/// <summary>
/// NVMe SMART / Health Information (log page 0x02) snapshot.
/// </summary>
public sealed record NvmeHealth
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Composite temperature in degrees Celsius.</summary>
    public double TemperatureCelsius { get; init; }

    /// <summary>Estimated percentage of device life used (0-100+, can exceed 100).</summary>
    public int PercentageUsed { get; init; }

    /// <summary>Available spare capacity as a percentage (0-100).</summary>
    public int AvailableSpare { get; init; }

    /// <summary>Threshold below which available spare is considered critical.</summary>
    public int AvailableSpareThreshold { get; init; }

    /// <summary>Critical warning bit flags reported by the controller (0 = healthy).</summary>
    public byte CriticalWarning { get; init; }

    public ulong DataUnitsRead { get; init; }
    public ulong DataUnitsWritten { get; init; }
    public ulong PowerCycles { get; init; }
    public ulong PowerOnHours { get; init; }
    public ulong UnsafeShutdowns { get; init; }
    public ulong MediaErrors { get; init; }

    /// <summary>NVMe data units are 1000 * 512 bytes each.</summary>
    public double TerabytesWritten => Math.Round(DataUnitsWritten * 512d * 1000d / 1_000_000_000_000d, 3);
    public double TerabytesRead => Math.Round(DataUnitsRead * 512d * 1000d / 1_000_000_000_000d, 3);

    public bool HasCriticalWarning => CriticalWarning != 0;
}
