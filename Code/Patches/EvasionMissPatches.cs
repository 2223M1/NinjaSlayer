using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class AttackEvasionFeedbackScopePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_attack_evasion_feedback_scope";
    public static string Description => "Suppress impact feedback while an attack target is evading.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(AttackCommand), nameof(AttackCommand.Execute), [typeof(PlayerChoiceContext)])
    ];

    public static void Prefix(AttackCommand __instance, out AttackEvasionFeedbackContext.Frame? __state)
    {
        __state = AttackEvasionFeedbackContext.Enter(__instance);
    }

    public static void Postfix(
        ref Task<AttackCommand> __result,
        AttackEvasionFeedbackContext.Frame? __state)
    {
        if (__state is null)
        {
            return;
        }

        AttackEvasionFeedbackContext.RestoreCaller(__state);
        __result = AttackEvasionFeedbackContext.Complete(__result, __state);
    }

    public static Exception? Finalizer(
        Exception? __exception,
        AttackEvasionFeedbackContext.Frame? __state)
    {
        if (__exception is not null && __state is not null)
        {
            AttackEvasionFeedbackContext.RestoreCaller(__state);
            __state.IsActive = false;
        }

        return __exception;
    }
}

internal sealed class EvasionMoveScopePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_move_scope";
    public static string Description => "Track evaded and connected hits for one monster move.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(MoveState),
            nameof(MoveState.PerformMove),
            [typeof(IEnumerable<Creature>)])
    ];

    public static void Prefix(out EvasionResolution.MoveFrame __state)
    {
        __state = EvasionResolution.EnterMove();
    }

    public static void Postfix(ref Task __result, EvasionResolution.MoveFrame __state)
    {
        EvasionResolution.RestoreCaller(__state);
        __result = EvasionResolution.CompleteMove(__result, __state);
    }

    public static Exception? Finalizer(
        Exception? __exception,
        EvasionResolution.MoveFrame __state)
    {
        if (__exception is not null)
        {
            EvasionResolution.RestoreCaller(__state);
            __state.IsActive = false;
        }

        return __exception;
    }
}

internal sealed class EvasionTargetHitVfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_target_hit_vfx";
    public static string Description => "Do not spawn attack impact VFX on an evading target.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreatureCenter), [typeof(Creature), typeof(string)]),
        new(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreature), [typeof(Creature), typeof(string)])
    ];

    public static bool Prefix(Creature target, string path) =>
        !AttackEvasionFeedbackContext.ShouldSuppressTargetVfx(target, path);
}

internal sealed class EvasionSideHitVfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_side_hit_vfx";
    public static string Description => "Do not spawn side-wide impact VFX when every target evades.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(VfxCmd),
            nameof(VfxCmd.PlayOnSide),
            [typeof(CombatSide), typeof(string), typeof(ICombatState)])
    ];

    public static bool Prefix(CombatSide side, string path) =>
        !AttackEvasionFeedbackContext.ShouldSuppressSideVfx(side, path);
}

internal sealed class EvasionFmodHitSfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_fmod_hit_sfx";
    public static string Description => "Do not play FMOD impact audio when every target evades.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)])
    ];

    public static bool Prefix(string sfx) =>
        !AttackEvasionFeedbackContext.ShouldSuppressFmodHitSfx(sfx);
}

internal sealed class EvasionTemporaryHitSfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_temporary_hit_sfx";
    public static string Description => "Do not play temporary impact audio when every target evades.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NDebugAudioManager),
            nameof(NDebugAudioManager.Play),
            [typeof(string), typeof(float), typeof(PitchVariance)])
    ];

    public static bool Prefix(string streamName, ref int __result)
    {
        if (!AttackEvasionFeedbackContext.ShouldSuppressTemporaryHitSfx(streamName))
        {
            return true;
        }

        __result = -1;
        return false;
    }
}

internal sealed class EvasionCustomHitVfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_custom_hit_vfx";
    public static string Description => "Do not spawn custom attack impact VFX on an evading target.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(AttackCommand),
            nameof(AttackCommand.WithHitVfxNode),
            [typeof(Func<Creature, Node2D>)])
    ];

    public static void Prefix(
        AttackCommand __instance,
        ref Func<Creature, Node2D?> createHitVfxNode)
    {
        Func<Creature, Node2D?> original = createHitVfxNode;
        createHitVfxNode = target =>
            AttackEvasionFeedbackContext.ShouldSuppressCustomHitVfx(__instance, target)
                ? null
                : original(target);
    }
}

