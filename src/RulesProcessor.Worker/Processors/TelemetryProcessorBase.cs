using System.Text.Json;
using Confluent.Kafka;
using SmartThermostat.Shared;
using SmartThermostat.Shared.Messaging;

namespace RulesProcessor.Worker.Processors;

/// <summary>
/// Base class for the four telemetry processors. Each subclass runs in its own Kafka
/// consumer group, so every processor receives a copy of every telemetry event (fan-out).
/// Subclasses implement <see cref="HandleAsync"/> with their own logic.
/// </summary>
public abstract class TelemetryProcessorBase : BackgroundService
{
    private readonly IConfiguration _config;
    protected readonly ILogger Logger;

    /// <summary>The Kafka consumer group id; one per processor to fan out the topic.</summary>
    protected abstract string GroupId { get; }

    protected TelemetryProcessorBase(IConfiguration config, ILogger logger)
    {
        _config = config;
        Logger = logger;
    }

    protected abstract Task HandleAsync(Telemetry telemetry, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrap = _config.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:19092";
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(Topics.Telemetry);
        Logger.LogInformation("{Processor} subscribed to {Topic} as group {GroupId}",
            GetType().Name, Topics.Telemetry, GroupId);

        // Run the blocking consume loop off the startup thread so the host can start all
        // processors concurrently.
        await Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value is null) continue;

                    var telemetry = JsonSerializer.Deserialize<Telemetry>(result.Message.Value);
                    if (telemetry is null) continue;

                    await HandleAsync(telemetry, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "{Processor} failed to process a message", GetType().Name);
                }
            }
            consumer.Close();
        }, stoppingToken);
    }
}
