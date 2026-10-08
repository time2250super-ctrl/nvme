namespace DriveManager.Core.Models;

/// <summary>
/// Unified, drive-agnostic health snapshot. NVMe drives populate the rich fields; SATA/USB
/// drives populate whatever Windows exposes (health status, reliability counters, predict-failure).
/// All metric fields are nullable because not every drive reports every metric.
/// </summary>
public sealed record DriveHealthInfo
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Where the data came from, e.g. "NVMe SMART (log 0x02)" or "Windows reliability counters".</summary>
    public required string Source { get; init; }

    /// <summary>Overall status string: Healthy / Warning / Unhealthy / Unknown.</summary>
    public string? HealthStatus { get; init; }

    /// <summary>True when the drive firmware predicts imminent failure (SMART trip).</summary>
    public bool? PredictFailure { get; init; }

    public double? TemperatureCelsius { get; init; }

    /// <summary>Percentage of rated life used / wear (0-100+).</summary>
    public int? PercentageUsed { get; init; }

    public int? AvailableSpare { get; init; }
    public int? AvailableSpareThreshold { get; init; }
    public byte? CriticalWarning { get; init; }

    public ulong? PowerOnHours { get; init; }
    public ulong? PowerCycles { get; init; }
    public ulong? UnsafeShutdowns { get; init; }
    public ulong? MediaErrors { get; init; }
    public ulong? ReadErrorsTotal { get; init; }
    public ulong? WriteErrorsTotal { get; init; }

    public double? TerabytesWritten { get; init; }
    public double? TerabytesRead { get; init; }

    public bool HasCriticalWarning => CriticalWarning is > 0;

    public bool HasAnyData =>
        HealthStatus is not null || PredictFailure is not null || TemperatureCelsius is not null ||
        PercentageUsed is not null || AvailableSpare is not null || PowerOnHours is not null ||
        MediaErrors is not null || ReadErrorsTotal is not null || WriteErrorsTotal is not null;

    public static DriveHealthInfo FromNvme(NvmeHealth h) => new()
    {
        CapturedAt = h.CapturedAt,
        Source = "NVMe SMART (log 0x02)",
        HealthStatus = h.HasCriticalWarning ? "Warning" : "Healthy",
        TemperatureCelsius = h.TemperatureCelsius,
        PercentageUsed = h.PercentageUsed,
        AvailableSpare = h.AvailableSpare,
        AvailableSpareThreshold = h.AvailableSpareThreshold,
        CriticalWarning = h.CriticalWarning,
        PowerOnHours = h.PowerOnHours,
        PowerCycles = h.PowerCycles,
        UnsafeShutdowns = h.UnsafeShutdowns,
        MediaErrors = h.MediaErrors,
        TerabytesWritten = h.TerabytesWritten,
        TerabytesRead = h.TerabytesRead,
    };
}
