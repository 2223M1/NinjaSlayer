using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class BossDeathPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_boss_death_presentation";
    public static string Description =>
        "Add NinjaSlayer party boss death video, dismemberment, and configured part flights.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])
    ];

    public static bool Prefix(NCreature __instance, bool shouldRemove, ref float __result)
    {
        MonsterModel? monster = __instance.Entity.Monster;
        NCombatRoom? room = NCombatRoom.Instance;
        if (!shouldRemove
            || monster == null
            || room == null
            || !__instance.Entity.IsPrimaryEnemy
            || __instance.DeathAnimationTask is { IsCompleted: false }
            || monster.CombatState?.RunState.CurrentRoom is not CombatRoom
            { RoomType: RoomType.Boss } modelRoom
            || monster.CombatState.Players.All(player => player.Character is not INinjaSlayerCharacter))
        {
            return true;
        }

        BossDeathPresentationController? controller = null;
        bool ownsMusicTransition = false;
        try
        {
            BossDeathPresentationConfig.TryGetPartSpec(
                monster.Id.Entry,
                out BossDeathPartSpec? spec);
            controller = BossDeathPresentationController.Attach(__instance, room, spec);
            if (!controller.TryPrepareDeathAnimation())
            {
                controller.AbortSetup();
                controller = null;
                Entry.Logger.Warn(
                    $"Boss death presentation capture was unavailable for {monster.Id.Entry}; "
                    + "using the original death animation.");
                return true;
            }

            BossBurstParticipationRegistry.Mark(
                __instance,
                room,
                modelRoom,
                monster.CombatState.RunState);
            ownsMusicTransition = BossBurstMusicSession.Begin(room);
            __result = controller.StartDeathAnimation(shouldRemove);
            Entry.Logger.Info(
                $"Boss death presentation started: {monster.Id.Entry}, "
                + $"part={spec?.BoneName ?? "none"}.");
            return false;
        }
        catch (Exception exception)
        {
            controller?.AbortSetup();
            bool hasRemainingParticipants =
                BossBurstParticipationRegistry.Unmark(__instance, room);
            if (BossBurstPresentationPolicy.ShouldRollbackMusic(
                    ownsMusicTransition,
                    hasRemainingParticipants))
            {
                BossBurstMusicSession.Rollback(modelRoom, monster.CombatState.RunState);
            }
            Entry.Logger.Error(
                $"Boss death presentation setup failed for {monster.Id.Entry}: {exception}");
            throw;
        }
    }
}
