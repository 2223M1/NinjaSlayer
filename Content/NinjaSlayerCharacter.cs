using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Characters;
using STS2RitsuLib.Scaffolding.Characters.Visuals.Definition;
using STS2RitsuLib.Scaffolding.Godot;
using STS2RitsuLib.Scaffolding.Visuals;
using STS2RitsuLib.Scaffolding.Visuals.Definition;
using STS2RitsuLib.Scaffolding.Visuals.StateMachine;

namespace NinjaSlayer.Content;

public interface INinjaSlayerCharacter
{
}

public abstract class NinjaSlayerCharacterTemplate<TCardPool> : ModCharacterTemplate<TCardPool, NinjaSlayerRelicPool, NinjaSlayerPotionPool>, INinjaSlayerCharacter
    where TCardPool : CardPoolModel
{
    private const int idleFrameCount = 27;
    private const float idleLoopDuration = idleFrameCount / 30f;
    private const string visualsPath = "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn";
    private const string energyCounterPath = "res://NinjaSlayer/scenes/ui/ninja_slayer_energy_counter.tscn";
    private const string idleTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0001.png";
    private const string iconTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/character_icon_NinjaSlayer.png";
    private const string iconOutlineTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/character_icon_NinjaSlayer_outline.png";
    private const string iconScenePath = "res://NinjaSlayer/scenes/ui/ninja_slayer_icon.tscn";
    private const string selectBgScenePath = "res://NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn";
    private const string selectTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/char_select_NinjaSlayer.png";
    private const string selectLockedTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/char_select_NinjaSlayer_locked.png";
    private const string mapMarkerTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/map_marker.png";
    public const string CharacterSelectTransitionMaterialPath = "res://NinjaSlayer/materials/transitions/ninja_slayer_transition_mat.tres";
    public const string TransitionFramePathFormat = "res://NinjaSlayer/images/ui/transitions/ninja_slayer/ninja_slayer_transition_{0:D4}.png";
    public const int TransitionFrameCount = 60;
    private const float xAttackSpinDuration = 0.24f;
    private const float xAttackSpinFps = 60f;
    public static readonly bool OriginalAnimations = true;
    public static readonly string AttackCueName = OriginalAnimations ? "attack" : "archived_attack";
    public static readonly string HitCueName = OriginalAnimations ? "hit" : "archived_hit";
    public static readonly string BlockedHitCueName = OriginalAnimations ? "blocked_hit" : "archived_blocked_hit";

    public static readonly VisualCueSet CombatVisualCues = ModVisualCues.CueSet()
        .Sequence("idle", AddIdleFrames)
        .Single("attack", idleTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_attack", seq => seq
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.08f, CueStyle(offsetX: 0f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.08f, CueStyle(offsetX: 55f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.08f, CueStyle(offsetX: 0f)))
        .Sequence("x_attack", AddXAttackSpinFrames)
        .Single("hit", idleTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_hit", seq => seq
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.08f, CueStyle(offsetX: 0f, rotationDegrees: 0f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.08f, CueStyle(offsetX: -30f, rotationDegrees: -15f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.08f, CueStyle(offsetX: 0f, rotationDegrees: 0f)))
        .Single("blocked_hit", idleTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_blocked_hit", seq => seq
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.05f, CueStyle(offsetX: 0f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.05f, CueStyle(offsetX: -5f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.05f, CueStyle(offsetX: -20f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png", 0.05f, CueStyle(offsetX: 0f)))
        .Single("cast", "res://NinjaSlayer/images/characters/ninja_slayer/cast/cast_0001.png", 0.2f, CueStyle(offsetX: 0f))
        .Single("dead", "res://NinjaSlayer/images/characters/ninja_slayer/dead/dead_0001.png", CueStyle(offsetX: 0f))
        .Single("relaxed", "res://NinjaSlayer/images/characters/ninja_slayer/relaxed/relaxed_0001.png", CueStyle(offsetX: 0f))
        .Build();

    public static class MerchantVisuals
    {
        public const float BodyScale = 1.1f;
        public const float BodyOffsetX = -40f;
        public const float BodyOffsetY = -100f;
        public const string IdleTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/merchant/ninja_slayer_merchant_idle.png";

        public static VisualNodeStyle BodyStyle() =>
            VisualNodeStyle.Create()
                .WithOffset(new Vector2(BodyOffsetX, BodyOffsetY))
                .WithScale(BodyScale);
    }

    public override CharacterGender Gender => CharacterGender.Masculine;
    public override CharacterAssetProfile AssetProfile => new(
        Scenes: new CharacterSceneAssetSet(
            VisualsPath: visualsPath,
            EnergyCounterPath: energyCounterPath,
            MerchantAnimPath: null,
            RestSiteAnimPath: null
        ),
        Ui: new CharacterUiAssetSet(
            IconTexturePath: iconTexturePath,
            IconOutlineTexturePath: iconOutlineTexturePath,
            IconPath: iconScenePath,
            CharacterSelectBgPath: selectBgScenePath,
            CharacterSelectIconPath: selectTexturePath,
            CharacterSelectLockedIconPath: selectLockedTexturePath,
            CharacterSelectTransitionPath: CharacterSelectTransitionMaterialPath,
            MapMarkerPath: mapMarkerTexturePath
        ),
        Vfx: null,
        Spine: null,
        Audio: new CharacterAudioAssetSet(
            CharacterSelectSfx: NinjaSlayerAudio.NinjaSlayerSelectEvent,
            CharacterTransitionSfx: NinjaSlayerAudio.NinjaSlayerTransitionEvent,
            AttackSfx: null,
            CastSfx: null,
            DeathSfx: NinjaSlayerAudio.NinjaSlayerDeathEvent
        ),
        Multiplayer: null,
        VisualCues: CombatVisualCues,
        WorldProceduralVisuals: CharacterWorldProceduralVisualSetBuilder.Create()
            .Merchant(cues => cues.Single("idle", MerchantVisuals.IdleTexturePath, MerchantVisuals.BodyStyle()))
            .Build()
    );
    public override Color NameColor => new("D32020FF");
    public override int StartingHp => 72;
    public override int StartingGold => 99;
    public override Color EnergyLabelOutlineColor => new("691A1BFF");
    public override Color DialogueColor => new("530909FF");
    public override VfxColor SpeechBubbleColor => VfxColor.Red;
    public override Color MapDrawingColor => new("D32020FF");
    public override Color RemoteTargetingLineColor => new("E34B3FFF");
    public override Color RemoteTargetingLineOutline => new("691A1BFF");
    public override float AttackAnimDelay => 0.15f;
    public override float CastAnimDelay => 0.2f;
    public override bool RequiresEpochAndTimeline => false;
    public override string CharacterSelectSfx => NinjaSlayerAudio.NinjaSlayerSelectEvent;
    public override string CharacterTransitionSfx => NinjaSlayerAudio.NinjaSlayerTransitionEvent;

    protected override NCreatureVisuals? TryCreateCreatureVisuals() => RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(AssetProfile.Scenes!.VisualsPath!);

    private static void AddIdleFrames(VisualFrameSequenceBuilder seq)
    {
        for (var i = 1; i <= idleFrameCount; i++)
        {
            seq.Frame(
                $"res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_{i:0000}.png",
                idleLoopDuration / idleFrameCount,
                CueStyle(offsetX: 0f));
        }

        seq.Loop();
    }

    private static void AddXAttackSpinFrames(VisualFrameSequenceBuilder seq)
    {
        AddVerticalSpinFrames(seq, xAttackSpinDuration, xAttackSpinFps, moveDistance: 0f);
    }

    private static void AddVerticalSpinFrames(VisualFrameSequenceBuilder seq, float duration, float fps, float moveDistance)
    {
        const string framePath = "res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png";
        var frameCount = Mathf.CeilToInt(duration * fps);
        var frameDuration = duration / frameCount;

        for (var i = 0; i < frameCount; i++)
        {
            var progress = frameCount == 1 ? 1f : i / (frameCount - 1f);
            var scaleX = Mathf.Cos(progress * Mathf.Pi * 2f);
            if (Mathf.Abs(scaleX) < 0.18f)
            {
                scaleX = scaleX < 0f ? -0.18f : 0.18f;
            }

            var x = Mathf.Sin(progress * Mathf.Pi) * moveDistance;
            seq.Frame(framePath, frameDuration, CueStyle(offsetX: x, scaleX: scaleX));
        }
    }

    private static VisualNodeStyle CueStyle(float offsetX, float rotationDegrees = 0f, float scaleX = 1f)
    {
        return VisualNodeStyle.Create()
            .WithPosition(NinjaSlayerCombatVisuals.BodySpriteBasePosition + new Vector2(offsetX, 0f))
            .WithScale(new Vector2(NinjaSlayerCombatVisuals.BodySpriteBaseScale * scaleX, NinjaSlayerCombatVisuals.BodySpriteBaseScale))
            .WithRotationDegrees(rotationDegrees);
    }

    protected override ModAnimStateMachine? SetupCustomCombatAnimationStateMachine(Node visualsRoot, CharacterModel character)
    {
        return NinjaSlayerAnimations.BuildCombatAnimationStateMachine(visualsRoot, character);
    }

    [Obsolete("RitsuLib compatibility path for non-Spine combat animation triggers.")]
    protected override ModAnimStateMachine? SetupCustomNonSpineAnimationStateMachine(Node visualsRoot, CharacterModel character)
    {
        return NinjaSlayerAnimations.BuildCombatAnimationStateMachine(visualsRoot, character);
    }

    public override List<string> GetArchitectAttackVfx()
    {
        return new List<string>
        {
            "vfx/vfx_attack_slash",
            "vfx/vfx_heavy_blunt",
            "vfx/vfx_bloody_impact"
        };
    }

}
