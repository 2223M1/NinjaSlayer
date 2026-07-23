using NinjaSlayer.Code.Feedback;

namespace NinjaSlayer.LogicTests;

public sealed class FeedbackSessionTests : IDisposable
{
    public FeedbackSessionTests() => NinjaSlayerFeedbackSession.Reset();

    public void Dispose() => NinjaSlayerFeedbackSession.Reset();

    [Fact]
    public void ReopeningTheSameScreenInvalidatesAnEarlierGeneration()
    {
        NinjaSlayerFeedbackSession.Begin();
        Assert.True(NinjaSlayerFeedbackSession.TryBindScreen(42, out var first));

        NinjaSlayerFeedbackSession.Begin();
        Assert.True(NinjaSlayerFeedbackSession.TryBindScreen(42, out var second));

        Assert.NotEqual(first.Generation, second.Generation);
        Assert.False(NinjaSlayerFeedbackSession.IsCurrent(first));
        Assert.False(NinjaSlayerFeedbackSession.TryConfirm(first));
        Assert.True(NinjaSlayerFeedbackSession.TryConfirm(second));
        Assert.False(NinjaSlayerFeedbackSession.TryConfirm(second));
        Assert.True(NinjaSlayerFeedbackSession.IsConfirmed(second));
        Assert.True(NinjaSlayerFeedbackSession.TryGetConfirmedToken(out var confirmed));
        Assert.Equal(second, confirmed);
    }

    [Fact]
    public void AStaleScreenCannotConfirmOrCloseTheCurrentSession()
    {
        NinjaSlayerFeedbackSession.Begin();
        Assert.True(NinjaSlayerFeedbackSession.TryBindScreen(10, out var first));

        NinjaSlayerFeedbackSession.Begin();
        Assert.True(NinjaSlayerFeedbackSession.TryBindScreen(20, out var second));

        Assert.False(NinjaSlayerFeedbackSession.TryConfirm(first));
        Assert.False(NinjaSlayerFeedbackSession.ResetForScreen(10));
        Assert.True(NinjaSlayerFeedbackSession.IsCurrent(second));
        Assert.True(NinjaSlayerFeedbackSession.ResetForScreen(20));
        Assert.False(NinjaSlayerFeedbackSession.IsCurrent(second));
        Assert.False(NinjaSlayerFeedbackSession.TryGetConfirmedToken(out _));
    }

    [Fact]
    public void AnUnboundSessionRejectsConfirmation()
    {
        NinjaSlayerFeedbackSession.Begin();

        Assert.False(NinjaSlayerFeedbackSession.TryGetCurrentToken(7, out _));
        Assert.False(NinjaSlayerFeedbackSession.TryBindScreen(0, out _));
    }
}
