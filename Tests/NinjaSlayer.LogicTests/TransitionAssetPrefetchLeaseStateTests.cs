using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionAssetPrefetchLeaseStateTests
{
    [Fact]
    public void PreparingLeaseCanBeExtendedAndClaimsOneGeneration()
    {
        var state = new TransitionAssetPrefetchLeaseState();

        long generation = state.BeginOrExtend(["run", "act"]);
        Assert.NotEqual(0, generation);
        Assert.Equal(generation, state.BeginOrExtend(["act", "room"]));

        TransitionAssetPrefetchSnapshot preparing = state.Snapshot();
        Assert.Equal(TransitionAssetPrefetchPhase.Preparing, preparing.Phase);
        Assert.Equal(3, preparing.ProtectedPathCount);
        Assert.Equal(generation, state.Claim());
        Assert.Equal(TransitionAssetPrefetchPhase.Claimed, state.Snapshot().Phase);
    }

    [Fact]
    public void UnloadFilterRetainsOnlyOwnedPaths()
    {
        var state = new TransitionAssetPrefetchLeaseState();
        state.BeginOrExtend(["run", "act", "room"]);

        string[] result = state.FilterUnprotected(
            ["menu", "run", "other", "room"],
            out int protectedCount);

        Assert.Equal(2, protectedCount);
        Assert.Equal(["menu", "other"], result);
    }

    [Fact]
    public void StaleReleaseCannotClearANewerLease()
    {
        var state = new TransitionAssetPrefetchLeaseState();
        long first = state.BeginOrExtend(["first"]);
        Assert.Equal(first, state.Claim());
        Assert.True(state.TryRelease(first));

        long second = state.BeginOrExtend(["second"]);
        Assert.NotEqual(first, second);
        Assert.Equal(second, state.Claim());

        Assert.False(state.TryRelease(first));
        Assert.Equal(TransitionAssetPrefetchPhase.Claimed, state.Snapshot().Phase);
        Assert.True(state.TryRelease(second));
        Assert.Equal(TransitionAssetPrefetchPhase.Idle, state.Snapshot().Phase);
    }

    [Fact]
    public void MainMenuResetCancelsOnlyAnUnclaimedLease()
    {
        var state = new TransitionAssetPrefetchLeaseState();
        state.BeginOrExtend(["candidate"]);
        Assert.True(state.CancelUnclaimed());
        Assert.Equal(TransitionAssetPrefetchPhase.Idle, state.Snapshot().Phase);

        long generation = state.BeginOrExtend(["saved-run"]);
        Assert.Equal(generation, state.Claim());
        Assert.False(state.CancelUnclaimed());
        Assert.Equal(TransitionAssetPrefetchPhase.Claimed, state.Snapshot().Phase);
    }
}
