namespace NinjaSlayer.Code.Combat;

internal enum FinisherSquashAnchorKind
{
    Center,
    BottomCenter
}

internal readonly record struct FinisherAnchorPoint(float X, float Y);

internal static class FinisherSquashAnchorPolicy
{
    private const float CompressionEpsilon = 0.0001f;

    public static FinisherSquashAnchorKind Resolve(float scaleX, float scaleY)
    {
        if (!float.IsFinite(scaleX) || !float.IsFinite(scaleY))
        {
            return FinisherSquashAnchorKind.Center;
        }

        bool verticalCompression = scaleY < 1f - CompressionEpsilon;
        if (verticalCompression && scaleY < scaleX - CompressionEpsilon)
        {
            return FinisherSquashAnchorKind.BottomCenter;
        }

        return FinisherSquashAnchorKind.Center;
    }

    public static FinisherAnchorPoint ResolveCompensatedPosition(
        FinisherAnchorPoint anchorInParent,
        FinisherAnchorPoint anchorInBody,
        FinisherAnchorPoint basisX,
        FinisherAnchorPoint basisY) =>
        new(
            anchorInParent.X
                - basisX.X * anchorInBody.X
                - basisY.X * anchorInBody.Y,
            anchorInParent.Y
                - basisX.Y * anchorInBody.X
                - basisY.Y * anchorInBody.Y);
}
