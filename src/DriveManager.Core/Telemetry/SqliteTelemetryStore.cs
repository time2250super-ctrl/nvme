using DriveManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace DriveManager.Core.Telemetry;

/// <summary>
/// SQLite-backed telemetry store (free, offline, no server). Stores the unified
/// <see cref="DriveHealthInfo"/> so any drive type (NVMe, SATA, USB) can be tracked over time.
/// The database file lives wherever the host chooses (later: the app's own NVMe partition).
/// </summary>
public sealed class SqliteTelemetryStore : ITelemetryStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteTelemetryStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS HealthSnapshots (
                    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    DiskKey                 TEXT    NOT NULL,
                    CapturedAt              TEXT    NOT NULL,
                    Source                  TEXT    NOT NULL,
                    HealthStatus            TEXT    NULL,
                    PredictFailure          INTEGER NULL,
                    TemperatureC            REAL    NULL,
                    PercentageUsed          INTEGER NULL,
                    AvailableSpare          INTEGER NULL,
                    AvailableSpareThreshold INTEGER NULL,
                    CriticalWarning         INTEGER NULL,
                    PowerCycles             INTEGER NULL,
                    PowerOnHours            INTEGER NULL,
                    UnsafeShutdowns         INTEGER NULL,
                    MediaErrors             INTEGER NULL,
                    ReadErrorsTotal         INTEGER NULL,
                    WriteErrorsTotal        INTEGER NULL,
                    TerabytesWritten        REAL    NULL,
                    TerabytesRead           REAL    NULL
                );
                CREATE INDEX IF NOT EXISTS IX_HealthSnapshots_DiskKey_CapturedAt
                    ON HealthSnapshots (DiskKey, CapturedAt);
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SaveAsync(string diskKey, DriveHealthInfo h, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO HealthSnapshots
                (DiskKey, CapturedAt, Source, HealthStatus, PredictFailure, TemperatureC, PercentageUsed,
                 AvailableSpare, AvailableSpareThreshold, CriticalWarning, PowerCycles, PowerOnHours,
                 UnsafeShutdowns, MediaErrors, ReadErrorsTotal, WriteErrorsTotal, TerabytesWritten, TerabytesRead)
            VALUES
                ($key, $at, $src, $status, $predict, $temp, $used, $spare, $spareTh, $crit, $pc, $poh,
                 $us, $me, $re, $we, $tbw, $tbr);
            """;
        cmd.Parameters.AddWithValue("$key", diskKey);
        cmd.Parameters.AddWithValue("$at", h.CapturedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$src", h.Source);
        cmd.Parameters.AddWithValue("$status", (object?)h.HealthStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$predict", h.PredictFailure is null ? DBNull.Value : (h.PredictFailure.Value ? 1 : 0));
        cmd.Parameters.AddWithValue("$temp", (object?)h.TemperatureCelsius ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$used", (object?)h.PercentageUsed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$spare", (object?)h.AvailableSpare ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$spareTh", (object?)h.AvailableSpareThreshold ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$crit", h.CriticalWarning is null ? DBNull.Value : (int)h.CriticalWarning.Value);
        cmd.Parameters.AddWithValue("$pc", h.PowerCycles is null ? DBNull.Value : (long)h.PowerCycles.Value);
        cmd.Parameters.AddWithValue("$poh", h.PowerOnHours is null ? DBNull.Value : (long)h.PowerOnHours.Value);
        cmd.Parameters.AddWithValue("$us", h.UnsafeShutdowns is null ? DBNull.Value : (long)h.UnsafeShutdowns.Value);
        cmd.Parameters.AddWithValue("$me", h.MediaErrors is null ? DBNull.Value : (long)h.MediaErrors.Value);
        cmd.Parameters.AddWithValue("$re", h.ReadErrorsTotal is null ? DBNull.Value : (long)h.ReadErrorsTotal.Value);
        cmd.Parameters.AddWithValue("$we", h.WriteErrorsTotal is null ? DBNull.Value : (long)h.WriteErrorsTotal.Value);
        cmd.Parameters.AddWithValue("$tbw", (object?)h.TerabytesWritten ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tbr", (object?)h.TerabytesRead ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DriveHealthInfo>> GetHistoryAsync(string diskKey, int limit = 100, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT CapturedAt, Source, HealthStatus, PredictFailure, TemperatureC, PercentageUsed,
                   AvailableSpare, AvailableSpareThreshold, CriticalWarning, PowerCycles, PowerOnHours,
                   UnsafeShutdowns, MediaErrors, ReadErrorsTotal, WriteErrorsTotal, TerabytesWritten, TerabytesRead
            FROM HealthSnapshots
            WHERE DiskKey = $key
            ORDER BY CapturedAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$key", diskKey);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<DriveHealthInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DriveHealthInfo
            {
                CapturedAt = DateTimeOffset.Parse(reader.GetString(0)),
                Source = reader.GetString(1),
                HealthStatus = reader.IsDBNull(2) ? null : reader.GetString(2),
                PredictFailure = reader.IsDBNull(3) ? null : reader.GetInt32(3) != 0,
                TemperatureCelsius = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                PercentageUsed = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                AvailableSpare = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                AvailableSpareThreshold = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                CriticalWarning = reader.IsDBNull(8) ? null : (byte)reader.GetInt32(8),
                PowerCycles = reader.IsDBNull(9) ? null : (ulong)reader.GetInt64(9),
                PowerOnHours = reader.IsDBNull(10) ? null : (ulong)reader.GetInt64(10),
                UnsafeShutdowns = reader.IsDBNull(11) ? null : (ulong)reader.GetInt64(11),
                MediaErrors = reader.IsDBNull(12) ? null : (ulong)reader.GetInt64(12),
                ReadErrorsTotal = reader.IsDBNull(13) ? null : (ulong)reader.GetInt64(13),
                WriteErrorsTotal = reader.IsDBNull(14) ? null : (ulong)reader.GetInt64(14),
                TerabytesWritten = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                TerabytesRead = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            });
        }
        return list;
    }

    public async Task<int> CountAsync(string diskKey, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM HealthSnapshots WHERE DiskKey = $key;";
        cmd.Parameters.AddWithValue("$key", diskKey);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }
}
