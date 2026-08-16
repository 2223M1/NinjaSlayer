using System.Reflection;
using HarmonyLib;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

internal static class CombatPresentationPacingPatch
{
    private const string PatchIdPrefix = "ninjaslayer_combat_presentation_pacing";

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        if (!GameCompatibility.CombatPresentationPacing.TryResolveStateMachines(
                out GameCompatibility.RuntimePatchTarget[] targets,
                out string reason))
        {
            throw new MissingMethodException(reason);
        }

        var transpiler = new HarmonyMethod(
            typeof(CombatPresentationPacingPatch),
            nameof(Transpiler));
        return targets.Select(target => new DynamicPatchInfo(
                $"{PatchIdPrefix}_{target.IdSuffix}",
                target.Method,
                transpiler: transpiler,
                isCritical: true,
                description: $"Apply scoped combat presentation pacing to {target.IdSuffix}."))
            .ToArray();
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original)
    {
        MethodInfo customWait = GameCompatibility.CombatPresentationPacing.CustomWait
            ?? throw new MissingMethodException("Cmd.CustomScaledWait");
        bool isDamage = original.DeclaringType?.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd);
        MethodInfo scopedWait = AccessTools.Method(
                typeof(CombatPresentationPacingScope),
                isDamage
                    ? nameof(CombatPresentationPacingScope.WaitForDamageRecovery)
                    : nameof(CombatPresentationPacingScope.WaitForPowerRecovery))
            ?? throw new MissingMethodException(typeof(CombatPresentationPacingScope).FullName);
        var rewriter = HarmonyIlRewriter.From(instructions);
        HarmonyIlRewriteReport report = HarmonyAsyncIl.RedirectAwaitedCalls(
            rewriter,
            "NinjaSlayer scoped combat presentation pacing",
            customWait,
            scopedWait,
            code => code.Any(HarmonyIl.IsCall(scopedWait)));
        return rewriter.InstructionsChecked(report);
    }
}
