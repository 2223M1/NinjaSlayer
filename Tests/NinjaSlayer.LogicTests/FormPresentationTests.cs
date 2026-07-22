using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class FormPresentationTests
{
    [Theory]
    [InlineData(false, false, false, NinjaSlayerFormKind.Normal)]
    [InlineData(false, true, false, NinjaSlayerFormKind.Normal)]
    [InlineData(false, false, true, NinjaSlayerFormKind.OneBodyOneSoul)]
    [InlineData(false, true, true, NinjaSlayerFormKind.OneBodyOneSoul)]
    [InlineData(true, false, false, NinjaSlayerFormKind.Naraku)]
    [InlineData(true, false, true, NinjaSlayerFormKind.Naraku)]
    [InlineData(true, true, false, NinjaSlayerFormKind.FullyReleasedNaraku)]
    [InlineData(true, true, true, NinjaSlayerFormKind.FullyReleasedNaraku)]
    public void ResolvePreservesFormPriority(
        bool hasNarakuPower,
        bool hasNarakuWithinRelic,
        bool hasOneBodyOneSoulPower,
        NinjaSlayerFormKind expected)
    {
        NinjaSlayerFormPresentation actual = NinjaSlayerFormPresentationCatalog.Resolve(
            hasNarakuPower,
            hasNarakuWithinRelic,
            hasOneBodyOneSoulPower);

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
        Assert.Equal(0.5f, normal.ShadowScale);

        NinjaSlayerFormPresentation naraku = NinjaSlayerFormPresentationCatalog.Naraku;
        Assert.Equal(NinjaSlayerBodyTextureMode.SynchronizedIdleSequence, naraku.BodyTextureMode);
        Assert.Equal(NinjaSlayerBodyTransformMode.Source, naraku.BodyTransformMode);
        Assert.False(naraku.UsesNarakuAudio);
        Assert.True(naraku.UseNormalSpinPivot);
        Assert.False(naraku.ForcePerHitComboAudio);
        Assert.Equal(0.5f, naraku.ShadowScale);

        NinjaSlayerFormPresentation fullyReleased = NinjaSlayerFormPresentationCatalog.FullyReleasedNaraku;
        Assert.Equal(NinjaSlayerBodyTextureMode.Static, fullyReleased.BodyTextureMode);
        Assert.Equal(NinjaSlayerBodyTransformMode.LegacyCentered, fullyReleased.BodyTransformMode);
        Assert.Equal(0.5f, fullyReleased.FixedBodyScale);
        Assert.Equal(-50f, fullyReleased.BodyYOffset);
        Assert.True(fullyReleased.UsesNarakuAudio);
        Assert.False(fullyReleased.UseNormalSpinPivot);
        Assert.True(fullyReleased.ForcePerHitComboAudio);
        Assert.Equal(1f, fullyReleased.ShadowScale);

        NinjaSlayerFormPresentation oneBodyOneSoul = NinjaSlayerFormPresentationCatalog.OneBodyOneSoul;
        Assert.Equal(NinjaSlayerBodyTextureMode.Static, oneBodyOneSoul.BodyTextureMode);
        Assert.Equal(NinjaSlayerBodyTransformMode.LegacyCentered, oneBodyOneSoul.BodyTransformMode);
        Assert.Null(oneBodyOneSoul.FixedBodyScale);
        Assert.False(oneBodyOneSoul.UsesNarakuAudio);
        Assert.False(oneBodyOneSoul.UseNormalSpinPivot);
        Assert.False(oneBodyOneSoul.ForcePerHitComboAudio);
        Assert.Equal(0.5f, oneBodyOneSoul.ShadowScale);
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
        Assert.Equal(
            NinjaSlayerFormPresentationCatalog.OneBodyOneSoulTexturePath,
            NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
                NinjaSlayerFormPresentationCatalog.OneBodyOneSoul,
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
    }
}
