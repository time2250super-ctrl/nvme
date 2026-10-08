using DriveManager.Core.Models;

namespace DriveManager.Ai;

/// <summary>
/// Transparent, offline health scoring based on NVMe spec thresholds. Serves as the baseline
/// and as a feature source / explainer for the chat assistant.
/// </summary>
public sealed class RuleBasedHealthAnalyzer : IHealthAnalyzer
{
    public HealthAssessment Assess(NvmeHealth h, IReadOnlyList<NvmeHealth>? history = null)
    {
        var findings = new List<string>();
        var recommendations = new List<string>();
        var risk = 0;

        // Critical warning flags are the strongest signal.
        if (h.HasCriticalWarning)
        {
            risk += 60;
            findings.Add($"Controller reports critical warning flags (0x{h.CriticalWarning:X2}).");
            recommendations.Add("Back up data immediately; the controller is flagging a critical condition.");
        }

        // Wear / endurance.
        if (h.PercentageUsed >= 100)
        {
            risk += 40;
            findings.Add($"Endurance used is {h.PercentageUsed}% (at or beyond rated life).");
            recommendations.Add("Plan to replace the drive; rated write endurance is exhausted.");
        }
        else if (h.PercentageUsed >= 80)
        {
            risk += 20;
            findings.Add($"Endurance used is {h.PercentageUsed}%.");
            recommendations.Add("Monitor wear; begin planning for eventual replacement.");
        }

        // Spare blocks.
        if (h.AvailableSpare > 0 && h.AvailableSpare <= h.AvailableSpareThreshold)
        {
            risk += 30;
            findings.Add($"Available spare {h.AvailableSpare}% at/below threshold {h.AvailableSpareThreshold}%.");
            recommendations.Add("Spare area is depleted; replace the drive soon.");
        }

        // Temperature.
        if (h.TemperatureCelsius >= 75)
        {
            risk += 20;
            findings.Add($"Temperature is high ({h.TemperatureCelsius:0.#} C).");
            recommendations.Add("Improve cooling/airflow; sustained high temperature accelerates wear.");
        }
        else if (h.TemperatureCelsius >= 65)
        {
            risk += 8;
            findings.Add($"Temperature is elevated ({h.TemperatureCelsius:0.#} C).");
        }

        // Reliability counters.
        if (h.MediaErrors > 0)
        {
            risk += 15;
            findings.Add($"{h.MediaErrors} media/data-integrity error(s) recorded.");
            recommendations.Add("Run a full backup and verify data integrity.");
        }

        if (h.UnsafeShutdowns > 0)
        {
            findings.Add($"{h.UnsafeShutdowns} unsafe shutdown(s) recorded.");
        }

        risk = Math.Clamp(risk, 0, 100);

        var level = risk switch
        {
            >= 70 => HealthLevel.Critical,
            >= 40 => HealthLevel.Warning,
            >= 15 => HealthLevel.Watch,
            _ => HealthLevel.Good,
        };

        if (findings.Count == 0)
            findings.Add("No concerning indicators. Temperature, wear, spare area and error counters are within normal ranges.");

        var summary = level switch
        {
            HealthLevel.Good => "Healthy. The drive shows no signs of trouble.",
            HealthLevel.Watch => "Generally healthy, but a few indicators are worth keeping an eye on.",
            HealthLevel.Warning => "Degraded. One or more health indicators need attention.",
            HealthLevel.Critical => "Critical. Back up now and plan replacement.",
            _ => "Unknown.",
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
