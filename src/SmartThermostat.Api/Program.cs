using SmartThermostat.Api.Services;
using SmartThermostat.Shared;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- Services (composition root) ---------------------------------------------------------

// Swagger / OpenAPI so the API is drivable from a browser and Postman can import the spec.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis connection (shared, thread-safe multiplexer) for reading latest device state.
var redisConn = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConn));

// Write side: publishes telemetry to Redpanda. Read sides: Redis state + SQLite alerts.
builder.Services.AddSingleton<TelemetryProducer>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<AlertStore>();

var app = builder.Build();

// Ensure the SQLite alerts table exists so GET /alerts works before any worker writes.
await app.Services.GetRequiredService<AlertStore>().EnsureSchemaAsync();

// --- HTTP pipeline -----------------------------------------------------------------------

app.UseSwagger();
app.UseSwaggerUI();

// --- Endpoints ---------------------------------------------------------------------------

// WRITE side: ingest telemetry, publish to the topic, return 202 (fire-and-forget).
app.MapPost("/telemetry", async (Telemetry telemetry, TelemetryProducer producer, CancellationToken ct) =>
{
    await producer.PublishAsync(telemetry, ct);
    return Results.Accepted($"/state/{telemetry.DeviceId}", new { telemetry.EventId, telemetry.DeviceId });
})
.WithName("IngestTelemetry")
.WithSummary("Ingest a thermostat telemetry event and publish it to the topic.");

// READ side: latest state for a device (written to Redis by StateProcessor).
app.MapGet("/state/{deviceId}", async (string deviceId, StateStore store) =>
{
    var json = await store.GetRawAsync(deviceId);
    return json is null
        ? Results.NotFound(new { deviceId, message = "No state yet for this device." })
        : Results.Content(json, "application/json");
})
.WithName("GetDeviceState")
.WithSummary("Get the latest known state for a device.");

// Liveness/readiness: report whether the API's backing dependencies are reachable.
app.MapGet("/health", async (IConnectionMultiplexer redis, AlertStore alerts) =>
{
    var redisOk = false;
    try
    {
        redisOk = (await redis.GetDatabase().PingAsync()) >= TimeSpan.Zero;
    }
    catch
    {
        // Treat any connection/ping failure as unhealthy; details are intentionally not leaked.
    }

    var sqliteOk = false;
    try
    {
        // EnsureSchemaAsync opens the SQLite connection and runs a trivial command.
        await alerts.EnsureSchemaAsync();
        sqliteOk = true;
    }
    catch
    {
        // Unhealthy if the database can't be opened.
    }

    var healthy = redisOk && sqliteOk;
    var body = new { status = healthy ? "healthy" : "unhealthy", redis = redisOk, sqlite = sqliteOk };
    return healthy ? Results.Ok(body) : Results.Json(body, statusCode: 503);
})
.WithName("GetHealth")
.WithSummary("Report API health, including Redis and SQLite connectivity.");

// READ side: recent alerts (written to SQLite by AlertProcessor), optionally filtered.
app.MapGet("/alerts", async (AlertStore store, string? deviceId, int? limit) =>
{
    var alerts = await store.GetRecentAsync(deviceId, Math.Clamp(limit ?? 50, 1, 500));
    return Results.Ok(alerts);
})
.WithName("GetAlerts")
.WithSummary("List recent alerts, optionally filtered by deviceId.");

app.Run();
