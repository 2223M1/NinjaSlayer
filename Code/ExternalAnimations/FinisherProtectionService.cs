using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherProtectionService
{
    private static readonly MethodInfo LethalDamage = AccessTools.Method(
        typeof(Creature),
        nameof(Creature.LoseHpInternal),
        [typeof(decimal), typeof(ValueProp)])
        ?? throw new MissingMethodException(typeof(Creature).FullName, nameof(Creature.LoseHpInternal));

    internal static bool CanProtectLethalDamage(out string reason)
    {
        HarmonyLib.Patches? patchInfo = Harmony.GetPatchInfo(LethalDamage);
        if (patchInfo == null)
        {
            reason = string.Empty;
            return true;
        }

        HarmonyLib.Patch? unsafeTranspiler = patchInfo.Transpilers
            .FirstOrDefault(patch => !IsNinjaSlayerPatch(patch));
        if (unsafeTranspiler != null)
        {
            reason = $"foreign transpiler {DescribePatch(unsafeTranspiler)} targets Creature.LoseHpInternal.";
            return false;
        }

        HarmonyLib.Patch? skippingPrefix = patchInfo.Prefixes.FirstOrDefault(patch =>
            !IsNinjaSlayerPatch(patch) && patch.PatchMethod.ReturnType == typeof(bool)
            && !IsVerifiedHpPrefix(patch));
        if (skippingPrefix != null)
        {
            reason = $"foreign bool Prefix {DescribePatch(skippingPrefix)} can skip Creature.LoseHpInternal.";
            return false;
        }

        HarmonyLib.Patch? resultReplacement = patchInfo.Prefixes
            .Concat(patchInfo.Postfixes)
            .Concat(patchInfo.Finalizers)
            .FirstOrDefault(patch =>
                !IsNinjaSlayerPatch(patch)
                && !IsVerifiedHpPrefix(patch)
                && patch.PatchMethod.GetParameters().Any(parameter =>
                    parameter.Name == "__result"
                    && parameter.ParameterType.IsByRef
                    && parameter.ParameterType.GetElementType() == typeof(DamageResult)));
        if (resultReplacement != null)
        {
            reason = $"foreign result-replacement Patch {DescribePatch(resultReplacement)} targets Creature.LoseHpInternal.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal static void TryProtectLethalDamage(
        Creature target,
        ref decimal amount,
        out FinisherProtectionToken? token)
    {
        token = null;
        FinisherSession? session = FinisherSessionRegistry.GetActiveSession();
        if (session == null && NinjaSlayerDeathClassifier.TryStartReverseFinisher(target, amount))
        {
            session = FinisherSessionRegistry.GetActiveSession();
        }

        session?.TryProtectLethalDamage(target, ref amount, out token);
    }

    internal static void ConfirmProtectedDamageResult(
        DamageResult? result,
        bool originalRan,
        FinisherProtectionToken? token)
    {
        if (token == null)
        {
            return;
        }

        if (result == null || !token.Ledger.Confirm(token, result, originalRan))
        {
            return;
        }

        try
        {
            Telemetry.NinjaSlayerCombatTelemetry.ProtectedDamage(result, token);
            token.Ledger.PresentProtectedDamage(token, result);
        }
        finally
        {
            if (FinisherSessionRegistry.GetActiveSession() is { } session
                && session.OwnsProtection(token))
            {
                session.NotifyProtectedDamageConfirmed();
            }
        }
    }

    internal static void FinalizeLethalProtection(FinisherProtectionToken? token)
    {
        token?.Ledger.FinalizeProtection(token);
    }

    internal static bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage)
    {
        if (FinisherSessionRegistry.GetActiveSession() is { } session)
        {
            return session.TryTakeDamageDisplayOverride(result, out displayDamage);
        }

        displayDamage = 0;
        return false;
    }

    private static bool IsNinjaSlayerPatch(HarmonyLib.Patch patch) =>
        patch.PatchMethod.DeclaringType?.Assembly == typeof(FinisherProtectionService).Assembly;

    // Both return an independent nonlethal result when intercepting HP loss. The final
    // protection prefix checks __runOriginal after them before reserving any death.
    private static bool IsVerifiedHpPrefix(HarmonyLib.Patch patch)
    {
        MethodInfo method = patch.PatchMethod;
        if (method.Name != "Prefix") return false;
        if (method.DeclaringType?.FullName is
            "HextechRunes.HextechCombatHooks+NearDeathFeastLoseHpPatch"
            or "Loadout.Patches.TildeKey.TildeKeyGodmodeLoseHpPatch") return true;

        // Official MinionLib 0.5.2/0.6.3 suppress only the owner's temporary
        // fallback loss while guardians distribute overflow. Actual loss runs later.
        return patch.owner == "MinionLib"
            && method.DeclaringType?.Assembly.GetName().Name == "MinionLib"
            && method.DeclaringType.FullName == "MinionLib.Powers.Patches.MinionGuardianOwnerDamageSuppressPatch"
            && method.IsStatic && method.ReturnType == typeof(bool)
            && method.GetParameters().Select(parameter => (parameter.Name, parameter.ParameterType)).SequenceEqual(
                new (string?, Type)[] { ("__instance", typeof(Creature)), ("amount", typeof(decimal)),
                    ("props", typeof(ValueProp)), ("__result", typeof(DamageResult).MakeByRefType()) });
    }

    private static string DescribePatch(HarmonyLib.Patch patch) =>
        $"owner={patch.owner}, method={patch.PatchMethod.DeclaringType?.FullName}.{patch.PatchMethod.Name}, "
        + $"priority={patch.priority}, before=[{string.Join(',', patch.before)}], after=[{string.Join(',', patch.after)}]";
}
