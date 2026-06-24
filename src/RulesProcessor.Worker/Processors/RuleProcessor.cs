using System.Text.Json;
using StackExchange.Redis;
using SmartThermostat.Shared;

namespace RulesProcessor.Worker.Processors;

/// <summary>Status values produced by rule evaluation.</summary>
public static class RuleStatus
{
    public const string Ok = "Ok";
    public const string CoolingIneffective = "CoolingIneffective";
    public const string HighHumidity = "HighHumidity";
}

/// <summary>The outcome of a single fired rule against a telemetry event.</summary>
public sealed record RuleResult(string DeviceId, string Status, double IndoorTempF, string Message, string EventId);

/// <summary>
/// Evaluates the demo rules against incoming telemetry and publishes the verdict(s) to Redis
/// under <c>device:{id}:rule</c>. A single event may trip more than one rule, so evaluation
/// returns a list. Kept deliberately small and pure so AlertProcessor can reuse it.
/// </summary>
public sealed class RuleProcessor : TelemetryProcessorBase
{
    private readonly IConnectionMultiplexer _redis;

    // Rule thresholds.
    private const double CoolingDriftF = 3.0;   // indoorTempF - setPointF at/above this -> cooling issue
    private const int HighHumidityPct = 65;     // humidity strictly above this -> high humidity

    protected override string GroupId => "rule-processor";

    public RuleProcessor(IConfiguration config, ILogger<RuleProcessor> logger, IConnectionMultiplexer redis)
        : base(config, logger) => _redis = redis;

    /// <summary>
    /// Applies all demo rules and returns every result that fired. Returns a single "Ok"
    /// result when no rule trips.
    /// </summary>
    public static IReadOnlyList<RuleResult> Evaluate(Telemetry t)
    {
        var results = new List<RuleResult>();

        // Rule 1 — cooling not effective.
        // IF mode = Cool AND hvacState = Cooling AND indoorTempF - setPointF >= 3F
        if (string.Equals(t.Mode, "Cool", StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.HvacState, "Cooling", StringComparison.OrdinalIgnoreCase)
            && t.IndoorTempF - t.SetPointF >= CoolingDriftF)
        {
            results.Add(new(t.DeviceId, RuleStatus.CoolingIneffective, t.IndoorTempF,
                $"Possible cooling performance issue: indoor {t.IndoorTempF}F is " +
                $"{t.IndoorTempF - t.SetPointF:0.0}F above set point {t.SetPointF}F while cooling.",
                t.EventId));
        }

        // Rule 2 — high humidity.
        // IF humidity > 65
        if (t.Humidity > HighHumidityPct)
        {
            results.Add(new(t.DeviceId, RuleStatus.HighHumidity, t.IndoorTempF,
                $"High humidity: {t.Humidity}% exceeds {HighHumidityPct}%.", t.EventId));
        }

        if (results.Count == 0)
            results.Add(new(t.DeviceId, RuleStatus.Ok, t.IndoorTempF, "Within range.", t.EventId));

        return results;
    }

    protected override async Task HandleAsync(Telemetry telemetry, CancellationToken ct)
    {
        var results = Evaluate(telemetry);
        var key = $"device:{telemetry.DeviceId}:rule";
        await _redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(results));

        foreach (var r in results)
            Logger.LogInformation("Rule for {DeviceId}: {Status} — {Message}",
                r.DeviceId, r.Status, r.Message);
    }
}
