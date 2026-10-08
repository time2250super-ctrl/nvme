using DriveManager.Core.Models;

namespace DriveManager.Core.Telemetry;

/// <summary>
/// Persists unified drive-health snapshots over time (any drive type) so trends and anomalies
/// can be analysed. Non-destructive: it only ever writes to a local database file.
/// </summary>
public interface ITelemetryStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task SaveAsync(string diskKey, DriveHealthInfo health, CancellationToken ct = default);

    Task<IReadOnlyList<DriveHealthInfo>> GetHistoryAsync(string diskKey, int limit = 100, CancellationToken ct = default);

    Task<int> CountAsync(string diskKey, CancellationToken ct = default);
}
