using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class YamotoKokiDodgeAttackScopePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_dodge_attack_scope";
    public static string Description => "Track enemy attacks that can pre-cue Yamoto Koki's dodge.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(AttackCommand),
            nameof(AttackCommand.Execute),
            [typeof(PlayerChoiceContext)])
    ];

    public static void Prefix(
        AttackCommand __instance,
        out object? __state)
    {
        __state = YamotoKokiDodgeAttackContext.Enter(__instance);
    }

    public static void Postfix(
        ref Task<AttackCommand> __result,
        object? __state)
    {
        if (__state is not YamotoKokiDodgeAttackContext.Frame frame)
        {
            return;
        }

        YamotoKokiDodgeAttackContext.RestoreCaller(frame);
        __result = YamotoKokiDodgeAttackContext.Complete(__result, frame);
    }
}

public sealed class YamotoKokiDodgeAttackAnimationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_dodge_attack_animation";
    public static string Description => "Start Yamoto Koki's dodge shortly before an incoming hit.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CreatureCmd),
            nameof(CreatureCmd.TriggerAnim),
            [typeof(Creature), typeof(string), typeof(float)])
    ];

    public static void Postfix(Creature creature, string triggerName, float waitTime)
    {
        YamotoKokiDodgeAttackContext.OnAttackerAnimation(creature, triggerName, waitTime);
    }
}

public sealed class YamotoKokiDodgePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_dodge";
    public static string Description =>
        "Make Yamoto Koki evade owner-targeted attacks and keep her missiles unhittable.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CreatureCmd),
            nameof(CreatureCmd.Damage),
            [
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<Creature>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel),
                typeof(CardPlay)
            ])
    ];

    public static void Prefix(
        ref IEnumerable<Creature>? targets,
        ValueProp props,
        Creature? dealer)
    {
        List<Creature> targetList = targets?
            .Where(target => target.Monster is not YamotoKokiGasBomb)
            .ToList() ?? [];
        targets = targetList;

        if (dealer is not { IsMonster: true } || !props.IsCardOrMonsterMove())
        {
            return;
        }

        foreach (var player in targetList
                     .Where(target => target.Side != dealer.Side)
                     .Select(target => target.Player ?? target.PetOwner)
                     .OfType<Player>()
                     .Distinct())
        {
            Creature? yamotoKoki = player.PlayerCombatState?.Pets
                .FirstOrDefault(pet => pet.Monster is YamotoKokiMonster && pet.IsAlive);
            if (yamotoKoki != null)
            {
                YamotoKokiDodgeAnimation.NotifyImpact(yamotoKoki);
            }
        }
    }
}
