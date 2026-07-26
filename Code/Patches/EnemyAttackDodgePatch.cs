using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class EnemyAttackDodgeScopePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_enemy_attack_dodge_scope";
    public static string Description => "Track enemy attacks that can pre-cue ally dodge animations.";
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
        __state = EnemyAttackDodgeContext.Enter(__instance);
    }

    public static void Postfix(
        ref Task<AttackCommand> __result,
        object? __state)
    {
        if (__state is not EnemyAttackDodgeContext.Frame frame)
        {
            return;
        }

        EnemyAttackDodgeContext.RestoreCaller(frame);
        __result = EnemyAttackDodgeContext.Complete(__result, frame);
    }
}

public sealed class EnemyAttackDodgeAnimationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_enemy_attack_dodge_animation";
    public static string Description => "Start ally dodge animations shortly before an incoming hit.";
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
        EnemyAttackDodgeContext.OnAttackerAnimation(creature, triggerName, waitTime);
    }
}

public sealed class AllyDodgeImpactPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_ally_dodge_impact";
    public static string Description =>
        "Notify ally dodge animations at impact and keep origami missiles unhittable.";
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
            .Where(target => target.Monster is not YamotoKokiOrigamiMissile)
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
            if (player.Creature is { IsAlive: true } owner
                && owner.GetPower<EvasionPower>() is { Amount: > 0 })
            {
                CombatDodgeAnimation.NotifyImpact(owner);
            }

            Creature? yamotoKoki = player.PlayerCombatState?.Pets
                .FirstOrDefault(pet => pet.Monster is YamotoKokiMonster && pet.IsAlive);
            if (yamotoKoki != null)
            {
                CombatDodgeAnimation.NotifyImpact(yamotoKoki);
            }
        }
    }
}

public sealed class AttackIntentDamagePreviewPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_attack_intent_damage_preview";
    public static string Description =>
        "Keep one-shot evasion effects out of enemy intent damage previews.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(AttackIntent),
            nameof(AttackIntent.GetSingleDamage),
            [typeof(IEnumerable<Creature>), typeof(Creature)])
    ];

    public static void Prefix(out IDisposable __state)
    {
        __state = AttackIntentPreviewContext.Enter();
    }

    public static Exception? Finalizer(Exception? __exception, IDisposable __state)
    {
        __state.Dispose();
        return __exception;
    }
}
