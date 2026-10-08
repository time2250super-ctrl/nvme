using DriveManager.Core.Models;

namespace DriveManager.Ai;

/// <summary>
/// Transparent, offline health scoring that works across drive types. It uses whatever metrics
/// a drive exposes (NVMe SMART, SATA/USB reliability counters, predict-failure, health status).
/// </summary>
public sealed class RuleBasedHealthAnalyzer : IHealthAnalyzer
{
    public HealthAssessment Assess(DriveHealthInfo h, IReadOnlyList<DriveHealthInfo>? history = null)
    {
        var findings = new List<string>();
        var recommendations = new List<string>();
        var risk = 0;

        // Firmware failure prediction (SMART trip) - strongest signal available on SATA/USB too.
        if (h.PredictFailure == true)
        {
            risk += 70;
            findings.Add("Drive firmware predicts imminent failure (SMART trip).");
            recommendations.Add("Back up immediately and replace the drive.");
        }

        // NVMe critical warning flags.
        if (h.HasCriticalWarning)
        {
            risk += 60;
            findings.Add($"Controller reports critical warning flags (0x{h.CriticalWarning:X2}).");
            recommendations.Add("Back up data immediately; the controller is flagging a critical condition.");
        }

        // OS-reported overall health status.
        switch (h.HealthStatus)
        {
            case "Unhealthy":
                risk += 60;
                findings.Add("Windows reports this disk as Unhealthy.");
                recommendations.Add("Investigate and replace the drive.");
                break;
            case "Warning":
                risk += 25;
                findings.Add("Windows reports this disk with a health Warning.");
                break;
        }

        // Wear / endurance.
        if (h.PercentageUsed is int used)
        {
            if (used >= 100)
            {
                risk += 40;
                findings.Add($"Endurance used is {used}% (at or beyond rated life).");
                recommendations.Add("Plan to replace the drive; rated write endurance is exhausted.");
            }
            else if (used >= 80)
            {
                risk += 20;
                findings.Add($"Endurance used is {used}%.");
                recommendations.Add("Monitor wear; begin planning for eventual replacement.");
            }
        }

        // Spare blocks.
        if (h.AvailableSpare is int spare && h.AvailableSpareThreshold is int threshold &&
            spare > 0 && spare <= threshold)
        {
            risk += 30;
            findings.Add($"Available spare {spare}% at/below threshold {threshold}%.");
            recommendations.Add("Spare area is depleted; replace the drive soon.");
        }

        // Temperature.
        if (h.TemperatureCelsius is double temp && temp > 0)
        {
            if (temp >= 75)
            {
                risk += 20;
                findings.Add($"Temperature is high ({temp:0.#} C).");
                recommendations.Add("Improve cooling/airflow; sustained high temperature accelerates wear.");
            }
            else if (temp >= 65)
            {
                risk += 8;
                findings.Add($"Temperature is elevated ({temp:0.#} C).");
            }
        }

        // Error counters.
        if (h.MediaErrors is ulong media && media > 0)
        {
            risk += 15;
            findings.Add($"{media} media/data-integrity error(s) recorded.");
            recommendations.Add("Run a full backup and verify data integrity.");
        }

        var readErr = h.ReadErrorsTotal ?? 0;
        var writeErr = h.WriteErrorsTotal ?? 0;
        if (readErr + writeErr > 0)
        {
            risk += 10;
            findings.Add($"Reliability counters report {readErr} read and {writeErr} write error(s).");
        }

        if (h.UnsafeShutdowns is ulong unsafeShutdowns && unsafeShutdowns > 0)
            findings.Add($"{unsafeShutdowns} unsafe shutdown(s) recorded.");

        risk = Math.Clamp(risk, 0, 100);

        var level = risk switch
        {
            >= 70 => HealthLevel.Critical,
            >= 40 => HealthLevel.Warning,
            >= 15 => HealthLevel.Watch,
            _ => h.HasAnyData ? HealthLevel.Good : HealthLevel.Unknown,
        };

        if (findings.Count == 0)
        {
            findings.Add(h.HasAnyData
                ? "No concerning indicators. The metrics this drive exposes are within normal ranges."
                : "Limited health data is available for this drive type.");
        }

        var summary = level switch
        {
            HealthLevel.Good => "Healthy. The drive shows no signs of trouble.",
            HealthLevel.Watch => "Generally healthy, but a few indicators are worth keeping an eye on.",
            HealthLevel.Warning => "Degraded. One or more health indicators need attention.",
            HealthLevel.Critical => "Critical. Back up now and plan replacement.",
            _ => "Unknown. This drive exposes little or no health telemetry.",
        };

        return new HealthAssessment
        {
            Level = level,
            RiskScore = risk,
            Summary = summary,
            Findings = findings,
            Recommendations = recommendations,
        };
    }
}
