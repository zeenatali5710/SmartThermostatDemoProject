using RulesProcessor.Worker.Processors;
using SmartThermostat.Shared;
using Xunit;

namespace RulesProcessor.Worker.Tests;

/// <summary>
/// Tests for <see cref="RuleProcessor.Evaluate"/>. The method is pure (no Kafka/Redis),
/// so we can exercise every rule branch with plain in-memory telemetry.
/// </summary>
public class RuleProcessorEvaluateTests
{
    /// <summary>Builds a baseline "healthy" telemetry event; override fields per test.</summary>
    private static Telemetry Reading(
        double indoorTempF = 72,
        double setPointF = 72,
        string mode = "Cool",
        string hvacState = "Idle",
        int humidity = 40) => new()
        {
            EventId = "evt-1",
            DeviceId = "dev-1",
            AccountId = "acct-1",
            Timestamp = DateTimeOffset.UnixEpoch,
            IndoorTempF = indoorTempF,
            SetPointF = setPointF,
            Mode = mode,
            HvacState = hvacState,
            Humidity = humidity,
        };

    [Fact]
    public void WithinRange_ReturnsSingleOkResult()
    {
        var results = RuleProcessor.Evaluate(Reading());

        var result = Assert.Single(results);
        Assert.Equal(RuleStatus.Ok, result.Status);
    }

    [Fact]
    public void CoolingDriftAtThreshold_FiresCoolingIneffective()
    {
        // indoor - setpoint == 3.0F, exactly at threshold, while actively cooling.
        var results = RuleProcessor.Evaluate(
            Reading(indoorTempF: 75, setPointF: 72, mode: "Cool", hvacState: "Cooling"));

        Assert.Contains(results, r => r.Status == RuleStatus.CoolingIneffective);
    }

    [Fact]
    public void CoolingDriftBelowThreshold_DoesNotFire()
    {
        // 2F drift is below the 3F threshold.
        var results = RuleProcessor.Evaluate(
            Reading(indoorTempF: 74, setPointF: 72, mode: "Cool", hvacState: "Cooling"));

        Assert.DoesNotContain(results, r => r.Status == RuleStatus.CoolingIneffective);
    }

    [Fact]
    public void CoolingDrift_IgnoredWhenNotCooling()
    {
        // Big drift, but HVAC is idle — the rule requires hvacState == "Cooling".
        var results = RuleProcessor.Evaluate(
            Reading(indoorTempF: 80, setPointF: 72, mode: "Cool", hvacState: "Idle"));

        Assert.DoesNotContain(results, r => r.Status == RuleStatus.CoolingIneffective);
    }

    [Fact]
    public void ModeMatchIsCaseInsensitive()
    {
        var results = RuleProcessor.Evaluate(
            Reading(indoorTempF: 75, setPointF: 72, mode: "cool", hvacState: "cooling"));

        Assert.Contains(results, r => r.Status == RuleStatus.CoolingIneffective);
    }

    [Fact]
    public void HumidityAboveThreshold_FiresHighHumidity()
    {
        var results = RuleProcessor.Evaluate(Reading(humidity: 66));

        Assert.Contains(results, r => r.Status == RuleStatus.HighHumidity);
    }

    [Fact]
    public void HumidityAtThreshold_DoesNotFire()
    {
        // Rule is "strictly greater than 65", so 65 must not fire.
        var results = RuleProcessor.Evaluate(Reading(humidity: 65));

        Assert.DoesNotContain(results, r => r.Status == RuleStatus.HighHumidity);
    }

    [Fact]
    public void MultipleRules_CanFireForOneEvent()
    {
        // Cooling drift AND high humidity at once -> two results, no "Ok".
        var results = RuleProcessor.Evaluate(
            Reading(indoorTempF: 76, setPointF: 72, mode: "Cool", hvacState: "Cooling", humidity: 70));

        Assert.Contains(results, r => r.Status == RuleStatus.CoolingIneffective);
        Assert.Contains(results, r => r.Status == RuleStatus.HighHumidity);
        Assert.DoesNotContain(results, r => r.Status == RuleStatus.Ok);
    }
}
