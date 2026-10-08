using DriveManager.Core.Models;

namespace DriveManager.Ai;

/// <summary>
/// Produces a <see cref="HealthAssessment"/> from a drive health snapshot. The default
/// implementation is rule-based and works for any drive type (NVMe, SATA, USB).
/// </summary>
public interface IHealthAnalyzer
{
    HealthAssessment Assess(DriveHealthInfo current, IReadOnlyList<DriveHealthInfo>? history = null);
}
