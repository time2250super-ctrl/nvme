using DriveManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace DriveManager.Core.Telemetry;

/// <summary>
/// SQLite-backed telemetry store (free, offline, no server). The database file lives wherever
/// the host chooses (default: a portable folder, later the app's own partition on the NVMe).
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
                    TemperatureC            REAL    NOT NULL,
                    PercentageUsed          INTEGER NOT NULL,
                    AvailableSpare          INTEGER NOT NULL,
                    AvailableSpareThreshold INTEGER NOT NULL,
                    CriticalWarning         INTEGER NOT NULL,
                    DataUnitsRead           INTEGER NOT NULL,
                    DataUnitsWritten        INTEGER NOT NULL,
                    PowerCycles             INTEGER NOT NULL,
                    PowerOnHours            INTEGER NOT NULL,
                    UnsafeShutdowns         INTEGER NOT NULL,
                    MediaErrors             INTEGER NOT NULL
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

    public async Task SaveAsync(string diskKey, NvmeHealth h, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO HealthSnapshots
                (DiskKey, CapturedAt, TemperatureC, PercentageUsed, AvailableSpare, AvailableSpareThreshold,
                 CriticalWarning, DataUnitsRead, DataUnitsWritten, PowerCycles, PowerOnHours, UnsafeShutdowns, MediaErrors)
            VALUES
                ($key, $at, $temp, $used, $spare, $spareTh, $crit, $dur, $duw, $pc, $poh, $us, $me);
            """;
        cmd.Parameters.AddWithValue("$key", diskKey);
        cmd.Parameters.AddWithValue("$at", h.CapturedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$temp", h.TemperatureCelsius);
        cmd.Parameters.AddWithValue("$used", h.PercentageUsed);
        cmd.Parameters.AddWithValue("$spare", h.AvailableSpare);
        cmd.Parameters.AddWithValue("$spareTh", h.AvailableSpareThreshold);
        cmd.Parameters.AddWithValue("$crit", h.CriticalWarning);
        cmd.Parameters.AddWithValue("$dur", (long)h.DataUnitsRead);
        cmd.Parameters.AddWithValue("$duw", (long)h.DataUnitsWritten);
        cmd.Parameters.AddWithValue("$pc", (long)h.PowerCycles);
        cmd.Parameters.AddWithValue("$poh", (long)h.PowerOnHours);
        cmd.Parameters.AddWithValue("$us", (long)h.UnsafeShutdowns);
        cmd.Parameters.AddWithValue("$me", (long)h.MediaErrors);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NvmeHealth>> GetHistoryAsync(string diskKey, int limit = 100, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT CapturedAt, TemperatureC, PercentageUsed, AvailableSpare, AvailableSpareThreshold,
                   CriticalWarning, DataUnitsRead, DataUnitsWritten, PowerCycles, PowerOnHours, UnsafeShutdowns, MediaErrors
            FROM HealthSnapshots
            WHERE DiskKey = $key
            ORDER BY CapturedAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$key", diskKey);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<NvmeHealth>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new NvmeHealth
            {
                CapturedAt = DateTimeOffset.Parse(reader.GetString(0)),
                TemperatureCelsius = reader.GetDouble(1),
                PercentageUsed = reader.GetInt32(2),
                AvailableSpare = reader.GetInt32(3),
                AvailableSpareThreshold = reader.GetInt32(4),
                CriticalWarning = (byte)reader.GetInt32(5),
                DataUnitsRead = (ulong)reader.GetInt64(6),
                DataUnitsWritten = (ulong)reader.GetInt64(7),
                PowerCycles = (ulong)reader.GetInt64(8),
                PowerOnHours = (ulong)reader.GetInt64(9),
                UnsafeShutdowns = (ulong)reader.GetInt64(10),
                MediaErrors = (ulong)reader.GetInt64(11),
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
