using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class CreaturePresentation
    {
        public static void DisableInteractionForDeath(NCreature creatureNode)
        {
#if NINJASLAYER_LEGACY_CREATURE_PRESENTATION
            if (creatureNode.Hitbox.HasFocus())
            {
                ActiveScreenContext.Instance.FocusOnDefaultControl();
            }

            creatureNode.Hitbox.FocusMode = Control.FocusModeEnum.None;
            creatureNode.Hitbox.MouseFilter = Control.MouseFilterEnum.Ignore;
#else
            creatureNode.DisableInteractionForDeath();
#endif
        }

        public static float GetHurtAnimationTrackOffset(MonsterModel? monster)
        {
#if NINJASLAYER_LEGACY_CREATURE_PRESENTATION
            return 0.1f;
#else
            return monster?.HurtAnimationTrackOffsetForDoom ?? 0.1f;
#endif
        }
    }

    internal static class NativeHandles
    {
        public static IDisposable Lease(object? handle) =>
            handle as IDisposable ?? EmptyLease.Instance;

        public static void Dispose(object? handle) =>
            (handle as IDisposable)?.Dispose();

        private sealed class EmptyLease : IDisposable
        {
            public static readonly EmptyLease Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
