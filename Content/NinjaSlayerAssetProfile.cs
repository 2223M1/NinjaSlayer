using STS2RitsuLib.Scaffolding.Characters;

namespace NinjaSlayer.Content;

public static class NinjaSlayerAssetProfile
{
    public const string VisualsPath = "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn";
    public const string EnergyCounterPath = "res://NinjaSlayer/scenes/ui/ninja_slayer_energy_counter.tscn";
    public const string IconTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/character_icon_NinjaSlayer.png";
    public const string IconOutlineTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/character_icon_NinjaSlayer_outline.png";
    public const string IconScenePath = "res://NinjaSlayer/scenes/ui/ninja_slayer_icon.tscn";
    public const string CharacterSelectBackgroundScenePath =
        "res://NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn";
    public const string CharacterSelectTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/char_select_NinjaSlayer.png";
    public const string CharacterSelectLockedTexturePath =
        "res://NinjaSlayer/images/characters/ninja_slayer/char_select_NinjaSlayer_locked.png";
    public const string MapMarkerTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/map_marker.png";
    public const string CharacterSelectTransitionMaterialPath =
        "res://NinjaSlayer/materials/transitions/ninja_slayer_transition_mat.tres";
    public const string TransitionVideoPath = "res://NinjaSlayer/videos/ninja_slayer_transition.ogv";

    public static readonly CharacterAssetProfile Profile = new(
        Scenes: new CharacterSceneAssetSet(
            VisualsPath: VisualsPath,
            EnergyCounterPath: EnergyCounterPath,
            MerchantAnimPath: null,
            RestSiteAnimPath: null),
        Ui: new CharacterUiAssetSet(
            IconTexturePath: IconTexturePath,
            IconOutlineTexturePath: IconOutlineTexturePath,
            IconPath: IconScenePath,
            CharacterSelectBgPath: CharacterSelectBackgroundScenePath,
            CharacterSelectIconPath: CharacterSelectTexturePath,
            CharacterSelectLockedIconPath: CharacterSelectLockedTexturePath,
            CharacterSelectTransitionPath: CharacterSelectTransitionMaterialPath,
            MapMarkerPath: MapMarkerTexturePath),
        Vfx: null,
        Spine: null,
        Audio: new CharacterAudioAssetSet(
            CharacterSelectSfx: NinjaSlayerAudio.NinjaSlayerSelectEvent,
            CharacterTransitionSfx: NinjaSlayerAudio.NinjaSlayerTransitionEvent,
            AttackSfx: null,
            CastSfx: null,
            DeathSfx: NinjaSlayerAudio.NinjaSlayerDeathEvent),
        Multiplayer: null,
        VisualCues: NinjaSlayerAnimationCatalog.CombatVisualCues,
        WorldProceduralVisuals: NinjaSlayerWorldVisualProfile.Profile);
}
