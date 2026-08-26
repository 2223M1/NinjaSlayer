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
            !IsNinjaSlayerPatch(patch) && patch.PatchMethod.ReturnType == typeof(bool));
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

        try
        {
            if (result != null)
            {
                token.Ledger.Confirm(token, result, originalRan);
            }
        }
        finally
        {
            if (token.IsConfirmed
                && FinisherSessionRegistry.GetActiveSession() is { } session
                && session.SessionId == token.SessionId
                && session.CombatEpoch == token.CombatEpoch)
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

    private static string DescribePatch(HarmonyLib.Patch patch) =>
        $"owner={patch.owner}, method={patch.PatchMethod.DeclaringType?.FullName}.{patch.PatchMethod.Name}, "
        + $"priority={patch.priority}, before=[{string.Join(',', patch.before)}], after=[{string.Join(',', patch.after)}]";
}
