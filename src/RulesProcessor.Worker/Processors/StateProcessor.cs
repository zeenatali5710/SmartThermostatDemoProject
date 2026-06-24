using System.Text.Json;
using StackExchange.Redis;
using SmartThermostat.Shared;

namespace RulesProcessor.Worker.Processors;

/// <summary>
/// Maintains the latest known state per device in Redis. Overwrites the device key on every
/// telemetry event so the API can answer "what is device X doing right now?" instantly.
/// </summary>
public sealed class StateProcessor : TelemetryProcessorBase
{
    private readonly IConnectionMultiplexer _redis;

    protected override string GroupId => "state-processor";

    public StateProcessor(IConfiguration config, ILogger<StateProcessor> logger, IConnectionMultiplexer redis)
        : base(config, logger) => _redis = redis;

    protected override async Task HandleAsync(Telemetry telemetry, CancellationToken ct)
    {
        var key = $"device:{telemetry.DeviceId}:state";
        var json = JsonSerializer.Serialize(telemetry);
        await _redis.GetDatabase().StringSetAsync(key, json);
        Logger.LogInformation("State updated for {DeviceId} ({TempF}F)",
            telemetry.DeviceId, telemetry.IndoorTempF);
    }
}
