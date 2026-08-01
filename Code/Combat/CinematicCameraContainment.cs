namespace NinjaSlayer.Code.Combat;

internal static class CinematicCameraContainment
{
    public static float ClampCenter(
        float desiredCenter,
        float viewportPixels,
        float scale,
        float sceneSize)
    {
        if (!float.IsFinite(scale)
            || !float.IsFinite(viewportPixels)
            || !float.IsFinite(sceneSize)
            || scale <= 0f
            || viewportPixels < 0f
            || sceneSize <= 0f)
        {
            return desiredCenter;
        }

        if (!float.IsFinite(desiredCenter))
        {
            desiredCenter = sceneSize * 0.5f;
        }

        float halfViewport = viewportPixels / (2f * scale);
        float minimum = halfViewport;
        float maximum = sceneSize - halfViewport;
        return minimum <= maximum
            ? Math.Clamp(desiredCenter, minimum, maximum)
            : sceneSize * 0.5f;
    }

    public static float ResolveSubjectAwareCenter(
        float desiredCenter,
        float viewportPixels,
        float scale,
        float sceneSize,
        float subjectMinimum,
        float subjectMaximum,
        float safeMarginPixels)
    {
        float containedCenter = ClampCenter(
            desiredCenter,
            viewportPixels,
            scale,
            sceneSize);
        if (!float.IsFinite(scale)
            || !float.IsFinite(subjectMinimum)
            || !float.IsFinite(subjectMaximum)
            || !float.IsFinite(safeMarginPixels)
            || scale <= 0f
            || subjectMaximum < subjectMinimum)
        {
            return containedCenter;
        }

        float halfViewport = viewportPixels / (2f * scale);
        float halfContent = Math.Max(0f, halfViewport - Math.Max(0f, safeMarginPixels) / scale);
        float sceneMinimum = halfViewport;
        float sceneMaximum = sceneSize - halfViewport;
        float subjectCenterMinimum = subjectMaximum - halfContent;
        float subjectCenterMaximum = subjectMinimum + halfContent;
        float minimum = Math.Max(sceneMinimum, subjectCenterMinimum);
        float maximum = Math.Min(sceneMaximum, subjectCenterMaximum);

        // Subject framing is best-effort. Scene containment wins when a large or
        // edge-aligned subject cannot fit at the requested cinematic scale.
        return minimum <= maximum
            ? Math.Clamp(desiredCenter, minimum, maximum)
            : containedCenter;
    }
}
