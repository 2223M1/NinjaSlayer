using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

public static class TornadoFistFinisherCadencePatch
{
    private const string PatchIdPrefix = "ninjaslayer_tornado_fist_finisher_cadence";

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        if (!GameCompatibility.TornadoCadence.TryResolveStateMachines(
                out GameCompatibility.RuntimePatchTarget[] targets,
                out string missingMember))
        {
            throw new MissingMethodException(missingMember);
        }

        var harmonyTranspiler = new HarmonyMethod(
            typeof(TornadoFistFinisherCadencePatch),
            nameof(Transpiler));
        return targets.Select(target => new DynamicPatchInfo(
                $"{PatchIdPrefix}_{target.IdSuffix}",
                target.Method,
                transpiler: harmonyTranspiler,
                isCritical: true,
                description: $"Remove generic pacing from Tornado Fist {target.IdSuffix}."))
            .ToArray();
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        const string operation = "NinjaSlayer Tornado Fist awaited pacing redirect";
        MethodInfo customWait = GameCompatibility.TornadoCadence.CustomWait
            ?? throw new MissingMethodException(typeof(Cmd).FullName, nameof(Cmd.CustomScaledWait));
        MethodInfo scopedWait = GameCompatibility.TornadoCadence.ScopedWait
            ?? throw new MissingMethodException(
                typeof(TornadoFistFinisherCadenceContext).FullName,
                nameof(TornadoFistFinisherCadenceContext.WaitUnlessActive));
        var rewriter = HarmonyIlRewriter.From(instructions);
        HarmonyIlRewriteReport report = HarmonyAsyncIl.RedirectAwaitedCalls(
            rewriter,
            operation,
            customWait,
            scopedWait,
            code => code.Any(HarmonyIl.IsCall(scopedWait)));
        return rewriter.InstructionsChecked(report);
    }
}
