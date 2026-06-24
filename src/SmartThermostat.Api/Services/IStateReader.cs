namespace SmartThermostat.Api.Services;

/// <summary>
/// Narrow, read-only view of per-device state.
///
/// SOLID — Interface Segregation: API endpoints only ever READ state, so they depend on
/// this minimal interface rather than on a fat type that also exposes write/connection
/// members. No consumer is forced to depend on methods it doesn't use.
///
/// SOLID — Open/Closed &amp; Dependency Inversion: callers depend on this abstraction, so a
/// new backing store (in-memory test double, a different cache, a composite) can be added
/// by implementing this interface — without modifying the endpoints or existing readers.
/// </summary>
public interface IStateReader
{
    /// <summary>Returns the stored state JSON for a device, or null if none exists yet.</summary>
    Task<string?> GetRawAsync(string deviceId);
}
