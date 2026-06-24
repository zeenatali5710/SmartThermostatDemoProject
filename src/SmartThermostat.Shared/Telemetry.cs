namespace SmartThermostat.Shared;

/// <summary>
/// A single thermostat telemetry event. This is the one message contract published to the
/// common topic and consumed by all worker services (StateProcessor, RuleProcessor,
/// AlertProcessor, AnalyticsProcessor).
/// </summary>
public sealed record Telemetry
{
    public required string EventId { get; init; }
    public required string DeviceId { get; init; }
    public required string AccountId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required double IndoorTempF { get; init; }
    public required double SetPointF { get; init; }

    /// <summary>Thermostat mode: e.g. "Cool", "Heat", "Off".</summary>
    public required string Mode { get; init; }

    /// <summary>Current HVAC activity: e.g. "Cooling", "Heating", "Idle".</summary>
    public required string HvacState { get; init; }

    /// <summary>Relative humidity percentage (0-100).</summary>
    public int Humidity { get; init; }

    /// <summary>Battery level percentage (0-100).</summary>
    public int Battery { get; init; }
}
