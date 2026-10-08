using DriveManager.Core.Models;

namespace DriveManager.Ai;

/// <summary>
/// Produces a <see cref="HealthAssessment"/> from NVMe telemetry. The default implementation is
/// rule-based; an ML/ONNX implementation can be swapped in behind this interface later.
/// </summary>
public interface IHealthAnalyzer
{
    HealthAssessment Assess(NvmeHealth current, IReadOnlyList<NvmeHealth>? history = null);
}
