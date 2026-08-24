namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardTag
    {
        Shiv,
        Shuriken
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature;
}

namespace MegaCrit.Sts2.Core.Models
{
    using MegaCrit.Sts2.Core.Entities.Cards;

    public enum CardType
    {
        Skill,
        Attack
    }

    public class CardModel
    {
        public CardType Type { get; set; }
        public HashSet<CardTag> Tags { get; } = [];
    }
}

namespace NinjaSlayer.Content
{
    using MegaCrit.Sts2.Core.Entities.Cards;

    public static class NinjaSlayerCardTags
    {
        public static readonly CardTag Shuriken = CardTag.Shuriken;
    }
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
