using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class BossDeathPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_boss_death_presentation";
    public static string Description => "Add NinjaSlayer party boss death audio, explosion, and configured part flights.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])
    ];

    public static void Prefix(NCreature __instance, bool shouldRemove, out BossDeathPresentationController? __state)
    {
        __state = null;
        MonsterModel? monster = __instance.Entity.Monster;
        NCombatRoom? room = NCombatRoom.Instance;
        if (!shouldRemove
            || monster == null
            || room == null
            || __instance.DeathAnimationTask is { IsCompleted: false }
            || monster.CombatState?.RunState.CurrentRoom is not CombatRoom { RoomType: RoomType.Boss }
            || monster.CombatState.Players.All(player => player.Character is not INinjaSlayerCharacter))
        {
            return;
        }

        BossDeathPresentationConfig.TryGetPartSpec(monster.Id.Entry, out BossDeathPartSpec? spec);
        __state = BossDeathPresentationController.Attach(__instance, room, spec);
    }

    public static void Postfix(NCreature __instance, BossDeathPresentationController? __state)
    {
        if (__state == null)
        {
            return;
        }

        float disappearDelay = __instance.HasSpineAnimation
            ? Math.Min(__instance.GetCurrentAnimationTimeRemaining() + 0.5f, 20f)
            : __instance.Entity.Monster is { HasDeathAnimLengthOverride: true } monster
                ? monster.DeathAnimLengthOverride
                : 0f;
        __state.Begin(disappearDelay);
        Entry.Logger.Info(
            $"Boss death presentation started: {__instance.Entity.Monster?.Id.Entry}, "
            + $"part={(BossDeathPresentationConfig.TryGetPartSpec(__instance.Entity.Monster!.Id.Entry, out BossDeathPartSpec? spec) ? spec.BoneName : "none")}.");
    }
}

internal sealed class BossDeathFadeStartPatch : IPatchMethod
{
    private static readonly FieldInfo? CreatureNodesField = typeof(NMonsterDeathVfx)
        .GetField("_creatureNodes", BindingFlags.Instance | BindingFlags.NonPublic);

    public static string PatchId => "ninjaslayer_boss_death_fade_start";
    public static string Description => "Synchronize NinjaSlayer boss explosions with the original fade start.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NMonsterDeathVfx), nameof(NMonsterDeathVfx.PlayVfx))
    ];

    public static void Prefix(NMonsterDeathVfx __instance)
    {
        if (CreatureNodesField?.GetValue(__instance) is not IEnumerable<NCreature> creatures)
        {
            return;
        }

        foreach (NCreature creature in creatures)
        {
            BossDeathPresentationController.NotifyDisappearanceStarted(creature);
        }
    }
}
