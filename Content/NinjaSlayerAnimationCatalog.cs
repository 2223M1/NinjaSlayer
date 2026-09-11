using Godot;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Scaffolding.Visuals;
using STS2RitsuLib.Scaffolding.Visuals.Definition;

namespace NinjaSlayer.Content;

public static class NinjaSlayerAnimationCatalog
{
    private const float IdleFrameDuration = 1f / 24f;
    private const string AttackTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png";
    public static readonly VisualCueSet CombatVisualCues = ModVisualCues.CueSet()
        .Sequence("idle", AddIdleFrames)
        .Single("attack", NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Single("x_attack", AttackTexturePath, 0.24f, CueStyle(offsetX: 0f))
        .Single("tornado_fist", AttackTexturePath, TornadoFistSpinAnimation.TurnSeconds, CueStyle(offsetX: 0f))
        .Single("hit", NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Single("blocked_hit", NinjaSlayerFormPresentationCatalog.NormalIdleFirstTexturePath, 0.01f, CueStyle(offsetX: 0f))
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

    private static VisualNodeStyle CueStyle(float offsetX, float rotationDegrees = 0f, float scaleX = 1f) =>
        VisualNodeStyle.Create()
            .WithPosition(NinjaSlayerCombatVisuals.BodySpriteBasePosition + new Vector2(offsetX, 0f))
            .WithScale(new Vector2(
                NinjaSlayerCombatVisuals.BodySpriteBaseScale * scaleX,
                NinjaSlayerCombatVisuals.BodySpriteBaseScale))
            .WithRotationDegrees(rotationDegrees);
}
