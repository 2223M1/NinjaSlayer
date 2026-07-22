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

namespace NinjaSlayer.Code.Patches;

public sealed class TornadoFistFinisherCadencePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_tornado_fist_finisher_cadence";
    public static string Description => "Remove only generic damage and power pacing waits from Tornado Fist finishers.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() => TryResolveTargets(out ModPatchTarget[] targets, out _)
        ? targets
        : [];

    private static bool TryResolveTargets(out ModPatchTarget[] targets, out string missingMember)
    {
        if (!GameCompatibility.TornadoCadence.TryResolveStateMachines(
                out Type[] stateMachines,
                out missingMember))
        {
            targets = [];
            return false;
        }

        targets = stateMachines
            .Select(stateMachine => new ModPatchTarget(stateMachine, "MoveNext"))
            .ToArray();
        return true;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> rewritten = instructions.ToList();
        int replacements = 0;
        foreach (CodeInstruction instruction in rewritten)
        {
            if (GameCompatibility.TornadoCadence.CustomWait is not { } customWait
                || GameCompatibility.TornadoCadence.ScopedWait is not { } scopedWait
                || !instruction.Calls(customWait))
            {
                continue;
            }

            instruction.operand = scopedWait;
            replacements++;
        }

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one pacing wait in the patched async method, found {replacements}.");
        }

        return rewritten;
    }

}
