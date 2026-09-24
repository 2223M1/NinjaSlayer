using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Combat;

internal readonly record struct CompanionIntentGeneration(Creature Creature, long Value);

internal static class CompanionIntentLifecycle
{
    private static readonly System.Reflection.FieldInfo IntentFadeTween =
        HarmonyLib.AccessTools.Field(typeof(NCreature), "_intentFadeTween");
    private static readonly ConditionalWeakTable<Creature, GenerationState> States = new();

    public static CompanionIntentGeneration BeginCombat(Creature creature)
    {
        GenerationState state = States.GetOrCreateValue(creature);
        bool wasRetired = state.HasRetired;
        long generation = ++state.Generation;
        state.IsActive = true;
        state.HasRetired = false;

        ShowContainer(creature);
        if (wasRetired && NCombatRoom.Instance is { } room) Patches.YamotoKokiAllyLayoutPatch.Reflow(room);
        return new CompanionIntentGeneration(creature, generation);
    }

    public static CompanionIntentGeneration Capture(Creature creature)
    {
        GenerationState state = States.GetOrCreateValue(creature);
        return new CompanionIntentGeneration(creature, state.Generation);
    }

    public static bool IsCurrent(CompanionIntentGeneration generation)
    {
        if (!States.TryGetValue(generation.Creature, out GenerationState? state))
        {
            return false;
        }

        return state.IsActive && state.Generation == generation.Value;
    }

    public static bool IsActive(Creature creature)
    {
        if (!States.TryGetValue(creature, out GenerationState? state))
        {
            return false;
        }

        return state.IsActive;
    }

    internal static bool HasRetired(Creature creature) =>
        States.TryGetValue(creature, out var state) && state.HasRetired;

    internal static void Retire(Creature creature)
    {
        Invalidate(creature);
        States.GetOrCreateValue(creature).HasRetired = true;
        if (NCombatRoom.Instance is { } room) Patches.YamotoKokiAllyLayoutPatch.Reflow(room);
    }

    public static void Invalidate(Creature creature)
    {
        GenerationState state = States.GetOrCreateValue(creature);
        state.Generation++;
        state.IsActive = false;

        HideContainer(creature);
    }

    public static void InvalidateCombat(ICombatState? combatState)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (combatState == null || room == null || !GodotObject.IsInstanceValid(room))
        {
            return;
        }

        foreach (NCreature node in room.CreatureNodes)
        {
            Creature creature = node.Entity;
            if (creature.Side == CombatSide.Player
                && creature.Monster is YamotoKokiMonster or SawatariMonster or YukanoMonster
                && ReferenceEquals(creature.CombatState, combatState))
            {
                Invalidate(creature);
            }
        }
    }

    public static bool PrepareContainerForWrite(CompanionIntentGeneration generation)
    {
        if (!IsCurrent(generation))
        {
            return false;
        }

        NCreature? node = generation.Creature.GetCreatureNode();
        if (node?.IntentContainer is not { } container || !GodotObject.IsInstanceValid(container))
        {
            return false;
        }

        (IntentFadeTween.GetValue(node) as Tween)?.Kill();
        container.Visible = true;
        return true;
    }

    public static void RehideIfInactive(CompanionIntentGeneration generation)
    {
        if (!States.TryGetValue(generation.Creature, out GenerationState? state))
        {
            return;
        }

        if (state.IsActive)
        {
            return;
        }

        HideContainer(generation.Creature);
    }

    private static void ShowContainer(Creature creature)
    {
        NCreature? node = creature.GetCreatureNode();
        if (node?.IntentContainer is not { } container || !GodotObject.IsInstanceValid(container))
        {
            return;
        }

        (IntentFadeTween.GetValue(node) as Tween)?.Kill();
        container.Visible = true;
        container.Modulate = Colors.White;
    }

    private static void HideContainer(Creature creature)
    {
        NCreature? node = creature.GetCreatureNode();
        if (node?.IntentContainer is not { } container || !GodotObject.IsInstanceValid(container))
        {
            return;
        }

        (IntentFadeTween.GetValue(node) as Tween)?.Kill();
        container.Visible = false;
        container.Modulate = Colors.Transparent;
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private sealed class GenerationState
    {
        public long Generation;
        public bool IsActive;
        public bool HasRetired;
    }
}
