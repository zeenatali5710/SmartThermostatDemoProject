using System.Text.Json;
using Confluent.Kafka;
using SmartThermostat.Shared;
using SmartThermostat.Shared.Messaging;

namespace SmartThermostat.Api.Services;

/// <summary>
/// Publishes telemetry events to the common Redpanda/Kafka topic. Registered as a singleton
/// so the underlying producer (and its connection) is reused across requests.
/// </summary>
public sealed class TelemetryProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<TelemetryProducer> _logger;

    public TelemetryProducer(IConfiguration config, ILogger<TelemetryProducer> logger)
    {
        _logger = logger;
        var bootstrap = config.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:19092";
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrap,
            // Demo-friendly: fail fast instead of blocking forever if the broker is down.
            MessageTimeoutMs = 5000,
        };
        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    /// <summary>Publishes telemetry keyed by DeviceId so per-device ordering is preserved.</summary>
    public async Task PublishAsync(Telemetry telemetry, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(telemetry);
        var result = await _producer.ProduceAsync(
            Topics.Telemetry,
            new Message<string, string> { Key = telemetry.DeviceId, Value = payload },
            ct);

        _logger.LogInformation(
            "Published telemetry {EventId} for {DeviceId} to {TopicPartitionOffset}",
            telemetry.EventId, telemetry.DeviceId, result.TopicPartitionOffset);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
