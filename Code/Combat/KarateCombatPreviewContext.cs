using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.Combat;

public static class KarateCombatPreviewContext
{
    private static CardModel? _card;
    private static Creature? _target;

    public static Creature? Assign(CardModel? card, Creature? target)
    {
        var previousTarget = _target;
        _card = card;
        _target = target;
        return previousTarget;
    }

    public static void Set(CardModel? card, Creature? target)
    {
        RefreshAssignedTargets(Assign(card, target), target);
    }

    public static void RefreshAssignedTargets(Creature? previousTarget, Creature? currentTarget)
    {
        RefreshHealthBar(previousTarget);
        RefreshHealthBar(currentTarget);
    }

    public static CardModel? TryGetCard(Creature creature) =>
        _target == creature ? _card : null;

    public static CardModel? CurrentCard => _card;

    public static Creature? CurrentTarget => _target;

    public static void Clear()
    {
        var previousTarget = _target;
        _card = null;
        _target = null;
        RefreshHealthBar(previousTarget);
    }

    public static void Clear(CardModel? card)
    {
        if (_card == card)
        {
            Clear();
        }
    }

    public static void RefreshHealthBar(Creature? creature)
    {
        if (creature == null)
        {
            return;
        }

        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        NHealthBar? healthBar = creatureNode
            ?.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar")
            ?.GetNodeOrNull<NHealthBar>("%HealthBar");
        healthBar?.RefreshValues();
    }
}
