using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVfx
{
    private const string BurnDamageSfx = "event:/sfx/characters/attack_fire";

    public static void PlayBurnStatusFeedback(IEnumerable<Creature> targets)
    {
        if (NCombatRoom.Instance is null)
        {
            return;
        }

        foreach (Creature target in targets)
        {
            NNinjaSlayerGroundFireVfx? vfx = NNinjaSlayerGroundFireVfx.Create(target);
            if (vfx is not null)
            {
                NCombatRoom.Instance.CombatVfxContainer.AddChildSafely(vfx);
            }
        }

        SfxCmd.Play(BurnDamageSfx);
    }
}
