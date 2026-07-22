using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.LogicTests;

public sealed class LifecycleTests
{
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
