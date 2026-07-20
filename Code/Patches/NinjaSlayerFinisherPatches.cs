using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerFinisherAttackCommandPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_attack_command";
    public static string Description => "Route all eligible NinjaSlayer card attack commands through the finisher system.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(AttackCommand),
            nameof(AttackCommand.Execute),
            [typeof(PlayerChoiceContext)])
    ];

    public static bool Prefix(
        AttackCommand __instance,
        PlayerChoiceContext? choiceContext,
        ref Task<AttackCommand> __result)
    {
        if (!NinjaSlayerFinisherCinematic.TryInterceptAttackCommand(__instance, choiceContext, out Task<AttackCommand>? result))
        {
            return true;
        }

        __result = result!;
        return false;
    }
}

public sealed class NinjaSlayerFinisherLethalDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_lethal_damage";
    public static string Description => "Defer guaranteed lethal damage until the finisher pose completes.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Creature), nameof(Creature.LoseHpInternal), [typeof(decimal), typeof(MegaCrit.Sts2.Core.ValueProps.ValueProp)])];

    public static void Prefix(
        Creature __instance,
        ref decimal amount,
        out int __state)
    {
        NinjaSlayerFinisherCinematic.TryProtectLethalDamage(__instance, ref amount, out __state);
    }

    public static void Postfix(DamageResult __result, int __state)
    {
        NinjaSlayerFinisherCinematic.RegisterProtectedDamageResult(__result, __state);
    }
}

public sealed class NinjaSlayerFinisherDamageNumberPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_damage_number";
    public static string Description => "Display unclamped hit values while finisher victims remain at one HP.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NDamageNumVfx),
            nameof(NDamageNumVfx.Create),
            [typeof(Creature), typeof(DamageResult)])
    ];

    public static bool Prefix(
        Creature target,
        DamageResult result,
        ref NDamageNumVfx? __result)
    {
        if (!NinjaSlayerFinisherCinematic.TryTakeDamageDisplayOverride(result, out int displayDamage))
        {
            return true;
        }

        __result = NDamageNumVfx.Create(target, displayDamage);
        return false;
    }
}

public sealed class NinjaSlayerFinisherCardVisualPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_card_visual_suppression";
    public static string Description => "Hide played and generated card visuals during enhanced finishers.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCard), nameof(NCard._EnterTree))];

    public static void Postfix(NCard __instance)
    {
        FinisherCardVisualSuppression.OnCardEnteredTree(__instance);
    }
}

public sealed class NinjaSlayerFinisherPrimaryDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_primary_damage";
    public static string Description => "Advance staged finisher camera zoom from primary attack hits.";
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

    public static bool Prefix(
        PlayerChoiceContext choiceContext,
        IEnumerable<Creature>? targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        NinjaSlayerFinisherCinematic.NotifyPrimaryDamage(dealer, cardSource, cardPlay);
        if (!NinjaSlayerFinisherCinematic.TryInterceptDirectDamage(
                choiceContext,
                targets,
                amount,
                props,
                dealer,
                cardSource,
                cardPlay,
                out Task<IEnumerable<DamageResult>>? result))
        {
            return true;
        }

        __result = result!;
        return false;
    }
}

public sealed class NinjaSlayerFinisherAfterCardPlayedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_after_card_played";
    public static string Description => "Keep deterministic post-card damage inside an active finisher session.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(Hook),
            nameof(Hook.AfterCardPlayed),
            [typeof(ICombatState), typeof(PlayerChoiceContext), typeof(CardPlay)])
    ];

    public static void Postfix(CardPlay cardPlay, ref Task __result)
    {
        __result = NinjaSlayerFinisherCinematic.WrapAfterCardPlayed(__result, cardPlay);
    }
}

public sealed class NinjaSlayerFinisherCardPlayCleanupPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_card_play_cleanup";
    public static string Description => "Clean up deferred finisher damage when card resolution exits early.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CardModel),
            nameof(CardModel.OnPlayWrapper),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool)])
    ];

    public static void Postfix(CardModel __instance, ref Task __result)
    {
        __result = NinjaSlayerFinisherCinematic.WrapCardPlay(__result, __instance);
    }
}
