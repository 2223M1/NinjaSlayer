namespace NinjaSlayer.Content;

public static class NinjaSlayerAudio
{
    public const string BankPath = NinjaSlayerAssetPaths.FmodRoot + "/NinjaSlayer.bank";
    public const string GuidMappingsPath = NinjaSlayerAssetPaths.FmodRoot + "/GUIDs.txt";

    private const string Root = "event:/NinjaSlayerAudio/sfx";
    private const string MusicRoot = "event:/NinjaSlayerAudio/music";
    private const string NinjaSlayerRoot = Root + "/ninja_slayer";
    private const string NarakuRoot = Root + "/naraku";
    private const string DarkNinjaRoot = Root + "/dark_ninja";
    private const string ForestSawatariRoot = Root + "/forest_sawatari";
    public const string PangbaiRoot = Root + "/pangbai";
    public const string YamotoKokiRoot = Root + "/yamoto_koki";

    public const string NinjaSlayerFastAttackEvent = NinjaSlayerRoot + "/ninja_slayer_fast_attack";
    public const string NinjaSlayerSlowAttackEvent = NinjaSlayerRoot + "/ninja_slayer_slow_attack";
    public const string NinjaSlayerCastEvent = NinjaSlayerRoot + "/ninja_slayer_cast";
    public const string NinjaSlayerHurtEvent = NinjaSlayerRoot + "/ninja_slayer_hurt";
    public const string NinjaSlayerDeathEvent = NinjaSlayerRoot + "/ninja_slayer_death";
    public const string NinjaSlayerSuicideEvent = NinjaSlayerRoot + "/ninja_slayer_suicide";
    public const string NinjaSlayerSelectEvent = NinjaSlayerRoot + "/ninja_slayer_select";
    public const string NinjaSlayerTransitionEvent = NinjaSlayerRoot + "/ninja_slayer_transition";
    public const string NinjaSlayerShortWashoiEvent = NinjaSlayerRoot + "/ninja_slayer_short_washoi";
    public const string NinjaSlayerLongWashoiEvent = NinjaSlayerRoot + "/ninja_slayer_long_washoi";
    public const string NinjaSlayerDomoEvent = NinjaSlayerRoot + "/ninja_slayer_domo";
    public const string NinjaSlayerNinjaSoulEvent = NinjaSlayerRoot + "/ninja_slayer_ninja_soul";
    public const string NinjaSlayerExplosionEvent = NinjaSlayerRoot + "/ninja_slayer_explotion";
    public const string NinjaSlayerKorosuBeshiEvent = NinjaSlayerRoot + "/ninja_slayer_korosu_beshi";

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

    public const string NinjaSlayerIntroSpinAttackEvent = NinjaSlayerRoot + "/ninja_slayer_intro_spin_attack";
    public const string NinjaSlayerLoopSpinAttackEvent = NinjaSlayerRoot + "/ninja_slayer_loop_spin_attack";
    public const string NinjaSlayerOutroSpinAttackEvent = NinjaSlayerRoot + "/ninja_slayer_outro_spin_attack";

    public const string NarakuFastAttackEvent = NarakuRoot + "/naraku_fast_attack";
    public const string NarakuSlowAttackEvent = NarakuRoot + "/naraku_slow_attack";
    public const string NarakuCastEvent = NarakuRoot + "/naraku_cast";
    public const string NarakuHurtEvent = NarakuRoot + "/naraku_hurt";
    public const string NarakuDeathEvent = NarakuRoot + "/naraku_death";

    public const string PangbaiLongjuanquanEvent = PangbaiRoot + "/pangbai_longjuanquan";
    public const string PangbaiDragonFlyingKickEvent = PangbaiRoot + "/pangbai_dragon_flying_kick";
    public const string PangbaiSomersaultKickEvent = PangbaiRoot + "/pangbai_somersault_kick";
    public const string PangbaiScaryEvent = PangbaiRoot + "/pangbai_scary";
    public const string PangbaiLowHealthEvent = PangbaiRoot + "/pangbai_low_health";

    public const string YamotoKokiByeEvent = YamotoKokiRoot + "/yamoto_koki_bye";
    public const string YamotoKokiEvent = YamotoKokiRoot + "/yamoto_koki_event";
    public const string YamotoKokiFastAttackEvent = YamotoKokiRoot + "/yamoto_koki_fast_attack";
    public const string YamotoKokiGoEvent = YamotoKokiRoot + "/yamoto_koki_go";
    public const string YamotoKokiMissileSummonEvent =
        "event:/sfx/characters/defect/defect_dark_channel";

    public const string DarkNinjaBattleMusicEvent = MusicRoot + "/dark_ninja_battle";
    public const string DarkNinjaBeginEvent = DarkNinjaRoot + "/dark_ninja_begin";
    public const string DarkNinjaBeppinAwakensEvent = DarkNinjaRoot + "/dark_ninja_beppin_awakens";
    public const string DarkNinjaDarkRobeEvent = DarkNinjaRoot + "/dark_ninja_dark_robe";
    public const string DarkNinjaDeathEvent = DarkNinjaRoot + "/dark_ninja_death";
    public const string DarkNinjaDeathKiriEvent = DarkNinjaRoot + "/dark_ninja_death_kiri";
    public const string DarkNinjaFailedEvent = DarkNinjaRoot + "/dark_ninja_failed";
    public const string DarkNinjaFastAttackEvent = DarkNinjaRoot + "/dark_ninja_fast_attack";
    public const string DarkNinjaHurtEvent = DarkNinjaRoot + "/dark_ninja_hurt";
    public const string DarkNinjaInsultEvent = DarkNinjaRoot + "/dark_ninja_insult";
    public const string DarkNinjaKirisuteGomenEvent =
        DarkNinjaRoot + "/dark_ninja_kirisute_goumen";
    public const string DarkNinjaSlowAttackEvent = DarkNinjaRoot + "/dark_ninja_slow_attack";
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
        ForestSawatariRoot + "/forest_sawatari_begin";
    public const string ForestSawatariHurtEvent =
        ForestSawatariRoot + "/forest_sawatari_hurt";
    public const string ForestSawatariAttackEvent =
        ForestSawatariRoot + "/forest_sawatari_attack";
    public const string ForestSawatariEndEvent =
        ForestSawatariRoot + "/forest_sawatari_end";
    public const string ForestSawatariDuelEvent =
        ForestSawatariRoot + "/forest_sawatari_duel";
    public const string ForestSawatariEnhancedEvent =
        ForestSawatariRoot + "/forest_sawatari_enhanced";
    public const string ForestSawatariBambooEvent =
        ForestSawatariRoot + "/forest_sawatari_bamboo";

    /// <summary>FMOD clip length for ninja_slayer_intro_spin_attack.</summary>
    public const float IntroSpinAttackSeconds = 1.369f;

    /// <summary>FMOD clip length for ninja_slayer_loop_spin_attack (one cycle).</summary>
    public const float LoopSpinAttackClipSeconds = 1.184f;

    /// <summary>FMOD clip length for ninja_slayer_outro_spin_attack.</summary>
    public const float OutroSpinAttackSeconds = 1.114f;

}
