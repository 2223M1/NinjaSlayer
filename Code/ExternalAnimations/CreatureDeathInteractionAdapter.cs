using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class CreatureDeathInteractionAdapter
{
    public static void Disable(NCreature creatureNode)
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
