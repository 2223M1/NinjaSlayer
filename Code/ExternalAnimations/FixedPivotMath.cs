namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FixedPivotMath
{
    internal static (float X, float Y) ResolveBodyPosition(
        float pivotParentX,
        float pivotParentY,
        float pivotBodyX,
        float pivotBodyY,
        float rotationDegrees,
        float scaleX,
        float scaleY)
    {
        float rotation = rotationDegrees * MathF.PI / 180f;
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        float transformedX = cosine * scaleX * pivotBodyX - sine * scaleY * pivotBodyY;
        float transformedY = sine * scaleX * pivotBodyX + cosine * scaleY * pivotBodyY;
        return (pivotParentX - transformedX, pivotParentY - transformedY);
    }
}
