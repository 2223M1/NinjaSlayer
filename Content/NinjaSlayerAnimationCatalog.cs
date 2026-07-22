using Godot;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Scaffolding.Visuals;
using STS2RitsuLib.Scaffolding.Visuals.Definition;

namespace NinjaSlayer.Content;

public static class NinjaSlayerAnimationCatalog
{
    private const float IdleFrameDuration = 1f / 24f;
    private const float XAttackSpinDuration = 0.24f;
    private const float XAttackSpinFps = 60f;
    private const string AttackTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png";
    private const string HitTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png";

    public static readonly bool OriginalAnimations = true;
    public static readonly string AttackCueName = OriginalAnimations ? "attack" : "archived_attack";
    public static readonly string HitCueName = OriginalAnimations ? "hit" : "archived_hit";
    public static readonly string BlockedHitCueName = OriginalAnimations ? "blocked_hit" : "archived_blocked_hit";

    public static readonly VisualCueSet CombatVisualCues = ModVisualCues.CueSet()
        .Sequence("idle", AddIdleFrames)
        .Single("attack", NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_attack", seq => seq
            .Frame(AttackTexturePath, 0.08f, CueStyle(offsetX: 0f))
            .Frame(AttackTexturePath, 0.08f, CueStyle(offsetX: 55f))
            .Frame(AttackTexturePath, 0.08f, CueStyle(offsetX: 0f)))
        .Sequence("x_attack", AddXAttackSpinFrames)
        .Sequence("tornado_fist", AddTornadoFistSpinFrames)
        .Single("hit", NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_hit", seq => seq
            .Frame(HitTexturePath, 0.08f, CueStyle(offsetX: 0f, rotationDegrees: 0f))
            .Frame(HitTexturePath, 0.08f, CueStyle(offsetX: -30f, rotationDegrees: -15f))
            .Frame(HitTexturePath, 0.08f, CueStyle(offsetX: 0f, rotationDegrees: 0f)))
        .Single("blocked_hit", NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_blocked_hit", seq => seq
            .Frame(HitTexturePath, 0.05f, CueStyle(offsetX: 0f))
            .Frame(HitTexturePath, 0.05f, CueStyle(offsetX: -5f))
            .Frame(HitTexturePath, 0.05f, CueStyle(offsetX: -20f))
            .Frame(HitTexturePath, 0.05f, CueStyle(offsetX: 0f)))
        .Single("cast", "res://NinjaSlayer/images/characters/ninja_slayer/cast/cast_0001.png", 0.2f, CueStyle(offsetX: 0f))
        .Single("dead", "res://NinjaSlayer/images/characters/ninja_slayer/dead/dead_0001.png", CueStyle(offsetX: 0f))
        .Single("relaxed", "res://NinjaSlayer/images/characters/ninja_slayer/relaxed/relaxed_0001.png", CueStyle(offsetX: 0f))
        .Build();

    private static void AddIdleFrames(VisualFrameSequenceBuilder sequence)
    {
        for (var frame = 1; frame <= NinjaSlayerFormPresentationCatalog.NormalIdleFrameCount; frame++)
        {
            sequence.Frame(
                NinjaSlayerFormPresentationCatalog.NormalIdleTexturePath(frame),
                IdleFrameDuration,
                CueStyle(offsetX: 0f));
        }

        sequence.Loop();
    }

    private static void AddXAttackSpinFrames(VisualFrameSequenceBuilder sequence)
    {
        AddVerticalSpinFrames(sequence, XAttackSpinDuration, XAttackSpinFps, moveDistance: 0f);
    }

    private static void AddTornadoFistSpinFrames(VisualFrameSequenceBuilder sequence)
    {
        const float fps = 60f;
        int frameCount = Mathf.CeilToInt(TornadoFistSpinAnimation.TurnSeconds * fps);
        float frameDuration = TornadoFistSpinAnimation.TurnSeconds / frameCount;
        float pivotOffset = NinjaSlayerVisualRig.SpinPivotDeltaX * NinjaSlayerCombatVisuals.BodySpriteBaseScale;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float progress = frame / (float)frameCount;
            float scaleX = ClampEdgeOnScale(Mathf.Cos(progress * Mathf.Tau));
            float fixedPivotOffsetX = pivotOffset * (1f - scaleX);
            sequence.Frame(AttackTexturePath, frameDuration, CueStyle(fixedPivotOffsetX, scaleX: scaleX));
        }
    }

    private static void AddVerticalSpinFrames(
        VisualFrameSequenceBuilder sequence,
        float duration,
        float fps,
        float moveDistance)
    {
        int frameCount = Mathf.CeilToInt(duration * fps);
        float frameDuration = duration / frameCount;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float progress = frameCount == 1 ? 1f : frame / (frameCount - 1f);
            float scaleX = ClampEdgeOnScale(Mathf.Cos(progress * Mathf.Pi * 2f));
            float offsetX = Mathf.Sin(progress * Mathf.Pi) * moveDistance;
            sequence.Frame(AttackTexturePath, frameDuration, CueStyle(offsetX, scaleX: scaleX));
        }
    }

    private static float ClampEdgeOnScale(float scaleX)
    {
        if (Mathf.Abs(scaleX) >= 0.18f)
        {
            return scaleX;
        }

        return scaleX < 0f ? -0.18f : 0.18f;
    }

    private static VisualNodeStyle CueStyle(float offsetX, float rotationDegrees = 0f, float scaleX = 1f) =>
        VisualNodeStyle.Create()
            .WithPosition(NinjaSlayerCombatVisuals.BodySpriteBasePosition + new Vector2(offsetX, 0f))
            .WithScale(new Vector2(
                NinjaSlayerCombatVisuals.BodySpriteBaseScale * scaleX,
                NinjaSlayerCombatVisuals.BodySpriteBaseScale))
            .WithRotationDegrees(rotationDegrees);
}
