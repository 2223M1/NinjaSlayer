using Godot;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
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
            && !CombatManager.Instance.IsInProgress;
        __state = eligible && NinjaSlayerOutsideCombatDeathFeedback.TryMark(creature);
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
/// Associates combat-style visuals recreated by Game Over with the creature that created them.
/// </summary>
public sealed class NinjaSlayerOutsideCombatVisualCreationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_outside_combat_visual_creation";

    public static string Description =>
        "Bind recreated Game Over visuals to an abandoned NinjaSlayer creature.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(Creature), nameof(Creature.CreateVisuals), [])
    ];

    public static void Postfix(Creature __instance, NCreatureVisuals? __result)
    {
        if (__result != null)
        {
            NinjaSlayerOutsideCombatDeathFeedback.RegisterVisual(__result, __instance);
        }
    }
}

/// <summary>
/// Merchant and fake-merchant Game Over paths retain <see cref="NMerchantCharacter"/> roots rather than
/// recreating <see cref="NCreatureVisuals"/>. Play the same fall directly on NinjaSlayer's procedural sprite.
/// </summary>
public sealed class NinjaSlayerOutsideCombatMerchantDeathPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_outside_combat_merchant_death";

    public static string Description =>
        "Play abandoned NinjaSlayer death feedback on merchant-style world visuals.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation), [typeof(string), typeof(bool)])
    ];

    public static void Postfix(NMerchantCharacter __instance, string anim)
    {
        if (!NinjaSlayerOutsideCombatDeathFeedback.IsDeathCue(anim)
            || !NinjaSlayerOutsideCombatDeathFeedback.IsNinjaSlayerMerchantVisual(__instance)
            || !NinjaSlayerOutsideCombatDeathFeedback.TryConsumeAny(out _))
        {
            return;
        }

        TaskHelper.RunSafely(
            DeathAnimation.PlayOtherDeathFallOnWorldVisual(
                __instance,
                playSuicideSfx: false));
        Entry.Logger.Info("NinjaSlayer outside-combat death feedback started: route=merchant-world-visual.");
    }
}

/// <summary>
/// Vanilla/Ritsu Game Over recreates combat visuals outside combat. Apply NinjaSlayer's custom Other fall only
/// to visuals explicitly associated with the abandoned creature; the rig fallback covers compatibility patches
/// that instantiate the character model directly rather than calling <see cref="Creature.CreateVisuals"/>.
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
        int started = 0;
        foreach (NCreatureVisuals visuals in NinjaSlayerOutsideCombatDeathFeedback
                     .EnumerateDescendants<NCreatureVisuals>(__instance))
        {
            if (!GodotObject.IsInstanceValid(visuals)
                || NinjaSlayerVisualRig.GetAirborneAnchor(visuals) == null
                || NinjaSlayerVisualRig.GetBodySprite(visuals) == null)
            {
                continue;
            }

            if (!NinjaSlayerOutsideCombatDeathFeedback.TryConsumeVisual(visuals, out Creature? creature)
                && !NinjaSlayerOutsideCombatDeathFeedback.TryConsumeAny(out creature))
            {
                continue;
            }

            TaskHelper.RunSafely(
                DeathAnimation.PlayOtherDeathFallOnVisuals(
                    visuals,
                    creature,
                    playSuicideSfx: false));
            started++;
        }

        int unmatched = NinjaSlayerOutsideCombatDeathFeedback.ClearRemaining();
        if (started > 0)
        {
            Entry.Logger.Info(
                $"NinjaSlayer outside-combat death feedback started: route=recreated-combat-visual, count={started}.");
        }

        if (unmatched > 0)
        {
            Entry.Logger.Warn(
                $"NinjaSlayer outside-combat death feedback could not match {unmatched} abandoned creature(s) to Game Over visuals.");
        }
    }
}

internal static class NinjaSlayerOutsideCombatDeathFeedback
{
    private static readonly object Sync = new();
    private static readonly HashSet<Creature> Pending = new(ReferenceEqualityComparer.Instance);
    private static readonly ConditionalWeakTable<NCreatureVisuals, CreatureMarker> VisualOwners = new();

    public static bool TryMark(Creature creature)
    {
        lock (Sync)
        {
            return Pending.Add(creature);
        }
    }

    public static bool IsPending(Creature creature)
    {
        lock (Sync)
        {
            return Pending.Contains(creature);
        }
    }

    public static void RegisterVisual(NCreatureVisuals visuals, Creature creature)
    {
        if (!IsPending(creature))
        {
            return;
        }

        VisualOwners.Remove(visuals);
        VisualOwners.Add(visuals, new CreatureMarker(creature));
    }

    public static bool TryConsumeVisual(NCreatureVisuals visuals, out Creature? creature)
    {
        creature = null;
        if (!VisualOwners.TryGetValue(visuals, out CreatureMarker? marker))
        {
            return false;
        }

        VisualOwners.Remove(visuals);
        creature = marker.Creature;
        return TryConsume(creature);
    }

    public static bool TryConsumeAny(out Creature? creature)
    {
        lock (Sync)
        {
            creature = Pending.FirstOrDefault();
            return creature != null && Pending.Remove(creature);
        }
    }

    public static int ClearRemaining()
    {
        lock (Sync)
        {
            int count = Pending.Count;
            Pending.Clear();
            return count;
        }
    }

    public static void Clear(Creature creature)
    {
        lock (Sync)
        {
            Pending.Remove(creature);
        }
    }

    public static bool IsDeathCue(string cue) =>
        cue.Equals("die", StringComparison.OrdinalIgnoreCase)
        || cue.Equals("death", StringComparison.OrdinalIgnoreCase)
        || cue.Equals("dead", StringComparison.OrdinalIgnoreCase);

    public static bool IsNinjaSlayerMerchantVisual(NMerchantCharacter visual)
    {
        Sprite2D? body = EnumerateDescendants<Sprite2D>(visual)
            .FirstOrDefault(sprite => sprite.Name == "Visuals");
        return body?.Texture?.ResourcePath.Equals(
            NinjaSlayerWorldVisualProfile.Merchant.IdleTexturePath,
            StringComparison.Ordinal) == true;
    }

    public static IEnumerable<T> EnumerateDescendants<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in EnumerateDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool TryConsume(Creature creature)
    {
        lock (Sync)
        {
            return Pending.Remove(creature);
        }
    }

    private sealed record CreatureMarker(Creature Creature);
}
