using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerDeathAnimPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_death_animation";

    public static string Description => "Choose NinjaSlayer death feedback from the fatal damage source.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])
    ];

    public static bool Prefix(
        NCreature __instance,
        bool shouldRemove,
        ref float __result,
        out bool __state)
    {
        if (__instance.Entity.Player?.Character is INinjaSlayerCharacter)
        {
            NinjaSlayerRapidAnimationCoordinator.CancelAndRestore(__instance.Entity);
        }

        if (ArchitectVictoryCleanup.TryConsume(__instance.Entity))
        {
            __state = false;
            return true;
        }

        if (NinjaSlayerAbandonDeathFeedback.IsPending(__instance.Entity))
        {
            __state = false;
            return true;
        }

        if (__instance.Entity.Monster is DarkNinjaMonster or SawatariMonster)
        {
            __state = false;
            if (__instance.DeathAnimationTask is { IsCompleted: false })
            {
                __result = 0f;
                return false;
            }

            Task deathTask = PlayMonsterDeathFlight(__instance, shouldRemove);
            __instance.DeathAnimationTask = deathTask;
            TaskHelper.RunSafely(deathTask);
            __result = DeathAnimation.EnemyKillDurationSeconds;
            return false;
        }

        __state = IsNinjaSlayerNonSpine(__instance)
            && (__instance.DeathAnimationTask == null || __instance.DeathAnimationTask.IsCompleted);
        return true;
    }

    public static void Postfix(NCreature __instance, ref float __result, bool __state)
    {
        if (!__state)
        {
            return;
        }

        if (FinisherDeathContinuationRegistry.TryConsumeReverseFlight(__instance.Entity))
        {
            Task reverseFlightTask = DeathAnimation.PlayEnemyFinisherFlightOnly(__instance.Entity);
            __instance.DeathAnimationTask = reverseFlightTask;
            TaskHelper.RunSafely(reverseFlightTask);
            __result = DeathAnimation.EnemyKillDurationSeconds;
            return;
        }

        NinjaSlayerDeathContext context = DeathAnimation.CreateContext(__instance.Entity);
        if (context.Kind != NinjaSlayerDeathKind.EnemyKill)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerSuicideEvent);
        }

        Task deathTask = DeathAnimation.Play(__instance.Entity, context);
        __instance.DeathAnimationTask = deathTask;
        TaskHelper.RunSafely(deathTask);
        __result = DeathAnimation.GetDuration(context.Kind);
    }

    private static async Task PlayMonsterDeathFlight(
        NCreature creatureNode,
        bool shouldRemove)
    {
        GameCompatibility.CreaturePresentation.DisableInteractionForDeath(creatureNode);
        foreach (NIntent intent in creatureNode.IntentContainer.GetChildren().OfType<NIntent>())
        {
            intent.SetFrozen(isFrozen: true);
        }

        if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
        {
            creatureNode.OrbManager?.ClearOrbs();
        }

        if (shouldRemove)
        {
            creatureNode.AnimHideIntent();
        }

        creatureNode.AnimDisableUi();
        SfxCmd.PlayDeath(creatureNode.Entity.Monster);
        try
        {
            await DeathAnimation.PlayEnemyFinisherFlightOnly(
                creatureNode.Entity,
                flyRight: true);
        }
        finally
        {
            if (shouldRemove && Godot.GodotObject.IsInstanceValid(creatureNode))
            {
                creatureNode.QueueFreeSafely();
            }
        }
    }

    private static bool IsNinjaSlayerNonSpine(NCreature creatureNode)
    {
        return creatureNode.Entity.Player?.Character is INinjaSlayerCharacter
            && !creatureNode.HasSpineAnimation;
    }
}
