using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.Compatibility;

internal static class FinisherLethalTargetContract
{
    public static bool TryValidate(
        out MethodInfo? target,
        out FinisherLethalTargetFingerprint fingerprint,
        out string reason)
    {
        target = AccessTools.Method(
            typeof(Creature),
            nameof(Creature.LoseHpInternal),
            [typeof(decimal), typeof(ValueProp)]);
        if (!MethodBodyFingerprintCapture.TryCapture(
                target,
                out MethodBodyFingerprint method,
                out reason))
        {
            fingerprint = default;
            return false;
        }

        if (!GameHostContractProfile.TryResolve(method, out GameHostContractProfile profile))
        {
            fingerprint = default;
            reason = $"Unsupported Creature.LoseHpInternal host ({method}).";
            return false;
        }
        if (!StableMethodBodyContract.Matches(
                method,
                profile,
                profile.LethalDamage))
        {
            fingerprint = default;
            reason = $"Creature.LoseHpInternal fingerprint mismatch for {profile.Id} ({method}).";
            return false;
        }

        fingerprint = new FinisherLethalTargetFingerprint(profile.Id, method);
        reason = string.Empty;
        return true;
    }
}

internal readonly record struct FinisherLethalTargetFingerprint(
    string HostProfile,
    MethodBodyFingerprint Method)
{
    public override string ToString() => $"host={HostProfile}, method=[{Method}]";
}
