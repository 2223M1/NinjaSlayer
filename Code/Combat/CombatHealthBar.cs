using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.Combat;

internal static class CombatHealthBar
{
    public static void Refresh(Creature? creature)
    {
        NHealthBar? healthBar = creature == null
            ? null
            : NCombatRoom.Instance
                ?.GetCreatureNode(creature)
                ?.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar")
                ?.GetNodeOrNull<NHealthBar>("%HealthBar");
        healthBar?.RefreshValues();
    }
}
