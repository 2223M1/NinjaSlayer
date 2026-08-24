using Godot;
using MegaCrit.Sts2.Core.Helpers;
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
    }
}
