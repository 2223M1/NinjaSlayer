using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal static class FriendlyCompanionTargeting
{
    private static AbstractModel? _selectedModel;

    public static void Select(AbstractModel? model) => _selectedModel = model;

    public static bool IsFriendlyCompanion(Creature creature) =>
        creature.Side == CombatSide.Player
        && creature.PetOwner != null
        && creature.IsAlive
        && creature.Monster is YamotoKokiMonster or SawatariMonster or YukanoMonster;

    public static bool Supports(CardModel card) =>
        card.TargetType == TargetType.AnyAlly
        && card.GetType().Name is
            "Blaze" or
            "Concoct" or
            "Coordinate" or
            "DemonicShield" or
            "Fade" or
            "Intercept" or
            "Lift" or
            "Mimic";

    public static bool Supports(PotionModel potion) =>
        potion.TargetType == TargetType.AnyPlayer
        && potion.GetType().Name is
            "BlockPotion" or
            "DexterityPotion" or
            "FlexPotion" or
            "FyshOil" or
            "HeartOfIron" or
            "LiquidBronze" or
            "LuckyTonic" or
            "MazalethsGift" or
            "RegenPotion" or
            "ShipInABottle" or
            "SpeedPotion" or
            "StrengthPotion";

    public static bool HasCompanion(ICombatState? combatState) =>
        combatState?.Creatures.Any(IsFriendlyCompanion) == true;

    public static bool SelectionAllows(Creature creature)
    {
        if (!IsFriendlyCompanion(creature))
        {
            return false;
        }

        return _selectedModel switch
        {
            CardModel card => Supports(card)
                && ReferenceEquals(ResolveCombatState(card), creature.CombatState),
            PotionModel potion => Supports(potion)
                && ReferenceEquals(potion.Owner.Creature.CombatState, creature.CombatState),
            _ => false
        };
    }

    public static IReadOnlyList<Creature> ResolveControllerAllies(ICombatState combatState)
    {
        if (_selectedModel is not CardModel card
            || !Supports(card)
            || !ReferenceEquals(ResolveCombatState(card), combatState))
        {
            return combatState.PlayerCreatures;
        }

        return combatState.PlayerCreatures
            .Concat(combatState.Creatures.Where(IsFriendlyCompanion))
            .ToArray();
    }

    public static ICombatState? ResolveCombatState(CardModel card) =>
        card.CombatState ?? card.Owner.Creature.CombatState;
}

public sealed class FriendlyCompanionInteractionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_interaction";
    public static string Description => "Keep full-size companions targetable without showing their health bars.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature), [typeof(Creature)]),
        new(
            typeof(NCombatRoom),
            nameof(NCombatRoom.TransitionToActiveCombat),
            [typeof(CombatRoom)])
    ];

    public static void Postfix(NCombatRoom __instance)
    {
        if (__instance.Mode != CombatRoomMode.ActiveCombat)
        {
            return;
        }

        foreach (NCreature node in __instance.CreatureNodes.Where(node =>
                     FriendlyCompanionTargeting.IsFriendlyCompanion(node.Entity)))
        {
            __instance.SetCreatureIsInteractable(node.Entity, on: true);
            NCreatureStateDisplay stateDisplay =
                node.GetNode<NCreatureStateDisplay>("%HealthBar");
            stateDisplay.GetNode<NHealthBar>("%HealthBar").Hide();
            stateDisplay.GetNode<Control>("%HpBarHitbox").MouseFilter =
                Control.MouseFilterEnum.Ignore;
        }
    }
}

public sealed class FriendlyCompanionCardSelectedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_card_selected";
    public static string Description => "Track the local card while companion targeting is active.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(HoveredModelTracker),
            nameof(HoveredModelTracker.OnLocalCardSelected),
            [typeof(CardModel)])
    ];

    public static void Postfix(CardModel cardModel) =>
        FriendlyCompanionTargeting.Select(cardModel);
}

public sealed class FriendlyCompanionCardDeselectedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_card_deselected";
    public static string Description => "Clear the local companion card-targeting context.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(HoveredModelTracker),
            nameof(HoveredModelTracker.OnLocalCardDeselected),
            Type.EmptyTypes)
    ];

    public static void Postfix() => FriendlyCompanionTargeting.Select(null);
}

public sealed class FriendlyCompanionPotionSelectedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_potion_selected";
    public static string Description => "Track the local potion while companion targeting is active.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(HoveredModelTracker),
            nameof(HoveredModelTracker.OnLocalPotionSelected),
            [typeof(PotionModel)])
    ];

    public static void Postfix(PotionModel potionModel) =>
        FriendlyCompanionTargeting.Select(potionModel);
}

public sealed class FriendlyCompanionPotionDeselectedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_potion_deselected";
    public static string Description => "Clear the local companion potion-targeting context.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(HoveredModelTracker),
            nameof(HoveredModelTracker.OnLocalPotionDeselected),
            Type.EmptyTypes)
    ];

    public static void Postfix() => FriendlyCompanionTargeting.Select(null);
}

public sealed class FriendlyCompanionTargetManagerPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_target_manager";
    public static string Description => "Allow safe cards and potions to select full-size companions.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NTargetManager), "AllowedToTargetCreature", [typeof(Creature)])
    ];

    public static void Postfix(Creature creature, ref bool __result)
    {
        if (!__result && FriendlyCompanionTargeting.SelectionAllows(creature))
        {
            __result = true;
        }
    }
}

public sealed class FriendlyCompanionCardCanPlayPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_card_can_play";
    public static string Description => "Count full-size companions as living allies for safe cards.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CardModel),
            nameof(CardModel.CanPlay),
            [
                typeof(UnplayableReason).MakeByRefType(),
                typeof(AbstractModel).MakeByRefType()
            ])
    ];

    public static void Postfix(
        CardModel __instance,
        ref bool __result,
        ref UnplayableReason reason)
    {
        if (!reason.HasFlag(UnplayableReason.NoLivingAllies)
            || !FriendlyCompanionTargeting.Supports(__instance)
            || !FriendlyCompanionTargeting.HasCompanion(
                FriendlyCompanionTargeting.ResolveCombatState(__instance)))
        {
            return;
        }

        reason &= ~UnplayableReason.NoLivingAllies;
        __result = reason == UnplayableReason.None;
    }
}

public sealed class FriendlyCompanionCardAutoPlayPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_card_autoplay";
    public static string Description => "Include companions when safe ally cards choose an automatic target.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CardCmd),
            nameof(CardCmd.AutoPlay),
            [
                typeof(PlayerChoiceContext),
                typeof(CardModel),
                typeof(Creature),
                typeof(AutoPlayType),
                typeof(bool),
                typeof(bool)
            ])
    ];

    public static void Prefix(CardModel card, ref Creature? target)
    {
        if (target != null || !FriendlyCompanionTargeting.Supports(card))
        {
            return;
        }

        ICombatState? combatState = FriendlyCompanionTargeting.ResolveCombatState(card);
        if (combatState == null)
        {
            return;
        }

        Creature owner = card.Owner.Creature;
        Creature[] candidates = combatState.GetTeammatesOf(owner)
            .Where(creature => creature.IsAlive
                && creature != owner
                && (creature.IsPlayer
                    || FriendlyCompanionTargeting.IsFriendlyCompanion(creature)))
            .ToArray();
        if (candidates.Length > 0)
        {
            target = card.Owner.RunState.Rng.CombatTargets.NextItem(candidates);
        }
    }
}

public sealed class FriendlyCompanionControllerCardTargetPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_controller_card_target";
    public static string Description => "Include companions in controller navigation for safe ally cards.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets()
    {
        MethodInfo method = AccessTools.Method(
                typeof(NControllerCardPlay),
                "SingleCreatureTargeting",
                [typeof(TargetType)])
            ?? throw new MissingMethodException(
                typeof(NControllerCardPlay).FullName,
                "SingleCreatureTargeting");
        Type stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new MissingMethodException(
                typeof(NControllerCardPlay).FullName,
                "SingleCreatureTargeting state machine");
        return [new(stateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes)];
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo playerCreatures = AccessTools.PropertyGetter(
                typeof(ICombatState),
                nameof(ICombatState.PlayerCreatures))
            ?? throw new MissingMethodException(
                typeof(ICombatState).FullName,
                nameof(ICombatState.PlayerCreatures));
        MethodInfo replacement = AccessTools.Method(
                typeof(FriendlyCompanionTargeting),
                nameof(FriendlyCompanionTargeting.ResolveControllerAllies))
            ?? throw new MissingMethodException(
                typeof(FriendlyCompanionTargeting).FullName,
                nameof(FriendlyCompanionTargeting.ResolveControllerAllies));
        List<CodeInstruction> rewritten = instructions.ToList();
        int replacementCount = 0;
        foreach (CodeInstruction instruction in rewritten)
        {
            if (!Equals(instruction.operand, playerCreatures))
            {
                continue;
            }

            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
            replacementCount++;
        }

        if (replacementCount != 1)
        {
            throw new InvalidOperationException(
                $"Expected one controller ally-list read, found {replacementCount}.");
        }

        return rewritten;
    }
}

public sealed class FriendlyCompanionPotionThrowPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_potion_throw";
    public static string Description => "Enter ally targeting when a safe potion can target a companion.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(PotionModel), nameof(PotionModel.CanThrowAtAlly), Type.EmptyTypes)
    ];

    public static void Postfix(PotionModel __instance, ref bool __result)
    {
        if (!__result
            && CombatManager.Instance.IsInProgress
            && FriendlyCompanionTargeting.Supports(__instance)
            && FriendlyCompanionTargeting.HasCompanion(
                __instance.Owner.Creature.CombatState))
        {
            __result = true;
        }
    }
}

public sealed class FriendlyCompanionPotionTargetPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_potion_target";
    public static string Description => "Validate full-size companions for safe player-targeted potions.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(PotionModel), nameof(PotionModel.IsValidTarget), [typeof(Creature)])
    ];

    public static void Postfix(
        PotionModel __instance,
        Creature? target,
        ref bool __result)
    {
        if (!__result
            && target != null
            && FriendlyCompanionTargeting.Supports(__instance)
            && FriendlyCompanionTargeting.IsFriendlyCompanion(target)
            && ReferenceEquals(__instance.Owner.Creature.CombatState, target.CombatState))
        {
            __result = true;
        }
    }
}
