using StackExchange.Redis;
using SmartThermostat.Shared;

namespace RulesProcessor.Worker.Processors;

/// <summary>
/// Maintains simple rolling analytics per device in a Redis hash: total event count and a
/// running average of indoor temperature. Demonstrates a stateful aggregation consumer that
/// reads its own prior state from Redis and updates it incrementally.
/// </summary>
public sealed class AnalyticsProcessor : TelemetryProcessorBase
{
    private readonly IConnectionMultiplexer _redis;

    protected override string GroupId => "analytics-processor";

    public AnalyticsProcessor(IConfiguration config, ILogger<AnalyticsProcessor> logger, IConnectionMultiplexer redis)
        : base(config, logger) => _redis = redis;

    protected override async Task HandleAsync(Telemetry telemetry, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = $"device:{telemetry.DeviceId}:analytics";

        // Atomic increment of the count, then fold the new reading into the running average.
        var count = await db.HashIncrementAsync(key, "count");
        var prevAvg = (double?)await db.HashGetAsync(key, "avgTempF") ?? telemetry.IndoorTempF;
        var newAvg = prevAvg + (telemetry.IndoorTempF - prevAvg) / count;

        await db.HashSetAsync(key,
        [
            new HashEntry("avgTempF", newAvg),
            new HashEntry("lastTempF", telemetry.IndoorTempF),
            new HashEntry("lastSeen", telemetry.Timestamp.ToString("O")),
        ]);

        Logger.LogInformation("Analytics {DeviceId}: n={Count} avgTempF={Avg:0.0}",
            telemetry.DeviceId, count, newAvg);
    }
}
