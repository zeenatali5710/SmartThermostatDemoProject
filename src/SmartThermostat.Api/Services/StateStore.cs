using StackExchange.Redis;

namespace SmartThermostat.Api.Services;

/// <summary>
/// Read access to the latest per-device state in Redis. The StateProcessor worker writes
/// these keys as JSON; the API only reads them and returns the raw JSON document.
///
/// SOLID — Single Responsibility: this type's only job is reading device state from Redis.
/// It implements <see cref="IStateReader"/> (Interface Segregation) so callers depend on
/// the read contract, not on this concrete Redis-backed class (Dependency Inversion).
/// </summary>
public sealed class StateStore : IStateReader
{
    private readonly IConnectionMultiplexer _redis;

    public StateStore(IConnectionMultiplexer redis) => _redis = redis;

    public static string Key(string deviceId) => $"device:{deviceId}:state";

    /// <summary>Returns the stored state JSON for a device, or null if none exists yet.</summary>
    public async Task<string?> GetRawAsync(string deviceId)
    {
        var value = await _redis.GetDatabase().StringGetAsync(Key(deviceId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }
}
