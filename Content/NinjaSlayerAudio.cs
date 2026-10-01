namespace NinjaSlayer.Content;

public static class NinjaSlayerAudio
{
    public const string BankPath = NinjaSlayerAssetPaths.FmodRoot + "/NinjaSlayer.bank";
    public const string GuidMappingsPath = NinjaSlayerAssetPaths.FmodRoot + "/GUIDs.txt";
    public const string EventMusicBankPath = NinjaSlayerAssetPaths.FmodRoot + "/NinjaSlayerEventMusic.bank";
    public const string EventMusicGuidMappingsPath = NinjaSlayerAssetPaths.FmodRoot + "/EventMusicGUIDs.txt";
    public const string EventMusicEndParameter = "event_end";
    public const string YukanoEventMusic = "event:/NinjaSlayerAudio/music/yukano_teahouse";
    public const string YamotoKokiEventMusic = "event:/NinjaSlayerAudio/music/yamoto_koki_remix";
    public const string NarakuEventMusic = "event:/NinjaSlayerAudio/music/naraku_crystal_variation";
    public const string YukanoEventMusicGuid = "{ae18763d-ad15-4e6f-be82-0f2eccb17eb7}";
    public const string YamotoKokiEventMusicGuid = "{5a225627-5e7e-47e9-b304-d297ccbd36cc}";
    public const string NarakuEventMusicGuid = "{420ac8ed-0e4f-4b2d-a134-2beaa0d7ca43}";

    private const string MusicRoot = "event:/NinjaSlayerAudio/music";
    public const string NarrationRoot = "event:/NinjaSlayerAudio/sfx/narration";

    public const string NinjaSlayerFastAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/fast_attack";
    public const string NinjaSlayerSlowAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/slow_attack";
    public const string NinjaSlayerCastEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/cast";
    public const string NinjaSlayerHurtEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/hurt";
    public const string NinjaSlayerDeathEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/death";
    public const string NinjaSlayerSuicideEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/suicide";
    public const string NinjaSlayerSelectEvent = "event:/NinjaSlayerAudio/sfx/cinematics/character_select";
    public const string NinjaSlayerTransitionEvent = "event:/NinjaSlayerAudio/sfx/cinematics/run_transition";
    public const string NinjaSlayerShortWashoiEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/short_washoi";
    public const string NinjaSlayerLongWashoiEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/long_washoi";
    public const string NinjaSlayerDomoEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/slow_domo";
    public const string NinjaSlayerNinjaSoulEvent = "event:/NinjaSlayerAudio/sfx/combat/ninja_soul";
    public const string NinjaSlayerKorosuBeshiEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/korosu_beshi";

    /// <summary>Longest clip in the randomized short Washoi FMOD event.</summary>
    public const float ShortWashoiSeconds = 1.024014f;

    /// <summary>Clip length of the long Washoi FMOD event.</summary>
    public const float LongWashoiSeconds = 1.299343f;

    /// <summary>FMOD clip length for ninja_slayer_transition (6月16日(1).wav).</summary>
    public const float TransitionAudioSeconds = 2.0201361f;

    /// <summary>Visual transition video length; the FMOD event continues independently.</summary>
    public const float TransitionVisualSeconds = 2f;

    /// <summary>Delay before new-run loading starts after the Transition view takes over.</summary>
    public const float EmbarkLoadStartDelaySeconds = 0.2f;

    /// <summary>Delay before saved-run loading starts after the Transition view takes over.</summary>
    public const float SaveLoadStartDelaySeconds = 0.6f;

    public const string NinjaSlayerSpinAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/spin_attack";

    public const string NarakuFastAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/naraku/fast_attack";
    public const string NarakuSlowAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/naraku/slow_attack";
    public const string NarakuCastEvent = "event:/NinjaSlayerAudio/sfx/characters/naraku/cast";
    public const string NarakuHurtEvent = "event:/NinjaSlayerAudio/sfx/characters/naraku/hurt";
    public const string NarakuDeathEvent = "event:/NinjaSlayerAudio/sfx/characters/naraku/death";

