namespace SmartThermostat.Shared.Messaging;

/// <summary>
/// Kafka/Redpanda topic names. For the demo a single common topic carries all telemetry,
/// consumed by every worker service.
/// </summary>
public static class Topics
{
    public const string Telemetry = "thermostat-telemetry";
}
