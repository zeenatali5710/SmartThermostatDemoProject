using Microsoft.Data.Sqlite;

namespace SmartThermostat.Api.Services;

/// <summary>An alert row as returned by the query API (read shape, local to the API).</summary>
public sealed record AlertView(
    long Id,
    string DeviceId,
    string Status,
    double IndoorTempF,
    string Message,
    string RaisedAt);

/// <summary>
/// Read access to alerts persisted in SQLite by the AlertProcessor worker. The API never
/// writes alerts; it only queries the history.
/// </summary>
public sealed class AlertStore
{
    private readonly string _connectionString;

    public AlertStore(IConfiguration config)
    {
        var path = config.GetValue<string>("Sqlite:Path") ?? "thermostat.db";
        _connectionString = $"Data Source={path}";
    }

    /// <summary>
    /// Ensures the Alerts table exists so reads succeed even before the worker has written
    /// anything. Both the API and the AlertProcessor call this on startup (CREATE IF NOT EXISTS).
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Alerts (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId    TEXT    NOT NULL,
                Status      TEXT    NOT NULL,
                IndoorTempF REAL    NOT NULL,
                Message     TEXT    NOT NULL,
                RaisedAt    TEXT    NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AlertView>> GetRecentAsync(string? deviceId, int limit)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, DeviceId, Status, IndoorTempF, Message, RaisedAt
            FROM Alerts
            WHERE (@deviceId IS NULL OR DeviceId = @deviceId)
            ORDER BY Id DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@deviceId", (object?)deviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);

        var alerts = new List<AlertView>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            alerts.Add(new AlertView(
                Id: reader.GetInt64(0),
                DeviceId: reader.GetString(1),
                Status: reader.GetString(2),
                IndoorTempF: reader.GetDouble(3),
                Message: reader.GetString(4),
                RaisedAt: reader.GetString(5)));
        }
        return alerts;
    }
}
