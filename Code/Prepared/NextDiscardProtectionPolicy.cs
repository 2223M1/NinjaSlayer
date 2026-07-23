namespace NinjaSlayer.Code.Prepared;

internal readonly record struct NextDiscardProtectionDecision(
    bool IsProtectedSource,
    bool ShouldConsumeLayer);

internal static class NextDiscardProtectionPolicy
{
    public static NextDiscardProtectionDecision Resolve(
        int powerAmount,
        bool hasSerializedSourceMarker,
        bool isExpectedSourceMovingFromPlay)
    {
        bool isProtectedSource = hasSerializedSourceMarker || isExpectedSourceMovingFromPlay;
        return new NextDiscardProtectionDecision(
            isProtectedSource,
            powerAmount - (isProtectedSource ? 1 : 0) > 0);
    }
}
