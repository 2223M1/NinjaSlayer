namespace NinjaSlayer.Code.Combat;

internal readonly record struct DarkCounterAdvance(int RemainingHits, bool ShouldCounter);

internal readonly record struct DarkNinjaPoint(float X, float Y);

internal readonly record struct DarkNinjaStabSegment(
    float ReferenceStartSeconds,
    float MotionSeconds,
    float HoldSeconds);

internal static class DarkNinjaCombatMath
{
    private const int MaximumCanvasZIndex = 4095;

    private static readonly DarkNinjaPoint[] BladeChargePath =
    [
        new(196f, 370f),
        new(136f, 310f),
        new(88f, 250f),
        new(58f, 190f),
        new(39f, 130f),
        new(40f, 30f)
    ];

    internal const int CounterInterval = 3;
    internal const float DeathSlashWindupRetreatDistance = 60f;
    internal const float DeathSlashWindupSeconds = 0.25f;
    internal const float DeathSlashOutboundSeconds = 0.25f;
    internal const float DeathSlashOffscreenSeconds = 0.1f;
    internal const float DeathSlashReturnSeconds = 0.25f;
    internal const float DeathSlashTotalSeconds = 0.85f;

    internal const float DarkStrikeReferenceMotionSeconds = 0.375f;
    internal const float DarkStrikeReferenceTotalSeconds = 0.4f;
    internal const float DarkStrikeLaterReferenceStartSeconds = 0.2f;
    internal const float DarkStrikeLaterMotionSeconds = 0.175f;
    internal const float DarkStrikeHoldSeconds = 0.025f;
    internal const float DarkStrikeSuccessfulFinalHoldSeconds = 0.2f;
    internal const float DarkStrikeReturnSeconds = 0.6f;

    internal const float DarkStrikeContactTextureX = 151.4f;
    internal const float DarkStrikeContactTextureY = 325f;
    // Episode 11 frames 5 -> 12, normalized by the source artwork scale.
    internal const float DarkStrikeStartOffsetTextureX = -142.389f;
    internal const float DarkStrikeStartOffsetTextureY = 142.269f;

    internal static DarkCounterAdvance AdvanceCounter(int remainingHits) =>
        remainingHits <= 1
            ? new DarkCounterAdvance(CounterInterval, true)
            : new DarkCounterAdvance(Math.Min(remainingHits, CounterInterval) - 1, false);

    internal static int ResolveDarkStrikeHealing(
        int blockedDamage,
        int unblockedDamage,
        int overkillDamage) =>
        unblockedDamage + overkillDamage > 0
            ? blockedDamage + unblockedDamage + overkillDamage
            : 0;

    internal static float SampleDeathSlashTravel(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        if (progress < 0.5f)
        {
            return 4f * progress * progress * progress;
        }

        float inverse = -2f * progress + 2f;
        return 1f - inverse * inverse * inverse * 0.5f;
    }

    internal static float SampleDeathSlashWindupOffset(float progress) =>
        DeathSlashWindupRetreatDistance * SampleDeathSlashTravel(progress);

    internal static DarkNinjaStabSegment GetDarkStrikeSegment(int targetIndex) =>
        targetIndex <= 0
            ? new DarkNinjaStabSegment(
                0f,
                DarkStrikeReferenceMotionSeconds,
                DarkStrikeHoldSeconds)
            : new DarkNinjaStabSegment(
                DarkStrikeLaterReferenceStartSeconds,
                DarkStrikeLaterMotionSeconds,
                DarkStrikeHoldSeconds);

    internal static float SampleDarkStrikeVisualReference(
        DarkNinjaStabSegment segment,
        float progress) =>
        segment.ReferenceStartSeconds
        + segment.MotionSeconds * Math.Clamp(progress, 0f, 1f);

    internal static float ResolveDarkStrikeHoldSeconds(
        DarkNinjaStabSegment segment,
        bool successfulHit,
        bool hasLaterTarget) =>
        segment.HoldSeconds
        + (successfulHit && !hasLaterTarget ? DarkStrikeSuccessfulFinalHoldSeconds : 0f);

    internal static int ResolveDarkStrikeAttackerZIndex(int targetZIndex) =>
        Math.Clamp(
            targetZIndex - 1,
            -MaximumCanvasZIndex,
            MaximumCanvasZIndex);

    internal static float ResolveDarkStrikeForegroundBladeCutTextureX(
        float referenceSeconds,
        bool penetratesTarget) =>
        penetratesTarget
            ? Math.Clamp(
                DarkStrikeContactTextureX + SampleDarkStrikeOffset(referenceSeconds).X,
                0f,
                DarkStrikeContactTextureX)
            : 0f;

