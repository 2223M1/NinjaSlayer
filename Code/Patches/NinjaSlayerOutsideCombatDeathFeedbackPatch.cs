using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Outside combat, <see cref="CreatureCmd.Kill"/> never reaches <c>StartDeathAnim</c>
/// (no <see cref="NCombatRoom"/> node). Play Other/suicide death SFX and wait so abandon
/// (e.g. Neow/Ancient) still has audible feedback.
/// </summary>
public sealed class NinjaSlayerOutsideCombatDeathFeedbackPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_outside_combat_death_feedback";

    public static string Description =>
        "Play NinjaSlayer Other death SFX when killed outside combat (abandon).";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CreatureCmd), nameof(CreatureCmd.Kill), [typeof(Creature), typeof(bool)])
    ];

    public static void Postfix(Creature creature, bool force, ref Task __result)
    {
        __result = PlayOutsideCombatFeedbackIfNeeded(__result, creature);
    }

    private static async Task PlayOutsideCombatFeedbackIfNeeded(Task original, Creature creature)
    {
        await original;

        if (creature.Player?.Character is not INinjaSlayerCharacter || !creature.IsDead)
        {
            return;
        }

        if (NCombatRoom.Instance?.GetCreatureNode(creature) != null)
        {
            return;
        }

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerSuicideEvent);
        await Cmd.CustomScaledWait(
            Mathf.Min(DeathAnimation.OtherDeathDurationSeconds * 0.5f, 0.25f),
            DeathAnimation.OtherDeathDurationSeconds);
    }
}
