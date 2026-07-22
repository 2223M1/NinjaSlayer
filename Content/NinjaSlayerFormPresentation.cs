namespace NinjaSlayer.Content;

public enum NinjaSlayerFormKind
{
    Normal,
    Naraku,
    FullyReleasedNaraku,
    OneBodyOneSoul
}

public enum NinjaSlayerBodyTextureMode
{
    Source,
    SynchronizedIdleSequence,
    Static
}

public enum NinjaSlayerBodyTransformMode
{
    Source,
    LegacyCentered
}

public sealed record NinjaSlayerFormPresentation(
    NinjaSlayerFormKind Kind,
    NinjaSlayerBodyTextureMode BodyTextureMode,
    NinjaSlayerBodyTransformMode BodyTransformMode,
    string? StaticTexturePath,
    string? IdleTexturePrefix,
    int IdleFrameCount,
    float? FixedBodyScale,
    float BodyYOffset,
    float ShadowScale,
    bool UsesNarakuAudio,
    bool UseNormalSpinPivot,
    bool ForcePerHitComboAudio)
{
    public bool UsesOverlay => BodyTextureMode != NinjaSlayerBodyTextureMode.Source;
}

public static class NinjaSlayerFormPresentationCatalog
{
    public const string NormalIdleTexturePrefix =
        "res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_";
    public const string NormalIdleFirstTexturePath = NormalIdleTexturePrefix + "0001.png";
    public const string NarakuIdleTexturePrefix =
        "res://NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_";
    public const string FullyReleasedNarakuTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/naraku.png";
    public const string OneBodyOneSoulTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/one_body_one_soul.png";
    public const int NormalIdleFrameCount = 22;
    public const int NarakuIdleFrameCount = 22;
    public const float ReferenceBodyTextureHeight = 1080f;

    public static NinjaSlayerFormPresentation Normal { get; } = new(
        NinjaSlayerFormKind.Normal,
        NinjaSlayerBodyTextureMode.Source,
        NinjaSlayerBodyTransformMode.Source,
        StaticTexturePath: null,
        IdleTexturePrefix: null,
        IdleFrameCount: 0,
        FixedBodyScale: null,
        BodyYOffset: 0f,
        ShadowScale: 0.5f,
        UsesNarakuAudio: false,
        UseNormalSpinPivot: true,
        ForcePerHitComboAudio: false);

    public static NinjaSlayerFormPresentation Naraku { get; } = new(
        NinjaSlayerFormKind.Naraku,
        NinjaSlayerBodyTextureMode.SynchronizedIdleSequence,
        NinjaSlayerBodyTransformMode.Source,
        StaticTexturePath: null,
        IdleTexturePrefix: NarakuIdleTexturePrefix,
        IdleFrameCount: NarakuIdleFrameCount,
        FixedBodyScale: null,
        BodyYOffset: 0f,
        ShadowScale: 0.5f,
        UsesNarakuAudio: false,
        UseNormalSpinPivot: true,
        ForcePerHitComboAudio: false);

    public static NinjaSlayerFormPresentation FullyReleasedNaraku { get; } = new(
        NinjaSlayerFormKind.FullyReleasedNaraku,
        NinjaSlayerBodyTextureMode.Static,
        NinjaSlayerBodyTransformMode.LegacyCentered,
        StaticTexturePath: FullyReleasedNarakuTexturePath,
        IdleTexturePrefix: null,
        IdleFrameCount: 0,
        FixedBodyScale: 0.5f,
        BodyYOffset: -50f,
        ShadowScale: 1f,
        UsesNarakuAudio: true,
        UseNormalSpinPivot: false,
        ForcePerHitComboAudio: true);

    public static NinjaSlayerFormPresentation OneBodyOneSoul { get; } = new(
        NinjaSlayerFormKind.OneBodyOneSoul,
        NinjaSlayerBodyTextureMode.Static,
        NinjaSlayerBodyTransformMode.LegacyCentered,
        StaticTexturePath: OneBodyOneSoulTexturePath,
        IdleTexturePrefix: null,
        IdleFrameCount: 0,
        FixedBodyScale: null,
        BodyYOffset: 0f,
        ShadowScale: 0.5f,
        UsesNarakuAudio: false,
        UseNormalSpinPivot: false,
        ForcePerHitComboAudio: false);

    public static NinjaSlayerFormPresentation Resolve(
        bool hasNarakuPower,
        bool hasNarakuWithinRelic,
        bool hasOneBodyOneSoulPower)
    {
        if (hasNarakuPower)
        {
            return hasNarakuWithinRelic ? FullyReleasedNaraku : Naraku;
        }

        return hasOneBodyOneSoulPower ? OneBodyOneSoul : Normal;
    }

    public static string NormalIdleTexturePath(int frame) =>
        FrameTexturePath(NormalIdleTexturePrefix, frame, NormalIdleFrameCount);

    public static string? ResolveBodyTexturePath(
        NinjaSlayerFormPresentation presentation,
        string? sourceTexturePath)
    {
        return presentation.BodyTextureMode switch
        {
            NinjaSlayerBodyTextureMode.Source => null,
            NinjaSlayerBodyTextureMode.SynchronizedIdleSequence => FrameTexturePath(
                presentation.IdleTexturePrefix
                    ?? throw new InvalidOperationException("Synchronized form presentation requires an idle texture prefix."),
                ResolveSourceIdleFrame(sourceTexturePath),
                presentation.IdleFrameCount),
            NinjaSlayerBodyTextureMode.Static => presentation.StaticTexturePath
                ?? throw new InvalidOperationException("Static form presentation requires a texture path."),
            _ => throw new ArgumentOutOfRangeException(nameof(presentation), presentation.BodyTextureMode, null)
        };
    }

    private static int ResolveSourceIdleFrame(string? sourceTexturePath)
    {
        if (string.IsNullOrEmpty(sourceTexturePath)
            || !sourceTexturePath.StartsWith(NormalIdleTexturePrefix, StringComparison.Ordinal)
            || !sourceTexturePath.EndsWith(".png", StringComparison.Ordinal))
        {
            return 1;
        }

        ReadOnlySpan<char> frameText = sourceTexturePath.AsSpan(
            NormalIdleTexturePrefix.Length,
            sourceTexturePath.Length - NormalIdleTexturePrefix.Length - ".png".Length);
        return int.TryParse(frameText, out int frame) && frame is >= 1 and <= NormalIdleFrameCount
            ? frame
            : 1;
    }

    private static string FrameTexturePath(string prefix, int frame, int frameCount)
    {
        if (frame is < 1 || frame > frameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), frame, $"Frame must be between 1 and {frameCount}.");
        }

        return $"{prefix}{frame:0000}.png";
    }
}
