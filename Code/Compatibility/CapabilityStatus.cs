using System.Collections.Immutable;

namespace NinjaSlayer.Code.Compatibility;

internal enum CapabilityState
{
    NotEvaluated,
    Enabled,
    Degraded,
    Disabled
}

internal sealed record CapabilityProbe(
    string Name,
    bool IsAvailable,
    bool IsRequired,
    string Detail)
{
    public static CapabilityProbe Required(string name, bool isAvailable, string detail) =>
        new(name, isAvailable, true, detail);

    public static CapabilityProbe Optional(string name, bool isAvailable, string detail) =>
        new(name, isAvailable, false, detail);
}

internal sealed record CapabilityStatus
{
    public CapabilityStatus(
        CapabilityState state,
        string reason,
        int installedPatchCount,
        IEnumerable<CapabilityProbe>? probes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(installedPatchCount);
        State = state;
        Reason = reason;
        InstalledPatchCount = installedPatchCount;
        Probes = probes?.ToImmutableArray() ?? [];
    }

    public CapabilityState State { get; }
    public string Reason { get; }
    public int InstalledPatchCount { get; }
    public ImmutableArray<CapabilityProbe> Probes { get; }
    public bool IsOperational => State is CapabilityState.Enabled or CapabilityState.Degraded;

    public static CapabilityStatus NotEvaluated { get; } =
        new(CapabilityState.NotEvaluated, "Capability has not been evaluated.", 0);
}

internal static class CapabilityStatusEvaluator
{
    public static CapabilityStatus EvaluatePatchResult(
        IEnumerable<CapabilityProbe>? probes,
        bool patchAllSucceeded,
        int registeredPatchCount,
        int appliedPatchCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registeredPatchCount);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedPatchCount);
        if (appliedPatchCount > registeredPatchCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedPatchCount),
                "Applied patch count cannot exceed registered patch count.");
        }

        ImmutableArray<CapabilityProbe> snapshot = probes?.ToImmutableArray() ?? [];
        CapabilityProbe? requiredFailure = snapshot.FirstOrDefault(probe => probe.IsRequired && !probe.IsAvailable);
        if (requiredFailure != null)
        {
            return new CapabilityStatus(
                CapabilityState.Disabled,
                $"Required compatibility probe failed: {requiredFailure.Name} ({requiredFailure.Detail}).",
                appliedPatchCount,
                snapshot);
        }

        if (!patchAllSucceeded)
        {
            return new CapabilityStatus(
                CapabilityState.Disabled,
                $"Patch installation failed and was rolled back ({appliedPatchCount}/{registeredPatchCount} applied).",
                appliedPatchCount,
                snapshot);
        }

        CapabilityProbe[] optionalFailures = snapshot
            .Where(probe => !probe.IsRequired && !probe.IsAvailable)
            .ToArray();
        if (optionalFailures.Length > 0 || appliedPatchCount < registeredPatchCount)
        {
            var reasons = new List<string>(2);
            if (optionalFailures.Length > 0)
            {
                reasons.Add(
                    "optional probes unavailable: "
                    + string.Join(", ", optionalFailures.Select(probe => probe.Name)));
            }
            if (appliedPatchCount < registeredPatchCount)
            {
                reasons.Add($"patches applied: {appliedPatchCount}/{registeredPatchCount}");
            }

            return new CapabilityStatus(
                CapabilityState.Degraded,
                string.Join("; ", reasons),
                appliedPatchCount,
                snapshot);
        }

        return new CapabilityStatus(
            CapabilityState.Enabled,
            $"All {registeredPatchCount} registered patches applied.",
            appliedPatchCount,
            snapshot);
    }

    public static CapabilityStatus DisabledByDependency(
        string dependencyId,
        IEnumerable<CapabilityProbe>? probes = null) =>
        new(
            CapabilityState.Disabled,
            $"Required capability is not operational: {dependencyId}.",
            0,
            probes);
}
