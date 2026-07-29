using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Combat;

internal readonly record struct YamotoKokiIntentGeneration(Creature Creature, long Value);

internal static class YamotoKokiIntentLifecycle
{
    private static readonly ConditionalWeakTable<Creature, GenerationState> States = new();

    public static YamotoKokiIntentGeneration BeginCombat(Creature creature)
    {
        GenerationState state = States.GetOrCreateValue(creature);
        long generation;
        lock (state)
        {
            generation = ++state.Generation;
            state.IsActive = true;
        }

        ShowContainer(creature);
        return new YamotoKokiIntentGeneration(creature, generation);
    }

    public static YamotoKokiIntentGeneration Capture(Creature creature)
    {
        GenerationState state = States.GetOrCreateValue(creature);
        lock (state)
        {
            return new YamotoKokiIntentGeneration(creature, state.Generation);
        }
    }

    public static bool IsCurrent(YamotoKokiIntentGeneration generation)
    {
        if (!States.TryGetValue(generation.Creature, out GenerationState? state))
        {
            return false;
        }

        lock (state)
        {
            return state.IsActive && state.Generation == generation.Value;
        }
    }

    public static bool IsActive(Creature creature)
    {
        if (!States.TryGetValue(creature, out GenerationState? state))
        {
            return false;
        }

        lock (state)
        {
            return state.IsActive;
        }
    }

    public static void Invalidate(Creature creature)
    {
        GenerationState state = States.GetOrCreateValue(creature);
        lock (state)
        {
            state.Generation++;
            state.IsActive = false;
        }

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
            if (creature.Monster is YamotoKokiMonster
                && ReferenceEquals(creature.CombatState, combatState))
            {
                Invalidate(creature);
            }
        }
    }

    public static bool PrepareContainerForWrite(YamotoKokiIntentGeneration generation)
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

        container.Visible = true;
        return true;
    }

    public static void RehideIfInactive(YamotoKokiIntentGeneration generation)
    {
        if (!States.TryGetValue(generation.Creature, out GenerationState? state))
        {
            return;
        }

        lock (state)
        {
            if (state.IsActive)
            {
                return;
            }
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
    }
}
