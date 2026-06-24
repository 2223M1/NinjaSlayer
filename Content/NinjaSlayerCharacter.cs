using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Cards;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Characters;
using STS2RitsuLib.Scaffolding.Godot;
using STS2RitsuLib.Scaffolding.Visuals;
using STS2RitsuLib.Scaffolding.Visuals.Definition;
using STS2RitsuLib.Scaffolding.Visuals.StateMachine;

namespace NinjaSlayer.Content;

[RegisterCharacter]
public sealed class NinjaSlayerCharacter : ModCharacterTemplate<NinjaSlayerCardPool, NinjaSlayerRelicPool, NinjaSlayerPotionPool>
{
    private const string visualsPath = "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn";
    private const string energyCounterPath = "res://NinjaSlayer/scenes/ui/ninja_slayer_energy_counter.tscn";
    private const string idleTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0001.png";
    private const string iconTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/character_icon_NinjaSlayer.png";
    private const string iconOutlineTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/character_icon_NinjaSlayer_outline.png";
    private const string iconScenePath = "res://NinjaSlayer/scenes/ui/ninja_slayer_icon.tscn";
    private const string selectBgTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/character_select_NinjaSlayer_bg.png";
    private const string selectTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/char_select_NinjaSlayer.png";
    private const string selectLockedTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/char_select_NinjaSlayer_locked.png";
    private const string mapMarkerTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/map_marker.png";
    private static readonly Vector2 combatVisualsBasePosition = new(-160f, -180f);
    private const float combatVisualsBaseScale = 0.32f;
    public const bool OriginalAnimations = true;
    public const string AttackCueName = OriginalAnimations ? "attack" : "archived_attack";
    public const string HitCueName = OriginalAnimations ? "hit" : "archived_hit";
    public const string BlockedHitCueName = OriginalAnimations ? "blocked_hit" : "archived_blocked_hit";

    public static readonly VisualCueSet CombatVisualCues = ModVisualCues.CueSet()
        .Sequence("idle", AddIdleFrames)
        .Single("attack", idleTexturePath, 0.01f, CueStyle(offsetX: 0f))
        .Sequence("archived_attack", seq => seq
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.08f, CueStyle(offsetX: 0f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.08f, CueStyle(offsetX: 55f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.08f, CueStyle(offsetX: 0f)))
        .Sequence("x_attack", seq => seq
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.04f, CueStyle(offsetX: 0f, scaleX: 1f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.04f, CueStyle(offsetX: 0f, scaleX: 0.2f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.04f, CueStyle(offsetX: 0f, scaleX: -1f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.04f, CueStyle(offsetX: 0f, scaleX: -0.2f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.04f, CueStyle(offsetX: 0f, scaleX: 0.2f))
            .Frame("res://NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png", 0.04f, CueStyle(offsetX: 0f, scaleX: 1f)))
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
            CharacterSelectBgPath: selectBgTexturePath,
            CharacterSelectIconPath: selectTexturePath,
            CharacterSelectLockedIconPath: selectLockedTexturePath,
            CharacterSelectTransitionPath: null,
            MapMarkerPath: mapMarkerTexturePath
        ),
        Vfx: null,
        Spine: null,
        Audio: new CharacterAudioAssetSet(
            CharacterSelectSfx: null,
            CharacterTransitionSfx: null,
            AttackSfx: NinjaSlayerAudio.CharacterAttackEvent,
            CastSfx: null,
            DeathSfx: NinjaSlayerAudio.CharacterDeathEvent
        ),
        Multiplayer: null,
        VisualCues: CombatVisualCues,
        WorldProceduralVisuals: null
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
    public override float AttackAnimDelay => 0.24f;
    public override float CastAnimDelay => 0.2f;
    public override bool RequiresEpochAndTimeline => false;

    protected override NCreatureVisuals? TryCreateCreatureVisuals() => RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(AssetProfile.Scenes!.VisualsPath!);

    private static void AddIdleFrames(VisualFrameSequenceBuilder seq)
    {
        for (var i = 1; i <= 30; i++)
        {
            seq.Frame($"res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_{i:0000}.png", 1f / 30f, CueStyle(offsetX: 0f));
        }

        seq.Loop();
    }

    private static VisualNodeStyle CueStyle(float offsetX, float rotationDegrees = 0f, float scaleX = 1f)
    {
        return VisualNodeStyle.Create()
            .WithPosition(combatVisualsBasePosition + new Vector2(offsetX, 0f))
            .WithScale(new Vector2(combatVisualsBaseScale * scaleX, combatVisualsBaseScale))
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
