using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.LogicTests;

public sealed class CapabilityStatusTests
{
    [Fact]
    public void CapabilityStatusSnapshotsProbeInputs()
    {
        var probes = new List<CapabilityProbe>
        {
            CapabilityProbe.Required("required", true, "available")
        };

        CapabilityStatus status = CapabilityStatusEvaluator.EvaluatePatchResult(
            probes,
            patchAllSucceeded: true,
            registeredPatchCount: 1,
            appliedPatchCount: 1);
        probes[0] = CapabilityProbe.Required("changed", false, "missing");
        probes.Add(CapabilityProbe.Optional("added", false, "missing"));

        CapabilityProbe snapshot = Assert.Single(status.Probes);
        Assert.Equal("required", snapshot.Name);
        Assert.True(snapshot.IsAvailable);
        Assert.Equal(CapabilityState.Enabled, status.State);
    }

    [Fact]
    public void CapabilityStatusDistinguishesEnabledDegradedAndDisabledResults()
    {
        CapabilityStatus enabled = CapabilityStatusEvaluator.EvaluatePatchResult(
            [CapabilityProbe.Required("required", true, "available")],
            patchAllSucceeded: true,
            registeredPatchCount: 2,
            appliedPatchCount: 2);
        CapabilityStatus degraded = CapabilityStatusEvaluator.EvaluatePatchResult(
            [CapabilityProbe.Optional("optional", false, "missing")],
            patchAllSucceeded: true,
            registeredPatchCount: 2,
            appliedPatchCount: 1);
        CapabilityStatus missingRequirement = CapabilityStatusEvaluator.EvaluatePatchResult(
            [CapabilityProbe.Required("required", false, "missing")],
            patchAllSucceeded: false,
            registeredPatchCount: 0,
            appliedPatchCount: 0);
        CapabilityStatus rolledBack = CapabilityStatusEvaluator.EvaluatePatchResult(
            [],
            patchAllSucceeded: false,
            registeredPatchCount: 2,
            appliedPatchCount: 0);

        Assert.Equal(CapabilityState.Enabled, enabled.State);
        Assert.True(enabled.IsOperational);
        Assert.Equal(CapabilityState.Degraded, degraded.State);
        Assert.True(degraded.IsOperational);
        Assert.Contains("optional probes unavailable", degraded.Reason, StringComparison.Ordinal);
        Assert.Contains("1/2", degraded.Reason, StringComparison.Ordinal);
        Assert.Equal(CapabilityState.Disabled, missingRequirement.State);
        Assert.Contains("Required compatibility probe failed", missingRequirement.Reason, StringComparison.Ordinal);
        Assert.Equal(CapabilityState.Disabled, rolledBack.State);
        Assert.Contains("rolled back", rolledBack.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityRegistryReturnsStableSnapshotsAndReadOnlyGates()
    {
        var registry = new NinjaSlayerCapabilityRegistry();
        CapabilityStatus enabled = CapabilityStatusEvaluator.EvaluatePatchResult(
            [],
            patchAllSucceeded: true,
            registeredPatchCount: 1,
            appliedPatchCount: 1);

        registry.Publish("first", enabled);
        IReadOnlyDictionary<string, CapabilityStatus> firstSnapshot = registry.Snapshot();
        registry.Publish("second", CapabilityStatusEvaluator.DisabledByDependency("first"));

        Assert.True(registry.IsOperational("first"));
        Assert.False(registry.IsOperational("second"));
        Assert.Equal(CapabilityState.NotEvaluated, registry.Get("unknown").State);
        Assert.Single(firstSnapshot);
        Assert.DoesNotContain("second", firstSnapshot.Keys);
        Assert.Equal(2, registry.Snapshot().Count);
    }
}
