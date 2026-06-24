using RulesProcessor.Worker.Processors;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Shared Redis multiplexer used by State, Rule, and Analytics processors.
var redisConn = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConn));

// The four telemetry processors. Each runs as its own hosted service with its own Kafka
// consumer group, so all four receive every telemetry event (fan-out).
builder.Services.AddHostedService<StateProcessor>();
builder.Services.AddHostedService<RuleProcessor>();
builder.Services.AddHostedService<AlertProcessor>();
builder.Services.AddHostedService<AnalyticsProcessor>();

var host = builder.Build();
host.Run();
