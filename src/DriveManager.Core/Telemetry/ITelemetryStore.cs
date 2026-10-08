using DriveManager.Core.Models;

namespace DriveManager.Core.Telemetry;

/// <summary>
/// Persists NVMe health snapshots over time so trends and anomalies can be analysed.
/// </summary>
public interface ITelemetryStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task SaveAsync(string diskKey, NvmeHealth health, CancellationToken ct = default);

    Task<IReadOnlyList<NvmeHealth>> GetHistoryAsync(string diskKey, int limit = 100, CancellationToken ct = default);

    Task<int> CountAsync(string diskKey, CancellationToken ct = default);
}
