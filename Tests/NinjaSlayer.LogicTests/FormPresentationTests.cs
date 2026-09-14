using NinjaSlayer.Content;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class FormPresentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OneBodyOneSoulWinsWithoutChangingUnderlyingNaraku(bool naraku, bool relic)
    {
        Assert.Equal(NinjaSlayerFormKind.OneBodyOneSoul,
            NinjaSlayerFormPresentationCatalog.Resolve(naraku, relic, true).Kind);
        Assert.Equal(naraku ? relic ? NinjaSlayerFormKind.FullyReleasedNaraku : NinjaSlayerFormKind.Naraku
            : NinjaSlayerFormKind.Normal, NinjaSlayerFormPresentationCatalog.Resolve(naraku, relic, false).Kind);
    }
    [Theory]
    [InlineData(false, false, NinjaSlayerFormKind.Normal)]
    [InlineData(false, true, NinjaSlayerFormKind.Normal)]
    [InlineData(true, false, NinjaSlayerFormKind.Naraku)]
    [InlineData(true, true, NinjaSlayerFormKind.FullyReleasedNaraku)]
    public void ResolvePreservesFormPriority(
        bool hasNarakuPower,
        bool hasNarakuWithinRelic,
        NinjaSlayerFormKind expected)
    {
        NinjaSlayerFormPresentation actual = NinjaSlayerFormPresentationCatalog.Resolve(
            hasNarakuPower,
            hasNarakuWithinRelic);

        Assert.Equal(expected, actual.Kind);
    }

    [Fact]
    public void PresentationProfilesPreserveExistingVisualAndAudioPolicies()
    {
        NinjaSlayerFormPresentation normal = NinjaSlayerFormPresentationCatalog.Normal;
        Assert.False(normal.UsesOverlay);
        Assert.False(normal.UsesNarakuAudio);
        Assert.True(normal.UseNormalSpinPivot);
        Assert.False(normal.ForcePerHitComboAudio);
        Assert.Equal(1f, normal.ShadowScaleMultiplier);

        NinjaSlayerFormPresentation naraku = NinjaSlayerFormPresentationCatalog.Naraku;
        Assert.Equal(NinjaSlayerBodyTextureMode.SynchronizedIdleSequence, naraku.BodyTextureMode);
        Assert.Equal(NinjaSlayerBodyTransformMode.Source, naraku.BodyTransformMode);
        Assert.False(naraku.UsesNarakuAudio);
        Assert.True(naraku.UseNormalSpinPivot);
        Assert.False(naraku.ForcePerHitComboAudio);
        Assert.Equal(1f, naraku.ShadowScaleMultiplier);

        NinjaSlayerFormPresentation fullyReleased = NinjaSlayerFormPresentationCatalog.FullyReleasedNaraku;
        Assert.Equal(NinjaSlayerBodyTextureMode.Static, fullyReleased.BodyTextureMode);
        Assert.Equal(NinjaSlayerBodyTransformMode.LegacyCentered, fullyReleased.BodyTransformMode);
        Assert.Equal(0.3732865f, fullyReleased.FixedBodyScale);
        Assert.Equal(5.55486f, fullyReleased.BodyYOffset);
        Assert.True(fullyReleased.UsesNarakuAudio);
        Assert.False(fullyReleased.UseNormalSpinPivot);
        Assert.True(fullyReleased.ForcePerHitComboAudio);
        Assert.Equal(1.493146f, fullyReleased.ShadowScaleMultiplier);

    }

    [Theory]
    [InlineData(NinjaSlayerFormKind.Normal)]
    [InlineData(NinjaSlayerFormKind.Naraku)]
    [InlineData(NinjaSlayerFormKind.FullyReleasedNaraku)]
    [InlineData(NinjaSlayerFormKind.OneBodyOneSoul)]
    public void RegisteredFormsShareTheNormalGroundLine(NinjaSlayerFormKind kind)
    {
        var form = NinjaSlayerFormCalibration.For(kind);
        Assert.InRange(Math.Abs(form.Position.Y + form.Foot.Y * form.Scale + 6.45f), 0f, .001f);
    }

    [Fact]
    public void CalibrationPreservesNormalLayoutAndSelectedHandAnchors()
    {
        var normal = NinjaSlayerFormCalibration.For(NinjaSlayerFormKind.Normal);
        var soul = NinjaSlayerFormCalibration.For(NinjaSlayerFormKind.OneBodyOneSoul);
        Assert.Equal(new System.Numerics.Vector2(-166f, -183f), normal.Position);
        Assert.Equal(normal, NinjaSlayerFormCalibration.For(NinjaSlayerFormKind.Naraku));
        var full = NinjaSlayerFormCalibration.For(NinjaSlayerFormKind.FullyReleasedNaraku);
        Assert.Equal(new System.Numerics.Vector2(1180f, 790f), full.Hand + new System.Numerics.Vector2(627f, 627f));
        Assert.Equal(new System.Numerics.Vector2(1754f, 430f), normal.Hand + new System.Numerics.Vector2(900f, 540f));
        Assert.Equal(new System.Numerics.Vector2(77f, 293f), soul.Hand + new System.Numerics.Vector2(871.5f, 1356f));
    }

    [Theory]
    [InlineData(NinjaSlayerFormKind.FullyReleasedNaraku, 292.0241f)]
    [InlineData(NinjaSlayerFormKind.OneBodyOneSoul, 300f)]
    public void AlternateFormsCenterSolidBodyAndPreserveGround(NinjaSlayerFormKind kind, float height)
    {
        var form = NinjaSlayerFormCalibration.For(kind);
        var body = CombatBodyContours.ForForm(kind).Select(p => form.Position + p * form.Scale).ToArray();
        Assert.InRange(Math.Abs((body.Min(p => p.X) + body.Max(p => p.X)) * .5f), 0f, .001f);
        Assert.InRange(Math.Abs(body.Max(p => p.Y) - body.Min(p => p.Y) - height), 0f, .01f);
        Assert.InRange(Math.Abs(body.Max(p => p.Y) - NinjaSlayerFormCalibration.GroundY), 0f, .001f);
    }

    [Theory]
    [InlineData("res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0001.png", 1)]
    [InlineData("res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0011.png", 11)]
    [InlineData("res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0022.png", 22)]
    [InlineData("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 1)]
    [InlineData(null, 1)]
    public void NarakuIdleMirrorsOnlyValidNormalIdleFrames(string? sourceTexturePath, int expectedFrame)
    {
        string? actual = NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
            NinjaSlayerFormPresentationCatalog.Naraku,
            sourceTexturePath);

        Assert.Equal(
            $"{NinjaSlayerFormPresentationCatalog.NarakuIdleTexturePrefix}{expectedFrame:0000}.png",
            actual);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("0023")]
    [InlineData("invalid")]
    public void NarakuIdleFallsBackForMalformedOrOutOfRangeNormalFrames(string frame)
    {
        string sourceTexturePath = NinjaSlayerFormPresentationCatalog.NormalIdleTexturePrefix + frame + ".png";

        string? actual = NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
            NinjaSlayerFormPresentationCatalog.Naraku,
            sourceTexturePath);

        Assert.Equal(
            NinjaSlayerFormPresentationCatalog.NarakuIdleTexturePrefix + "0001.png",
            actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(22)]
    public void LeftFacingNormalIdleUsesMatchingKillFrame(int frame)
    {
        string? actual = NinjaSlayerFormPresentationCatalog.ResolveFacingIdleTexturePath(
            NinjaSlayerFormPresentationCatalog.NormalIdleTexturePath(frame),
            faceLeft: true);

        Assert.Equal(NinjaSlayerFormPresentationCatalog.KillIdleTexturePath(frame), actual);
    }

    [Theory]
    [InlineData(false, "res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0001.png")]
    [InlineData(true, "res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png")]
    [InlineData(true, null)]
    public void FacingVariantDoesNotReplaceRightFacingOrNonIdleTextures(
        bool faceLeft,
        string? sourceTexturePath)
    {
        Assert.Null(NinjaSlayerFormPresentationCatalog.ResolveFacingIdleTexturePath(
            sourceTexturePath,
            faceLeft));
    }

    [Fact]
    public void FacingVariantRejectsOutOfRangeIdleFrame()
    {
        Assert.Null(NinjaSlayerFormPresentationCatalog.ResolveFacingIdleTexturePath(
            NinjaSlayerFormPresentationCatalog.NormalIdleTexturePrefix + "0000.png",
            faceLeft: true));
    }

    [Fact]
    public void StaticAndSourceTexturePoliciesRemainDistinct()
    {
        Assert.Null(NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
            NinjaSlayerFormPresentationCatalog.Normal,
            NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath));
        Assert.Equal(
            NinjaSlayerFormPresentationCatalog.FullyReleasedNarakuTexturePath,
            NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
                NinjaSlayerFormPresentationCatalog.FullyReleasedNaraku,
                sourceTexturePath: null));
    }

    [Fact]
    public void NormalIdleFramePathsRejectOutOfRangeFrames()
    {
        Assert.Equal(
            NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath,
            NinjaSlayerFormPresentationCatalog.NormalIdleTexturePath(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NinjaSlayerFormPresentationCatalog.NormalIdleTexturePath(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NinjaSlayerFormPresentationCatalog.NormalIdleTexturePath(23));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NinjaSlayerFormPresentationCatalog.KillIdleTexturePath(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NinjaSlayerFormPresentationCatalog.KillIdleTexturePath(23));
    }
}
