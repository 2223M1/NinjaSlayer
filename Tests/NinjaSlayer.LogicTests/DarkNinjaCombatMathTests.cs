using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class DarkNinjaCombatMathTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 0.0625f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.75f, 0.9375f)]
    [InlineData(1f, 1f)]
    public void DeathSlashUsesPronouncedCubicAccelerationAndDeceleration(
        float elapsedProgress,
        float expectedTravelProgress)
    {
        Assert.Equal(
            expectedTravelProgress,
            DarkNinjaCombatMath.SampleDeathSlashTravel(elapsedProgress),
            precision: 6);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 30f)]
    [InlineData(1f, 60f)]
    public void DeathSlashWindupRetreatsBeforeTheCharge(
        float elapsedProgress,
        float expectedOffset)
    {
        Assert.Equal(
            expectedOffset,
            DarkNinjaCombatMath.SampleDeathSlashWindupOffset(elapsedProgress),
            precision: 6);
    }

    [Theory]
    [InlineData(3, 2, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 3, true)]
    [InlineData(0, 3, true)]
    [InlineData(99, 2, false)]
    public void CounterTriggersEveryThirdAttack(
        int remainingHits,
        int expectedRemainingHits,
        bool expectedCounter)
    {
        DarkCounterAdvance result = DarkNinjaCombatMath.AdvanceCounter(remainingHits);

        Assert.Equal(expectedRemainingHits, result.RemainingHits);
        Assert.Equal(expectedCounter, result.ShouldCounter);
    }

    [Theory]
    [InlineData(14, 0, 0, 0)]
    [InlineData(5, 9, 0, 14)]
    [InlineData(0, 3, 11, 14)]
    public void DarkStrikeHealsOnceForTheFullAttackDamageWhenHpWasLost(
        int blockedDamage,
        int unblockedDamage,
        int overkillDamage,
        int expectedHealing)
    {
        int healing = DarkNinjaCombatMath.ResolveDarkStrikeHealing(
            blockedDamage,
            unblockedDamage,
            overkillDamage);

        Assert.Equal(expectedHealing, healing);
    }

    [Fact]
    public void FirstDarkStrikeUsesFullTimelineAndLaterTargetsUseItsSecondHalf()
    {
        DarkNinjaStabSegment first = DarkNinjaCombatMath.GetDarkStrikeSegment(0);
        DarkNinjaStabSegment later = DarkNinjaCombatMath.GetDarkStrikeSegment(1);

        Assert.Equal(0f, first.ReferenceStartSeconds);
        Assert.Equal(0.375f, first.MotionSeconds);
        Assert.Equal(0.025f, first.HoldSeconds);
        Assert.Equal(0.4f, first.MotionSeconds + first.HoldSeconds, precision: 6);
        Assert.Equal(0.2f, later.ReferenceStartSeconds);
        Assert.Equal(0.175f, later.MotionSeconds);
        Assert.Equal(0.025f, later.HoldSeconds);
        Assert.Equal(0.2f, later.MotionSeconds + later.HoldSeconds, precision: 6);
        Assert.Equal(
            DarkNinjaCombatMath.DarkStrikeReferenceMotionSeconds,
            later.ReferenceStartSeconds + later.MotionSeconds,
            precision: 6);
    }

    [Fact]
    public void DarkStrikeSamplesTheUnmodifiedReferenceCubic()
    {
        DarkNinjaPoint start = DarkNinjaCombatMath.SampleDarkStrikeOffset(0f);
        DarkNinjaPoint secondHalfStart = DarkNinjaCombatMath.SampleDarkStrikeOffset(0.2f);
        DarkNinjaPoint impact = DarkNinjaCombatMath.SampleDarkStrikeOffset(0.375f);

        Assert.Equal(-142.389f, start.X, precision: 3);
        Assert.Equal(142.269f, start.Y, precision: 3);
        Assert.InRange(secondHalfStart.X, -14.48f, -14.46f);
        Assert.InRange(secondHalfStart.Y, 14.45f, 14.47f);
        Assert.Equal(0f, impact.X, precision: 3);
        Assert.Equal(0f, impact.Y, precision: 3);
    }

    [Fact]
    public void DarkStrikeFirstVisibleFrameMatchesTheMirroredReferenceTrajectory()
    {
        DarkNinjaPoint start = DarkNinjaCombatMath.SampleDarkStrikeOffset(0f);
        float gameScale = 0.55f;
        float screenX = start.X * gameScale;
        float screenY = start.Y * gameScale;
        float upwardAngleDegrees = MathF.Atan2(-screenY, -screenX) * 180f / MathF.PI;

        Assert.InRange(screenX, -78.32f, -78.30f);
        Assert.InRange(screenY, 78.24f, 78.26f);
        Assert.InRange(upwardAngleDegrees, -45.1f, -44.9f);
    }

    [Fact]
    public void DarkStrikeUsesOneTrajectoryRegardlessOfThePendingDamageOutcome()
    {
        DarkNinjaStabSegment segment = DarkNinjaCombatMath.GetDarkStrikeSegment(1);

        float start = DarkNinjaCombatMath.SampleDarkStrikeVisualReference(
            segment,
            0f);
        float impact = DarkNinjaCombatMath.SampleDarkStrikeVisualReference(
            segment,
            1f);

        Assert.Equal(0.2f, start, precision: 6);
        Assert.Equal(0.375f, impact, precision: 6);
    }

    [Theory]
    [InlineData(true, false, 0.225f)]
    [InlineData(true, true, 0.025f)]
    [InlineData(false, false, 0.025f)]
    public void DarkStrikeOnlyAddsTheLongHoldAfterTheFinalSuccessfulStab(
        bool successfulHit,
        bool hasLaterTarget,
        float expectedSeconds)
    {
        DarkNinjaStabSegment segment = DarkNinjaCombatMath.GetDarkStrikeSegment(0);

        Assert.Equal(
            expectedSeconds,
            DarkNinjaCombatMath.ResolveDarkStrikeHoldSeconds(
                segment,
                successfulHit,
                hasLaterTarget),
            precision: 6);
    }

    [Fact]
    public void DarkStrikeSplitsTheBladeAroundTheUntouchedLiveTarget()
    {
        const int targetZIndex = 12;

        int attackerZIndex = DarkNinjaCombatMath.ResolveDarkStrikeAttackerZIndex(
            targetZIndex);
        const int rearSwordRelativeZIndex = 0;
        const int frontSwordRelativeZIndex = 2;

        Assert.Equal(11, attackerZIndex);
        Assert.Equal(11, attackerZIndex + rearSwordRelativeZIndex);
        Assert.Equal(13, attackerZIndex + frontSwordRelativeZIndex);
        Assert.True(attackerZIndex < targetZIndex);
        Assert.True(targetZIndex < attackerZIndex + frontSwordRelativeZIndex);
    }

    [Theory]
    [InlineData(0f, false, 0f)]
    [InlineData(0.375f, false, 0f)]
    [InlineData(0.375f, true, 151.4f)]
    public void DarkStrikeForegroundBladeAppearsOnlyAfterAConfirmedHit(
        float referenceSeconds,
        bool penetratesTarget,
        float expectedCutTextureX)
    {
        Assert.Equal(
            expectedCutTextureX,
            DarkNinjaCombatMath.ResolveDarkStrikeForegroundBladeCutTextureX(
                referenceSeconds,
                penetratesTarget),
            precision: 3);
    }

    [Fact]
    public void DarkStrikeReturnStartsOutsideTheRightViewportEdge()
    {
        Assert.Equal(
            2120f,
            DarkNinjaCombatMath.ResolveDarkStrikeRightReturnStartX(
                firstViewportEdgeX: 0f,
                secondViewportEdgeX: 1920f,
                characterHalfWidth: 200f));
    }

    [Fact]
    public void MultiplayerTargetsAreOrderedLeftToRightAndTiesStayStable()
    {
        int[] order = DarkNinjaCombatMath.OrderTargetsByCanvasX([400f, 120f, 400f, 250f]);

        Assert.Equal([1, 3, 0, 2], order);
    }

    [Fact]
    public void ReturnParabolaKeepsOneContinuousArcAcrossTheHorizontalWrap()
    {
        DarkNinjaPoint start = new(500f, 700f);
        DarkNinjaPoint end = new(-600f, 500f);

        DarkNinjaPoint atStart = DarkNinjaCombatMath.SampleReturnParabola(start, end, 180f, 0f);
        DarkNinjaPoint atApex = DarkNinjaCombatMath.SampleReturnParabola(start, end, 180f, 0.5f);
        DarkNinjaPoint atEnd = DarkNinjaCombatMath.SampleReturnParabola(start, end, 180f, 1f);

        Assert.Equal(start, atStart);
        Assert.Equal(end, atEnd);
        Assert.Equal(-50f, atApex.X, precision: 3);
        Assert.Equal(420f, atApex.Y, precision: 3);
    }

    [Fact]
    public void BattleFireAppliesNominalDownwardOffsetWithoutShadowConstraint()
    {
        float baseline = DarkNinjaBattleFireLayout.ResolveBaseline(
            [757f],
            []);

        Assert.Equal(683f, baseline);
    }

    [Fact]
    public void BattleFireStaysAboveTheLargestAuthoredShadow()
    {
        float baseline = DarkNinjaBattleFireLayout.ResolveBaseline(
            [757f],
            [683f]);

        Assert.Equal(679f, baseline);
        Assert.True(baseline <= 683f - DarkNinjaBattleFireLayout.ShadowSafetyGap);
    }

    [Fact]
    public void BattleFireFallsBackToShadowSafetyLineWithoutAHitbox()
    {
        float baseline = DarkNinjaBattleFireLayout.ResolveBaseline(
            [],
            [683f]);

        Assert.Equal(679f, baseline);
    }

    [Fact]
    public void BattleFireCoversAFullHdViewportWithSeventeenOverlappingInstances()
    {
        Assert.Equal(17, DarkNinjaBattleFireLayout.GetInstanceCount(1920f));
    }

    [Fact]
    public void BattleFireRevealOrderIsStableFromRightToLeft()
    {
        Assert.Equal(
            [2, 0, 3, 1],
            DarkNinjaBattleFireLayout.OrderRevealByX([100f, -20f, 500f, 100f]));
    }

    [Fact]
    public void BladeChargeFollowsTheCurvedSwordFromBaseToTip()
    {
        DarkNinjaPoint start = DarkNinjaCombatMath.SampleBladeChargePath(0f);
        DarkNinjaPoint quarter = DarkNinjaCombatMath.SampleBladeChargePath(0.25f);
        DarkNinjaPoint half = DarkNinjaCombatMath.SampleBladeChargePath(0.5f);
        DarkNinjaPoint threeQuarters = DarkNinjaCombatMath.SampleBladeChargePath(0.75f);
        DarkNinjaPoint end = DarkNinjaCombatMath.SampleBladeChargePath(1f);

        Assert.Equal(new DarkNinjaPoint(196f, 370f), start);
        Assert.Equal(new DarkNinjaPoint(40f, 30f), end);
        Assert.True(start.Y > quarter.Y);
        Assert.True(quarter.Y > half.Y);
        Assert.True(half.Y > threeQuarters.Y);
        Assert.True(threeQuarters.Y > end.Y);
    }
}
