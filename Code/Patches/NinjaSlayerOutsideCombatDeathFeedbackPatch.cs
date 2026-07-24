using Godot;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Marks an abandoned NinjaSlayer before the force-kill starts. Outside combat there is no
/// <see cref="NCreature.StartDeathAnim"/> call, so this owns the suicide SFX frame.
/// </summary>
public sealed class NinjaSlayerOutsideCombatDeathCapturePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_outside_combat_death_capture";

    public static string Description =>
        "Capture abandoned NinjaSlayer deaths that have no combat creature node.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CreatureCmd), nameof(CreatureCmd.Kill), [typeof(Creature), typeof(bool)])
    ];

    public static void Prefix(Creature creature, bool force, out bool __state)
    {
        bool eligible = force
            && RunManager.Instance.IsAbandoned
            && creature.Player?.Character is INinjaSlayerCharacter
            && NCombatRoom.Instance?.GetCreatureNode(creature) == null;
        __state = eligible && NinjaSlayerOutsideCombatDeathFeedback.TryMark(creature);
        if (!__state)
        {
            return;
        }
    }

    public static void Postfix(Creature creature, bool __state, ref Task __result)
    {
        if (__state)
        {
            __result = CompleteCapture(__result, creature);
        }
    }

    private static async Task CompleteCapture(Task original, Creature creature)
    {
        try
        {
            await original;
            if (creature.IsDead)
            {
                // Vanilla stops active audio while constructing Game Over; play after that boundary.
                NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerSuicideEvent);
            }
            else
            {
                NinjaSlayerOutsideCombatDeathFeedback.Clear(creature);
            }
        }
        catch
        {
            NinjaSlayerOutsideCombatDeathFeedback.Clear(creature);
            throw;
        }
    }
}

/// <summary>
/// Vanilla/Ritsu Game Over recreates combat visuals outside combat. Apply NinjaSlayer's
/// custom Other fall only to players captured by the abandoned-run force-kill.
/// </summary>
public sealed class NinjaSlayerOutsideCombatDeathFeedbackPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_outside_combat_death_feedback";

    public static string Description =>
        "Play NinjaSlayer Other death fall on recreated Game Over visuals.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NGameOverScreen), "MoveCreaturesToDifferentLayerAndDisableUi")
    ];

    public static void Postfix(NGameOverScreen __instance)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }

        List<NCreatureVisuals> nsVisuals = __instance
            .FindChildren("*", nameof(NCreatureVisuals), recursive: true, owned: false)
            .OfType<NCreatureVisuals>()
            .Where(visuals => GodotObject.IsInstanceValid(visuals)
                && NinjaSlayerVisualRig.GetAirborneAnchor(visuals) != null
                && NinjaSlayerVisualRig.GetBodySprite(visuals) != null)
            .ToList();
        if (nsVisuals.Count == 0)
        {
            return;
        }

        List<Player> nsPlayers = runState.Players
            .Where(player => player.Character is INinjaSlayerCharacter
                && NinjaSlayerOutsideCombatDeathFeedback.IsPending(player.Creature))
            .ToList();
        if (nsPlayers.Count == 0)
        {
            return;
        }

        int count = Math.Min(nsVisuals.Count, nsPlayers.Count);
        for (int i = 0; i < count; i++)
        {
            NCreatureVisuals visuals = nsVisuals[i];
            Creature creature = nsPlayers[i].Creature;
            if (!NinjaSlayerOutsideCombatDeathFeedback.TryConsume(creature))
            {
                continue;
            }

            TaskHelper.RunSafely(
                DeathAnimation.PlayOtherDeathFallOnVisuals(
                    visuals,
                    creature,
                    playSuicideSfx: false));
        }
    }
}

internal static class NinjaSlayerOutsideCombatDeathFeedback
{
    private static readonly ConditionalWeakTable<Creature, Marker> Pending = new();

    public static bool TryMark(Creature creature)
    {
        if (Pending.TryGetValue(creature, out _))
        {
            return false;
        }

        Pending.Add(creature, new Marker());
        return true;
    }

    public static bool IsPending(Creature creature) => Pending.TryGetValue(creature, out _);

    public static bool TryConsume(Creature creature) => Pending.Remove(creature);

    public static void Clear(Creature creature) => Pending.Remove(creature);

    private sealed class Marker;
}
