using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class VisitedMapHistoryAlignmentTests
{
    [Fact]
    public void ResolvesVisitedRoomsAfterAnAncientHistoryPrefix()
    {
        int[] visited = [2, 8, 5];
        int[] history = [8, 2, 8, 5];

        Assert.Equal(1, VisitedMapHistoryAlignment.ResolveHistoryIndex(visited, history, 0));
        Assert.Equal(2, VisitedMapHistoryAlignment.ResolveHistoryIndex(visited, history, 1));
        Assert.Equal(3, VisitedMapHistoryAlignment.ResolveHistoryIndex(visited, history, 2));
    }

    [Fact]
    public void PrefersTheExpectedPrefixWhenTypesAreAmbiguous()
    {
        int[] visited = [8];
        int[] history = [8, 8];

        Assert.Equal(1, VisitedMapHistoryAlignment.ResolveHistoryIndex(visited, history, 0));
    }

    [Fact]
    public void DoesNotResolveAVisitedPointWhoseHistoryIsNotWrittenYet()
    {
        int[] visited = [2, 8, 5];
        int[] history = [8, 2, 8];

        Assert.Equal(-1, VisitedMapHistoryAlignment.ResolveHistoryIndex(visited, history, 2));
    }
}
