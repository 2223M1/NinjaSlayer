using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace NinjaSlayer.Monsters;

// Keep ally-specific hover wording while inheriting all native icon/animation behavior.
internal sealed class YamotoKokiSummonIntent : SummonIntent
{
    protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
    {
        LocString description = new("intents", "NINJA_SLAYER_YAMOTO_KOKI_SUMMON.description");
        description.Add("Count", YamotoKokiMonster.SummonMissileCount);
        return description;
    }
}

internal sealed class YamotoKokiIaiSlashIntent(Func<decimal> damageCalc) : SingleAttackIntent(damageCalc)
{
    protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
    {
        LocString description = new("intents", "NINJA_SLAYER_YAMOTO_KOKI_IAI_SLASH.description");
        description.Add("Damage", GetSingleDamage(targets, owner));
        return description;
    }
}
