using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossDeathExplosionVfx
{
    public const string TemporaryExplosionSfx =
        "event:/sfx/enemy/enemy_attacks/waterfall_giant/waterfall_giant_eruption";
    private const string KaiserCrabExplosionPath = "vfx/monsters/kaiser_crab_boss_explosion";
    private const float FireBurstScale = 1.4f;
    private static readonly Color FireBurstTint = new("ff6640");

    public static IEnumerable<string> AssetPaths =>
    [
        SceneHelper.GetScenePath(KaiserCrabExplosionPath),
        NFireBurstVfx.scenePath
    ];

    public static void Play(NCombatRoom room, Vector2 globalCenter)
    {
        if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
        {
            return;
        }

        try
        {
            VfxCmd.PlayVfx(globalCenter, KaiserCrabExplosionPath, room.CombatVfxContainer);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Failed to play temporary Kaiser Crab boss explosion: {exception}");
        }

        try
        {
            NFireBurstVfx? fireBurst = NFireBurstVfx.Create(globalCenter, FireBurstScale, FireBurstTint);
            if (fireBurst != null)
            {
                room.CombatVfxContainer.AddChildSafely(fireBurst);
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Failed to play temporary boss fire burst: {exception}");
        }
    }
}