    internal const string YukanoByeEvent = "event:/NinjaSlayerAudio/sfx/characters/yukano/fast_bye";
    internal const string YukanoAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/yukano/fast_attack";
    internal const string PangbaiBreastEvent = "event:/NinjaSlayerAudio/sfx/narration/breast";
    public const string NinjaSlayerFastDomoEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/fast_domo";
    public const string NinjaSlayerNoDomoEvent = "event:/NinjaSlayerAudio/sfx/characters/ninja_slayer/no_domo";
    public const string DarkNinjaDomoEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/domo";
    public const string ForestSawatariBambooAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/bamboo_attack";
    public const string YamotoKokiSlowAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/yamoto_koki/slow_attack";
    public const string PangbaiLowHealthEvent = "event:/NinjaSlayerAudio/sfx/narration/low_health";

    public const string YamotoKokiByeEvent = "event:/NinjaSlayerAudio/sfx/characters/yamoto_koki/bye";
    public const string YamotoKokiEvent = "event:/NinjaSlayerAudio/sfx/characters/yamoto_koki/event";
    public const string YamotoKokiGoEvent = "event:/NinjaSlayerAudio/sfx/characters/yamoto_koki/go";
    public const string YamotoKokiMissileSummonEvent =
        "event:/sfx/characters/defect/defect_dark_channel";

    public const string DarkNinjaBattleMusicEvent = MusicRoot + "/dark_ninja_battle";
    public const string DarkNinjaBattleMusicGuid = "{4dc577b9-8347-4b1e-b6c1-e4e2e5847207}";
    public const string DarkNinjaBeginEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/begin";
    public const string DarkNinjaBeppinAwakensEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/beppin_awakens";
    public const string DarkNinjaDarkRobeEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/dark_robe";
    public const string DarkNinjaDeathEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/death";
    public const string DarkNinjaDeathKiriEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/death_kiri";
    public const string DarkNinjaFailedEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/failed";
    public const string DarkNinjaFastAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/fast_attack";
    public const string DarkNinjaHurtEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/hurt";
    public const string DarkNinjaInsultEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/insult";
    public const string DarkNinjaKirisuteGomenEvent =
        "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/kirisute_gomen";
    public const string DarkNinjaSlowAttackEvent = "event:/NinjaSlayerAudio/sfx/characters/dark_ninja/slow_attack";
    public const string DarkNinjaStabEvent =
        "event:/sfx/enemy/enemy_attacks/lagavulin_matriarch/lagavulin_matriarch_attack_stab";
    public const string DarkNinjaProgressParameter = "dark_ninja_progress";
    public const float DarkNinjaBattleProgress = 1f;
    public const float DarkNinjaEndProgress = 5f;

    public const string ForestSawatariBattleMusicEvent = MusicRoot + "/forest_sawatari_battle";
    public const string ForestSawatariProgressParameter = "forest_sawatari_progress";
    public const float ForestSawatariEndProgress = 5f;
    public const string SawatariCoopMusicEvent = MusicRoot + "/sawatari_coop_sequence";
    public const string SawatariCoopPhaseParameter = "sawatari_coop_phase";
    public const float SawatariCoopBattlePhase = 0f;
    public const float SawatariCoopDecisionPhase = 1f;
    public const float SawatariCoopLeavePhase = 2f;
    public const float SawatariCoopDuelPhase = 3f;
    public const float SawatariCoopDuelEndPhase = 4f;

    public const string ForestSawatariBeginEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/begin";
    public const string ForestSawatariHurtEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/hurt";
    public const string ForestSawatariDeathEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/death";
    public const string ForestSawatariAttackEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/attack";
    public const string ForestSawatariEndEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/end";
    public const string ForestSawatariDuelEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/duel";
    public const string ForestSawatariEnhancedEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/enhanced";
    public const string ForestSawatariBambooEvent =
        "event:/NinjaSlayerAudio/sfx/characters/forest_sawatari/bamboo";

}
