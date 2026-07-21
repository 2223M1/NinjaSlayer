namespace NinjaSlayer.Content;

public static class NinjaSlayerAudio
{
    public const string BankPath = NinjaSlayerAssetPaths.FmodRoot + "/NinjaSlayer.bank";
    public const string GuidMappingsPath = NinjaSlayerAssetPaths.FmodRoot + "/GUIDs.txt";

    private const string Root = "event:/NinjaSlayerAudio/sfx";
    private const string NinjaSlayerRoot = Root + "/ninja_slayer";
    private const string NarakuRoot = Root + "/naraku";
    public const string PangbaiRoot = Root + "/pangbai";

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
    public const float LongWashoiSeconds = 1.429343f;

    /// <summary>FMOD clip length for ninja_slayer_transition (6月16日(1).wav).</summary>
    public const float TransitionAudioSeconds = 2.0201361f;

    /// <summary>Visual transition video length; the FMOD event continues independently.</summary>
    public const float TransitionVisualSeconds = 2f;

    /// <summary>Delay before run asset loading starts during the embark transition animation.</summary>
    public const float EmbarkLoadStartDelaySeconds = 0.2f;

    /// <summary>Delay before run asset loading starts during the save-load transition animation.</summary>
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

    /// <summary>FMOD clip length for ninja_slayer_intro_spin_attack.</summary>
    public const float IntroSpinAttackSeconds = 1.369f;

    /// <summary>FMOD clip length for ninja_slayer_loop_spin_attack (one cycle).</summary>
    public const float LoopSpinAttackClipSeconds = 1.184f;

    /// <summary>FMOD clip length for ninja_slayer_outro_spin_attack.</summary>
    public const float OutroSpinAttackSeconds = 1.114f;

    // Legacy aliases kept for any external references.
    public const string CharacterAttackEvent = NinjaSlayerFastAttackEvent;
    public const string CharacterDeathEvent = NinjaSlayerDeathEvent;
    public const string CharacterHurtEvent = NinjaSlayerHurtEvent;
}