    internal static float ResolveDarkStrikeRightReturnStartX(
        float firstViewportEdgeX,
        float secondViewportEdgeX,
        float characterHalfWidth) =>
        Math.Max(firstViewportEdgeX, secondViewportEdgeX)
        + Math.Max(0f, characterHalfWidth);

    internal static DarkNinjaPoint SampleDarkStrikeOffset(float referenceSeconds)
    {
        float normalized = Math.Clamp(
            referenceSeconds / DarkStrikeReferenceMotionSeconds,
            0f,
            1f);
        float inverse = 1f - normalized;
        float progress = 1f - inverse * inverse * inverse;
        return new DarkNinjaPoint(
            DarkStrikeStartOffsetTextureX * (1f - progress),
            DarkStrikeStartOffsetTextureY * (1f - progress));
    }

    internal static int[] OrderTargetsByCanvasX(IReadOnlyList<float> canvasX) =>
        Enumerable.Range(0, canvasX.Count)
            .OrderBy(index => canvasX[index])
            .ThenBy(index => index)
            .ToArray();

    internal static DarkNinjaPoint SampleReturnParabola(
        DarkNinjaPoint start,
        DarkNinjaPoint end,
        float arcHeight,
        float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        return new DarkNinjaPoint(
            start.X + (end.X - start.X) * progress,
            start.Y + (end.Y - start.Y) * progress
                - 4f * arcHeight * progress * (1f - progress));
    }

    internal static DarkNinjaPoint SampleBladeChargePath(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        if (progress <= 0f)
        {
            return BladeChargePath[0];
        }

        if (progress >= 1f)
        {
            return BladeChargePath[^1];
        }

        float totalLength = 0f;
        for (int index = 1; index < BladeChargePath.Length; index++)
        {
            totalLength += Distance(BladeChargePath[index - 1], BladeChargePath[index]);
        }

        float remaining = totalLength * progress;
        for (int index = 1; index < BladeChargePath.Length; index++)
        {
            DarkNinjaPoint start = BladeChargePath[index - 1];
            DarkNinjaPoint end = BladeChargePath[index];
            float segmentLength = Distance(start, end);
            if (remaining <= segmentLength)
            {
                float segmentProgress = segmentLength <= 0f ? 1f : remaining / segmentLength;
                return new DarkNinjaPoint(
                    start.X + (end.X - start.X) * segmentProgress,
                    start.Y + (end.Y - start.Y) * segmentProgress);
            }

            remaining -= segmentLength;
        }

        return BladeChargePath[^1];
    }

    private static float Distance(DarkNinjaPoint first, DarkNinjaPoint second)
    {
        float x = second.X - first.X;
        float y = second.Y - first.Y;
        return MathF.Sqrt(x * x + y * y);
    }
}

internal static class DarkNinjaBattleFireLayout
{
    internal const float HitboxClearance = 100f;
    internal const float DownwardOffset = 26f;
    internal const float ShadowSafetyGap = 4f;
    internal const float InstanceSpacing = 135f;
    internal const float HorizontalOverhang = 120f;
    internal const float RevealIntervalSeconds = 0.05f;
    internal const float RevealTweenSeconds = 0.15f;
    internal const float InitialHeightScale = 0.2f;
    internal const float PerInstanceSfxVolume = 0.22f;

    internal static float ResolveBaseline(
        IEnumerable<float> hitboxBottoms,
        IEnumerable<float> shadowTops)
    {
        float baseline = float.PositiveInfinity;
        foreach (float bottom in hitboxBottoms)
        {
            if (float.IsFinite(bottom))
            {
                baseline = Math.Min(
                    baseline,
                    bottom - HitboxClearance + DownwardOffset);
            }
        }

        foreach (float top in shadowTops)
        {
            if (float.IsFinite(top))
            {
                baseline = Math.Min(baseline, top - ShadowSafetyGap);
            }
        }

        return float.IsFinite(baseline) ? baseline : float.NaN;
    }

    internal static int GetInstanceCount(float width)
    {
        if (!float.IsFinite(width) || width <= 0f)
        {
            return 1;
        }

        return Math.Max(
            1,
            (int)Math.Ceiling(
                (width + HorizontalOverhang * 2f) / InstanceSpacing) + 1);
    }

    internal static int[] OrderRevealByX(IReadOnlyList<float> canvasX) =>
        Enumerable.Range(0, canvasX.Count)
            .OrderByDescending(index => canvasX[index])
            .ThenBy(index => index)
            .ToArray();

}
