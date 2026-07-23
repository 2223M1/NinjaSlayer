using NinjaSlayer.Code.Telemetry;

namespace NinjaSlayer.LogicTests;

public sealed class TelemetryIdentityTrackerTests
{
    private static readonly object ActiveRun = new();

    [Fact]
    public void OnlyTheLocalOfficialCharacterIsEligible()
    {
        var tracker = BeginTrackedRun();

        Assert.Equal(
            NinjaSlayerTelemetryIdentityStatus.Eligible,
            tracker.Refresh(ActiveRun, 10, [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));
        Assert.True(tracker.TryCaptureCompletedRun(
            new object(),
            isAbandoned: false,
            10,
            [
                Player(10, NinjaSlayerTelemetryCharacterKind.Official),
                Player(20, NinjaSlayerTelemetryCharacterKind.Other)
            ]));
    }

    [Fact]
    public void ATeammatesNinjaSlayerDoesNotMakeTheLocalPlayerEligible()
    {
        var tracker = BeginTrackedRun();

        Assert.False(tracker.TryCaptureCompletedRun(
            new object(),
            isAbandoned: false,
            10,
            [
                Player(10, NinjaSlayerTelemetryCharacterKind.Other),
                Player(20, NinjaSlayerTelemetryCharacterKind.Official)
            ]));
    }

    [Theory]
    [InlineData(NinjaSlayerTelemetryCharacterKind.Debug)]
    [InlineData(NinjaSlayerTelemetryCharacterKind.Other)]
    [InlineData(NinjaSlayerTelemetryCharacterKind.Unknown)]
    public void NonOfficialLocalCharactersAreRejected(NinjaSlayerTelemetryCharacterKind characterKind)
    {
        var tracker = BeginTrackedRun();

        Assert.False(tracker.TryCaptureCompletedRun(
            new object(),
            isAbandoned: false,
            10,
            [Player(10, characterKind)]));
    }

    [Fact]
    public void MissingOrAmbiguousLocalIdentityFailsClosed()
    {
        var missing = BeginTrackedRun();
        Assert.False(missing.TryCaptureCompletedRun(
            new object(),
            isAbandoned: false,
            null,
            [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));

        var ambiguous = BeginTrackedRun();
        Assert.False(ambiguous.TryCaptureCompletedRun(
            new object(),
            isAbandoned: false,
            10,
            [
                Player(10, NinjaSlayerTelemetryCharacterKind.Official),
                Player(10, NinjaSlayerTelemetryCharacterKind.Official)
            ]));
    }

    [Fact]
    public void AbandonedRunsAreRejectedEvenForAnEligibleLocalPlayer()
    {
        var tracker = BeginTrackedRun();

        Assert.False(tracker.TryCaptureCompletedRun(
            new object(),
            isAbandoned: true,
            10,
            [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));
    }

    [Fact]
    public void IdentityCanRefreshAfterLaunchMakesTheNetIdAvailable()
    {
        var tracker = BeginTrackedRun();
        Assert.Equal(
            NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity,
            tracker.Refresh(ActiveRun, null, [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));

        Assert.Equal(
            NinjaSlayerTelemetryIdentityStatus.Eligible,
            tracker.Refresh(ActiveRun, 10, [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));
    }

    [Fact]
    public void EndObservationAndCaptureAreOrderIndependentAndIdempotent()
    {
        object endedRun = new();
        NinjaSlayerTelemetryPlayerIdentity[] players =
            [Player(10, NinjaSlayerTelemetryCharacterKind.Official)];

        var observerFirst = BeginTrackedRun();
        observerFirst.ObserveRunEnded(endedRun, isAbandoned: false, 10, players);
        Assert.True(observerFirst.TryCaptureCompletedRun(endedRun, false, 10, players));
        Assert.False(observerFirst.TryCaptureCompletedRun(endedRun, false, 10, players));

        var filterFirst = BeginTrackedRun();
        Assert.True(filterFirst.TryCaptureCompletedRun(endedRun, false, 10, players));
        filterFirst.ObserveRunEnded(endedRun, false, 10, players);
        Assert.False(filterFirst.TryCaptureCompletedRun(endedRun, false, 10, players));
    }

    [Fact]
    public void ANewRunAndCleanupDiscardPreviousIdentity()
    {
        var tracker = BeginTrackedRun();
        tracker.Refresh(ActiveRun, 10, [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]);
        long firstGeneration = tracker.Generation;

        object nextRun = new();
        tracker.BeginRun(nextRun);
        Assert.Equal(firstGeneration + 1, tracker.Generation);
        Assert.Equal(NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity, tracker.Status);
        Assert.False(tracker.TryCaptureCompletedRun(
            new object(),
            false,
            null,
            [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));

        tracker.Clear();
        Assert.Equal(NinjaSlayerTelemetryIdentityStatus.NoActiveRun, tracker.Status);
    }

    [Fact]
    public void AForeignRunCannotRefreshTheActiveIdentity()
    {
        var tracker = BeginTrackedRun();

        Assert.Equal(
            NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity,
            tracker.Refresh(
                new object(),
                10,
                [Player(10, NinjaSlayerTelemetryCharacterKind.Official)]));
        Assert.Equal(NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity, tracker.Status);
    }

    private static NinjaSlayerTelemetryIdentityTracker BeginTrackedRun()
    {
        var tracker = new NinjaSlayerTelemetryIdentityTracker();
        tracker.BeginRun(ActiveRun);
        return tracker;
    }

    private static NinjaSlayerTelemetryPlayerIdentity Player(
        ulong netId,
        NinjaSlayerTelemetryCharacterKind characterKind) => new(netId, characterKind);
}
