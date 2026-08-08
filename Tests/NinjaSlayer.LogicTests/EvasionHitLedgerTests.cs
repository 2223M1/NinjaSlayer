using NinjaSlayer.Code.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.LogicTests;

public sealed class EvasionHitLedgerTests
{
    [Fact]
    public void SuppressesOnlyTargetsWhoseEveryHitWasEvaded()
    {
        var ledger = new EvasionHitLedger();
        var attacker = new Creature();
        var otherAttacker = new Creature();
        var onlyEvaded = new Creature();
        var eventuallyHit = new Creature();
        var unrelated = new Creature();

        ledger.RecordEvaded(attacker, onlyEvaded);
        ledger.RecordEvaded(attacker, eventuallyHit);
        ledger.RecordConnected(attacker, eventuallyHit);
        ledger.RecordEvaded(otherAttacker, eventuallyHit);

        Assert.True(ledger.WasOnlyEvaded(attacker, onlyEvaded));
        Assert.False(ledger.WasOnlyEvaded(attacker, eventuallyHit));
        Assert.True(ledger.WasOnlyEvaded(otherAttacker, eventuallyHit));
        Assert.False(ledger.WasOnlyEvaded(attacker, unrelated));
    }

    [Fact]
    public void KeysAttackersAndTargetsByReference()
    {
        var ledger = new EvasionHitLedger();
        var attacker = new EqualCreature();
        var equalAttacker = new EqualCreature();
        var target = new EqualCreature();
        var equalTarget = new EqualCreature();

        ledger.RecordEvaded(attacker, target);

        Assert.True(ledger.WasOnlyEvaded(attacker, target));
        Assert.False(ledger.WasOnlyEvaded(equalAttacker, target));
        Assert.False(ledger.WasOnlyEvaded(attacker, equalTarget));
    }

    private sealed class EqualCreature : Creature
    {
        public override bool Equals(object? obj) => obj is EqualCreature;

        public override int GetHashCode() => 0;
    }
}
