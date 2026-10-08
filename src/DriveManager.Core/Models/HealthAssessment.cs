namespace DriveManager.Core.Models;

public enum HealthLevel
{
    Unknown = 0,
    Good = 1,
    Watch = 2,
    Warning = 3,
    Critical = 4,
}

/// <summary>
/// AI / rule-based assessment derived from one or more <see cref="NvmeHealth"/> snapshots.
/// </summary>
public sealed record HealthAssessment
{
    public required HealthLevel Level { get; init; }

    /// <summary>0 (perfect) to 100 (failure imminent).</summary>
    public required int RiskScore { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Recommendations { get; init; } = [];

    public IReadOnlyList<string> Findings { get; init; } = [];
}
