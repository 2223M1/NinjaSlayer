using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class BlackFlameDamageReceiverSetTests
{
    [Fact]
    public void RecordsPositiveReceiversOnceInFirstHitOrderByReference()
    {
        var receivers = new BlackFlameDamageReceiverSet<Target>();
        var first = new Target("same", InitialEligibility: true);
        var equalButDistinct = new Target("same", InitialEligibility: true);
        var ignored = new Target("ignored", InitialEligibility: true);

        receivers.Record(ignored, 0);
        receivers.Record(ignored, -1);
        receivers.Record(first, 4);
        receivers.Record(equalButDistinct, 2);
        receivers.Record(first, 8);

        IReadOnlyList<Target> snapshot = receivers.SnapshotWhere(static _ => true);

        Assert.Collection(
            snapshot,
            target => Assert.Same(first, target),
            target => Assert.Same(equalButDistinct, target));
    }

    [Fact]
    public void UsesTheActualReceiverAndFiltersOnlyWhenConsumed()
    {
        var receivers = new BlackFlameDamageReceiverSet<Target>();
        var originalTarget = new Target("original", InitialEligibility: true);
        var redirectedTarget = new Target("redirected", InitialEligibility: true);
        var noLongerEligible = new Target("dead-or-friendly", InitialEligibility: true);

        receivers.Record(redirectedTarget, 7);
        receivers.Record(noLongerEligible, 3);
        noLongerEligible.IsEligible = false;

        IReadOnlyList<Target> snapshot = receivers.SnapshotWhere(static target => target.IsEligible);

        Assert.Single(snapshot);
        Assert.Same(redirectedTarget, snapshot[0]);
        Assert.DoesNotContain(originalTarget, snapshot);
    }

    private sealed record Target(string Name, bool InitialEligibility)
    {
        public bool IsEligible { get; set; } = InitialEligibility;
    }
}
