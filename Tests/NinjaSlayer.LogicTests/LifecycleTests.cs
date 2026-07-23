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
    public void XAttackComboScopesRestoreNestedState()
    {
        Assert.False(XAttackComboContext.Active);
        using (XAttackComboContext.Enter(3))
        {
            XAttackComboContext.CurrentHitIndex = 2;
            using (XAttackComboContext.Enter(5))
            {
                Assert.True(XAttackComboContext.Active);
                Assert.Equal(0, XAttackComboContext.CurrentHitIndex);
                Assert.Equal(5, XAttackComboContext.TotalHits);
                XAttackComboContext.CurrentHitIndex = 4;
            }

            Assert.True(XAttackComboContext.Active);
            Assert.Equal(2, XAttackComboContext.CurrentHitIndex);
            Assert.Equal(3, XAttackComboContext.TotalHits);
        }

        Assert.False(XAttackComboContext.Active);
        Assert.Equal(0, XAttackComboContext.CurrentHitIndex);
        Assert.Equal(0, XAttackComboContext.TotalHits);
    }

    [Fact]
    public void XAttackScopesTolerateOutOfOrderDisposal()
    {
        IDisposable audioOuter = XAttackAudioContext.Suppress();
        IDisposable audioInner = XAttackAudioContext.Suppress();
        audioOuter.Dispose();
        Assert.True(XAttackAudioContext.SuppressAutomaticSfx);
        audioInner.Dispose();
        Assert.False(XAttackAudioContext.SuppressAutomaticSfx);

        IDisposable comboOuter = XAttackComboContext.Enter(3);
        XAttackComboContext.CurrentHitIndex = 1;
        IDisposable comboInner = XAttackComboContext.Enter(5);
        XAttackComboContext.CurrentHitIndex = 4;
        comboOuter.Dispose();
        Assert.True(XAttackComboContext.Active);
        Assert.Equal(4, XAttackComboContext.CurrentHitIndex);
        Assert.Equal(5, XAttackComboContext.TotalHits);
        comboInner.Dispose();
        Assert.False(XAttackComboContext.Active);
    }

    [Fact]
    public async Task XAttackComboHitStateIsIsolatedAcrossExecutionContexts()
    {
        using (XAttackComboContext.Enter(3))
        {
            XAttackComboContext.CurrentHitIndex = 2;
            await Task.Run(() =>
            {
                Assert.Equal(2, XAttackComboContext.CurrentHitIndex);
                XAttackComboContext.CurrentHitIndex = 1;
                Assert.Equal(1, XAttackComboContext.CurrentHitIndex);
            });

            Assert.Equal(2, XAttackComboContext.CurrentHitIndex);
        }

        Assert.False(XAttackComboContext.Active);
    }

    [Fact]
    public void CinematicContractsRemainCalibratedAndDisposalIsIdempotent()
    {
        Assert.Equal(2f, CinematicTimingContract.BossMinimumCameraHoldSeconds);
        Assert.Equal(0.2f, CinematicTimingContract.BossCameraReturnSeconds);
        Assert.Equal(0.1f, CinematicTimingContract.FinisherDeathKickSettleSeconds);
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
    public void FinisherDeathKickRecoveryUsesTheSharedCubicTimeline()
    {
        Assert.Equal(0f, FinisherDeathKickTimeline.GetRecoveryProgress(0f, 0f));
        Assert.Equal(0.875f, FinisherDeathKickTimeline.GetRecoveryProgress(0.5f, 0f));
        Assert.Equal(1f, FinisherDeathKickTimeline.GetRecoveryProgress(1f, 0f));

        Assert.Equal(0f, FinisherDeathKickTimeline.GetRecoveryProgress(0.5f, 0.5f));
        Assert.Equal(0.875f, FinisherDeathKickTimeline.GetRecoveryProgress(0.75f, 0.5f));
        Assert.Equal(1f, FinisherDeathKickTimeline.GetRecoveryProgress(1f, 0.5f));
        Assert.Equal(1f, FinisherDeathKickTimeline.GetRecoveryProgress(1f, 1f));
    }

    [Fact]
    public void ResolutionScopesRestoreNestedStateAndCleanUpAfterFailure()
    {
        object subject = new();
        object outerScope = new();
        object innerScope = new();
        object stateOwner = new();
        var scopes = new ResolutionScopeRegistry<object, object>();
        Assert.True(scopes.Begin(subject, outerScope));
        Assert.True(scopes.TryGetOrCreateState(
            outerScope,
            stateOwner,
            static () => new List<int>(),
            out List<int>? created));
        created!.Add(1);
        Assert.True(scopes.Begin(subject, innerScope));

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

    [Fact]
    public void ResolutionScopesKeyStateByOwnerReferenceAndStateType()
    {
        object subject = new();
        object scope = new();
        object owner = new();
        var scopes = new ResolutionScopeRegistry<object, object>();
        Assert.True(scopes.Begin(subject, scope));

        Assert.True(scopes.TryGetOrCreateState(scope, owner, static () => new List<int>(), out List<int>? list));
        Assert.True(scopes.TryGetOrCreateState(scope, owner, static () => new HashSet<int>(), out HashSet<int>? set));
        Assert.True(scopes.TryGetOrCreateState(scope, owner, static () => new List<int>(), out List<int>? sameList));

        Assert.NotNull(list);
        Assert.NotNull(set);
        Assert.Same(list, sameList);
    }

    [Fact]
    public async Task ResolutionScopesRejectForeignThreadMutationAndReportOnceInReleaseMode()
    {
        var reports = new List<string>();
        var scopes = new ResolutionScopeRegistry<object, object>(reports.Add, throwOnThreadViolation: false);
        object subject = new();

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            scopes.Begin(subject, new object()))));

        Assert.All(results, Assert.False);
        Assert.Single(reports);
        Assert.Equal(0, scopes.Count);
    }

    [Fact]
    public async Task ResolutionScopesAssertForeignThreadMutationInStrictMode()
    {
        var scopes = new ResolutionScopeRegistry<object, object>(throwOnThreadViolation: true);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => scopes.Begin(new object(), new object())));

        Assert.Contains("owner thread", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, scopes.Count);
    }

    [Fact]
    public async Task ResolutionScopesCanBeForceClearedAtALifecycleBoundary()
    {
        var scopes = new ResolutionScopeRegistry<object, object>(throwOnThreadViolation: false);
        Assert.True(scopes.Begin(new object(), new object()));

        int cleared = await Task.Run(scopes.ForceClear);

        Assert.Equal(1, cleared);
        Assert.Equal(0, scopes.Count);
    }
}
