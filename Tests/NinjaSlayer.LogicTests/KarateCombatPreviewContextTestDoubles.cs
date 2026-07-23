namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature;
}

namespace MegaCrit.Sts2.Core.Models
{
    public class CardModel;
}

namespace MegaCrit.Sts2.Core.Nodes.Combat
{
    public class NCreature
    {
        public T? GetNodeOrNull<T>(string path)
            where T : class =>
            null;
    }

    public class NCreatureStateDisplay
    {
        public T? GetNodeOrNull<T>(string path)
            where T : class =>
            null;
    }

    public class NHealthBar
    {
        public void RefreshValues()
        {
        }
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Rooms
{
    public class NCombatRoom
    {
        public static NCombatRoom? Instance { get; set; }

        public MegaCrit.Sts2.Core.Nodes.Combat.NCreature? GetCreatureNode(
            MegaCrit.Sts2.Core.Entities.Creatures.Creature creature) =>
            null;
    }
}