internal sealed class AttackEvasionDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_attack_evasion_damage";
    public static string Description => "Resolve evasion as a miss before the host runs hit feedback and hooks.";
    public static bool IsCritical => true;

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
                typeof(CardModel)
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , typeof(CardPlay)
#endif
            ])
    ];

    public static void Prefix(
        ref IEnumerable<Creature>? targets,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        out List<EvasionPower>? __state)
    {
        List<Creature> targetList = targets?
            .Where(target => target.Monster is not YamotoKokiOrigamiMissile)
            .ToList() ?? [];
        targets = targetList;
        __state = null;

        if (dealer is not { IsDead: false } attacker || !props.IsCardOrMonsterMove())
        {
            return;
        }

        var remainingTargets = new List<Creature>(targetList.Count);
        var evadedPowers = new List<EvasionPower>();
        var reservedLayers = new Dictionary<EvasionPower, int>(ReferenceEqualityComparer.Instance);
        foreach (Creature target in targetList)
        {
            if (target.IsDead || target.Side == attacker.Side)
            {
                remainingTargets.Add(target);
                continue;
            }

            EvasionPower? evasion = target.GetPower<EvasionPower>();
            int reserved = evasion is null ? 0 : reservedLayers.GetValueOrDefault(evasion);
            if (evasion is not null
                && evasion.Amount > reserved
                && evasion.CanEvade(target, props, attacker))
            {
                reservedLayers[evasion] = reserved + 1;
                evadedPowers.Add(evasion);
                EvasionResolution.RecordEvadedHit(cardSource, attacker, target);
                continue;
            }

            remainingTargets.Add(target);
            EvasionResolution.RecordConnectedHit(cardSource, attacker, target);
        }

        AttackEvasionFeedbackContext.ResolveDeferredHitSfx(
            remainingTargets.Any(target => target.IsAlive && target.Side != attacker.Side));
        targets = remainingTargets;
        __state = evadedPowers.Count > 0 ? evadedPowers : null;

        NotifyCompanionImpact(targetList, attacker);
    }

    public static void Postfix(
        ref Task<IEnumerable<DamageResult>> __result,
        List<EvasionPower>? __state)
    {
        if (__state is not null)
        {
            __result = Complete(__result, __state);
        }
    }

    private static async Task<IEnumerable<DamageResult>> Complete(
        Task<IEnumerable<DamageResult>> damageTask,
        IReadOnlyList<EvasionPower> evadedPowers)
    {
        IEnumerable<DamageResult> results = await damageTask;
        foreach (EvasionPower evasion in evadedPowers)
        {
            await evasion.ResolveDodge();
        }

        return results;
    }

    private static void NotifyCompanionImpact(IReadOnlyList<Creature> targets, Creature attacker)
    {
        if (!attacker.IsMonster)
        {
            return;
        }

        foreach (var player in targets
                     .Where(target => target.Side != attacker.Side)
                     .Select(target => target.Player ?? target.PetOwner)
                     .OfType<MegaCrit.Sts2.Core.Entities.Players.Player>()
                     .Distinct())
        {
            if (attacker.CombatState is not { } combatState)
            {
                continue;
            }

            foreach (Creature companion in combatState.Creatures.Where(creature =>
                         creature.PetOwner == player
                         && creature.IsAlive
                         && creature.Monster is YamotoKokiMonster or SawatariMonster or YukanoMonster))
            {
                CombatDodgeAnimation.NotifyImpact(companion);
            }
        }
    }
}

internal sealed class EvasionPowerApplyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_power_apply";
    public static string Description => "Suppress attack-card debuffs on targets missed by evasion.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(PowerCmd),
            nameof(PowerCmd.Apply),
            [
                typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal),
                typeof(Creature), typeof(CardModel), typeof(bool)
            ])
    ];

    public static bool Prefix(
        PowerModel power,
        Creature target,
        decimal amount,
        Creature? applier,
        CardModel? cardSource,
        ref Task __result)
    {
        if (!EvasionResolution.ShouldSuppressDebuff(cardSource, applier, target, power, amount))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }
}

internal sealed class EvasionPowerModifyAmountPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_evasion_power_modify_amount";
    public static string Description => "Suppress stacking attack-card debuffs on targets missed by evasion.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(PowerCmd),
            nameof(PowerCmd.ModifyAmount),
            [
                typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal), typeof(Creature),
                typeof(CardModel), typeof(bool)
            ])
    ];

    public static bool Prefix(
        PowerModel power,
        decimal offset,
        Creature? applier,
        CardModel? cardSource,
        ref Task<int> __result)
    {
        if (!EvasionResolution.ShouldSuppressDebuff(
                cardSource,
                applier,
                power.Owner,
                power,
                offset))
        {
            return true;
        }

        __result = Task.FromResult(power.Amount);
        return false;
    }
}
