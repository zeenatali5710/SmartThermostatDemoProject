using Microsoft.Data.Sqlite;
using SmartThermostat.Shared;

namespace RulesProcessor.Worker.Processors;

/// <summary>
/// Persists alerts to SQLite when a telemetry event violates a rule. Reuses
/// <see cref="RuleProcessor.Evaluate"/> for the verdict, then appends a durable alert row
/// for any non-Ok status. The API reads these rows via GET /alerts.
/// </summary>
public sealed class AlertProcessor : TelemetryProcessorBase
{
    private readonly string _connectionString;

    protected override string GroupId => "alert-processor";

    public AlertProcessor(IConfiguration config, ILogger<AlertProcessor> logger)
        : base(config, logger)
    {
        var path = config.GetValue<string>("Sqlite:Path") ?? "thermostat.db";
        _connectionString = $"Data Source={path}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
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
        cmd.ExecuteNonQuery();
    }

    protected override async Task HandleAsync(Telemetry telemetry, CancellationToken ct)
    {
        // A single event may trip more than one rule; store a row for each non-Ok verdict.
        var fired = RuleProcessor.Evaluate(telemetry)
            .Where(r => r.Status != RuleStatus.Ok)
            .ToList();
        if (fired.Count == 0) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var result in fired)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Alerts (DeviceId, Status, IndoorTempF, Message, RaisedAt)
                VALUES (@deviceId, @status, @tempF, @message, @raisedAt);
                """;
            cmd.Parameters.AddWithValue("@deviceId", telemetry.DeviceId);
            cmd.Parameters.AddWithValue("@status", result.Status);
            cmd.Parameters.AddWithValue("@tempF", telemetry.IndoorTempF);
            cmd.Parameters.AddWithValue("@message", result.Message);
            cmd.Parameters.AddWithValue("@raisedAt", telemetry.Timestamp.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);

            Logger.LogWarning("ALERT stored for {DeviceId}: {Status} — {Message}",
                telemetry.DeviceId, result.Status, result.Message);
        }
    }
}
