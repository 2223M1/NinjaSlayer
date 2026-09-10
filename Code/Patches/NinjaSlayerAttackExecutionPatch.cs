using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using NinjaSlayer.Code.Combat;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class NinjaSlayerAttackExecutionPatch : IPatchMethod
{
    private static readonly FieldInfo SingleTarget = AccessTools.Field(typeof(AttackCommand), "_singleTarget")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_singleTarget");

    public static string PatchId => "ninjaslayer_attack_execution";
    public static string Description => "Keep target and intra-card cadence with the actual attack execution.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(AttackCommand), nameof(AttackCommand.Execute), [typeof(PlayerChoiceContext)])];

    [HarmonyPriority(Priority.First)]
    public static void Prefix(AttackCommand __instance, out NinjaSlayerAttackExecution.CommandLease __state) =>
        __state = NinjaSlayerAttackExecution.Enter(__instance, (Creature?)SingleTarget.GetValue(__instance));

    public static void Postfix(NinjaSlayerAttackExecution.CommandLease __state) => __state.RestoreCaller();

    public static Exception? Finalizer(Exception? __exception, NinjaSlayerAttackExecution.CommandLease __state)
    {
        __state.RestoreCaller();
        return __exception;
    }
}

internal sealed class NinjaSlayerAttackHitCountPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_attack_actual_hits";
    public static string Description => "Observe the host's resolved hit count without calling its hooks again.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Hook), nameof(Hook.ModifyAttackHitCount), [typeof(ICombatState), typeof(AttackCommand), typeof(int)])];

    public static void Postfix(AttackCommand __1, decimal __result) =>
        NinjaSlayerAttackExecution.SetActualHitCount(__1, __result);
}
