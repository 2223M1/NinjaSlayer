using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.LogicTests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task FinisherCompletionUsesOneWinnerAndOneSharedResult()
    {
        var protocol = new FinisherCompletionProtocol(42);
        Assert.True(protocol.TryStart());
        Assert.True(protocol.TryAwaitPostCard());

        int completionWinners = 0;
        async Task<FinisherCompletionResult> Complete()
        {
            if (protocol.TryBeginCompletion())
            {
                Interlocked.Increment(ref completionWinners);
                Assert.True(protocol.TryTransition(FinisherSessionPhase.Committing));
                await Task.Yield();
                Assert.True(protocol.TryTransition(FinisherSessionPhase.Restoring));
                protocol.Finish(new FinisherCompletionResult(
                    protocol.SessionId,
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose,
                    null));
            }

            return await protocol.Completion;
        }

        FinisherCompletionResult[] results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Complete()));

        Assert.Equal(1, completionWinners);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(FinisherSessionPhase.Finished, protocol.Phase);
    }

    [Fact]
    public void FinisherCompletionRejectsInvalidPhaseTransitions()
    {
        var protocol = new FinisherCompletionProtocol(7);

        Assert.False(protocol.TryAwaitPostCard());
        Assert.True(protocol.TryStart());
        Assert.False(protocol.TryTransition(FinisherSessionPhase.Finished));
        Assert.True(protocol.TryTransition(FinisherSessionPhase.Restoring));
        Assert.True(protocol.TryTransition(FinisherSessionPhase.Finished));
    }

    [Fact]
    public void FinisherProtectionOnlyRollsBackAnIntactCurrentBump()
    {
        var protection = new FinisherProtectionProtocol(11, 4, 1, hpBefore: 1, temporaryHpBumpApplied: true);

        Assert.False(protection.TryReleaseAndShouldRollback(12, 4, currentHp: 2, contextIsCurrent: true));
        Assert.True(protection.IsReleased);

        var intact = new FinisherProtectionProtocol(11, 4, 2, hpBefore: 1, temporaryHpBumpApplied: true);
        Assert.True(intact.TryReleaseAndShouldRollback(11, 4, currentHp: 2, contextIsCurrent: true));
        Assert.False(intact.TryReleaseAndShouldRollback(11, 4, currentHp: 2, contextIsCurrent: true));

        var changedHp = new FinisherProtectionProtocol(11, 4, 3, hpBefore: 1, temporaryHpBumpApplied: true);
        Assert.False(changedHp.TryReleaseAndShouldRollback(11, 4, currentHp: 1, contextIsCurrent: true));
    }

    [Fact]
    public void ConfirmedFinisherProtectionCanNeverRollBackHp()
    {
        var protection = new FinisherProtectionProtocol(11, 4, 1, hpBefore: 1, temporaryHpBumpApplied: true);

        Assert.True(protection.TryConfirm());
        Assert.False(protection.TryReleaseAndShouldRollback(11, 4, currentHp: 2, contextIsCurrent: true));
        Assert.False(protection.TryConfirm());
    }

    [Fact]
    public async Task FinisherProtectionHasOneAtomicTerminalState()
    {
        var protection = new FinisherProtectionProtocol(11, 4, 1, hpBefore: 1, temporaryHpBumpApplied: true);
        int winners = 0;
        Task[] competitors = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() =>
            {
                bool won = index % 2 == 0
                    ? protection.TryConfirm()
                    : protection.TryReleaseAndShouldRollback(11, 4, currentHp: 2, contextIsCurrent: true);
                if (won)
                {
                    Interlocked.Increment(ref winners);
                }
            }))
            .ToArray();

        await Task.WhenAll(competitors);

        Assert.Equal(1, winners);
        Assert.NotEqual(protection.IsConfirmed, protection.IsReleased);
    }

    [Fact]
    public async Task FinisherCleanupRunsEveryStepBeforeReportingFailures()
    {
        var cleanup = new FinisherCleanupAccumulator();
        var completed = new List<int>();

        cleanup.Capture(() => throw new InvalidOperationException("first"));
        cleanup.Capture(() => completed.Add(1));
        await cleanup.CaptureAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("second");
        });
        await cleanup.CaptureAsync(() =>
        {
            completed.Add(2);
            return Task.CompletedTask;
        });

        AggregateException exception = Assert.Throws<AggregateException>(() => cleanup.ThrowIfAny("cleanup failed"));
        Assert.Equal([1, 2], completed);
        Assert.Equal(2, cleanup.FailureCount);
        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task XAttackAudioSuppressionSupportsNestedAsyncScopes()
    {
        Assert.False(XAttackAudioContext.SuppressAutomaticSfx);
        using (XAttackAudioContext.Suppress())
        {
            Assert.True(XAttackAudioContext.SuppressAutomaticSfx);
            using (XAttackAudioContext.Suppress())
            {
                Assert.True(XAttackAudioContext.SuppressAutomaticSfx);
            }
            Assert.True(XAttackAudioContext.SuppressAutomaticSfx);
            Assert.True(await Task.Run(() => XAttackAudioContext.SuppressAutomaticSfx));
        }
        Assert.False(XAttackAudioContext.SuppressAutomaticSfx);
    }

    [Fact]
    public void CinematicContractsRemainCalibratedAndDisposalIsIdempotent()
    {
        Assert.Equal(2f, CinematicTimingContract.BossMinimumCameraHoldSeconds);
        Assert.Equal(0.2f, CinematicTimingContract.BossCameraReturnSeconds);
        Assert.Equal(0.2f, CinematicTimingContract.FinisherReturnSeconds);
        Assert.Equal(90f, CinematicTimingContract.FinisherWatchdogSeconds);

        var lifetime = new CinematicSessionLifetime();
        CancellationToken token = lifetime.Token;
        lifetime.Cancel();
        Assert.True(token.IsCancellationRequested);
        lifetime.Dispose();
        lifetime.Dispose();
        Assert.True(lifetime.IsDisposed);
    }

    [Fact]
    public void ResolutionScopesRestoreNestedStateAndCleanUpAfterFailure()
    {
        object subject = new();
        object outerScope = new();
        object innerScope = new();
        object stateOwner = new();
        var scopes = new ResolutionScopeRegistry<object, object>();
        scopes.Begin(subject, outerScope);
        scopes.GetOrCreateState(outerScope, stateOwner, static () => new List<int>()).Add(1);
        scopes.Begin(subject, innerScope);

        Assert.True(scopes.TryGetLatestScope(subject, out object? latest));
        Assert.Same(innerScope, latest);
        scopes.Complete(innerScope);
        Assert.True(scopes.TryGetLatestScope(subject, out latest));
        Assert.Same(outerScope, latest);
        Assert.True(scopes.TryGetState(outerScope, stateOwner, out List<int>? values));
        Assert.Equal([1], values);

        scopes.CompleteSubject(subject);
        Assert.Equal(0, scopes.Count);
        Assert.False(scopes.TryGetLatestScope(subject, out _));
    }
}
