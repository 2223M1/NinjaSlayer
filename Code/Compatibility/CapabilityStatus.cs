namespace NinjaSlayer.Code.Compatibility;

internal enum CapabilityState
{
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

internal sealed record CapabilityStatus(
    CapabilityState State,
    string Reason,
    int InstalledPatchCount)
{
    public bool IsOperational => State is CapabilityState.Enabled or CapabilityState.Degraded;
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

        CapabilityProbe[] probeArray = probes?.ToArray() ?? [];
        CapabilityProbe? requiredFailure = probeArray.FirstOrDefault(probe => probe.IsRequired && !probe.IsAvailable);
        if (requiredFailure != null)
        {
            return new CapabilityStatus(
                CapabilityState.Disabled,
                $"Required compatibility probe failed: {requiredFailure.Name} ({requiredFailure.Detail}).",
                appliedPatchCount);
        }

        if (!patchAllSucceeded)
        {
            return new CapabilityStatus(
                CapabilityState.Disabled,
                $"Patch installation failed and was rolled back ({appliedPatchCount}/{registeredPatchCount} applied).",
                appliedPatchCount);
        }

        CapabilityProbe[] optionalFailures = probeArray
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
                appliedPatchCount);
        }

        return new CapabilityStatus(
            CapabilityState.Enabled,
            $"All {registeredPatchCount} registered patches applied.",
            appliedPatchCount);
    }

    public static CapabilityStatus DisabledByDependency(string dependencyId) =>
        new(
            CapabilityState.Disabled,
            $"Required capability is not operational: {dependencyId}.",
            0);

    public static CapabilityStatus RolledBack(string failedCapabilityId) =>
        new(
            CapabilityState.Disabled,
            $"Core activation was rolled back after {failedCapabilityId} failed.",
            0);
}
