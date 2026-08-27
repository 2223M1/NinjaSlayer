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
    public void XAttackAudioScopesTolerateOutOfOrderDisposal()
    {
        IDisposable audioOuter = XAttackAudioContext.Suppress();
        IDisposable audioInner = XAttackAudioContext.Suppress();
        audioOuter.Dispose();
        Assert.True(XAttackAudioContext.SuppressAutomaticSfx);
        audioInner.Dispose();
        Assert.False(XAttackAudioContext.SuppressAutomaticSfx);
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

}
