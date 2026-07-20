using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerSurroundedFacingPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_surrounded_facing";
    public static string Description => "Move persistent Kaiser Crab facing from the animated body to the NinjaSlayer visual rig.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(SurroundedPower),
            "FaceDirection",
            [typeof(SurroundedPower.Direction)])
    ];

    public static void Prefix(
        SurroundedPower __instance,
        out (Creature? Creature, float BodyScaleX, bool RestoreBodyScale) __state)
    {
        __state = NinjaSlayerFacingState.CaptureSurroundedBody(__instance);
    }

    public static void Postfix(
        SurroundedPower __instance,
        (Creature? Creature, float BodyScaleX, bool RestoreBodyScale) __state,
        ref Task __result)
    {
        __result = NinjaSlayerFacingState.TransferSurroundedFacing(
            __result,
            __instance,
            __state.Creature,
            __state.BodyScaleX,
            __state.RestoreBodyScale);
    }
}

public sealed class NinjaSlayerAttackFacingPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_attack_facing";
    public static string Description => "Persist NinjaSlayer attack facing and mirror its cinematic focus with the visual rig.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(Hook),
            nameof(Hook.BeforeCardPlayed),
            [typeof(ICombatState), typeof(CardPlay)])
    ];

    public static void Postfix(CardPlay cardPlay, ref Task __result)
    {
        __result = NinjaSlayerFacingState.SyncAfterBeforeCardPlayed(__result, cardPlay);
    }
}
