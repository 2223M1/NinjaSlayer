namespace NinjaSlayer.Content;

public static class NinjaSlayerAudio
{
    public const string BankPath = "res://NinjaSlayer/audio/fmod/NinjaSlayer.bank";
    public const string GuidMappingsPath = "res://NinjaSlayer/audio/fmod/GUIDs.txt";

    private const string Root = "event:/NinjaSlayerAudio/sfx";
    private const string NinjaSlayerRoot = Root + "/ninja_slayer";
    private const string NarakuRoot = Root + "/naraku";
    private const string PangbaiRoot = Root + "/pangbai";

    public const string NinjaSlayerFastAttackEvent = NinjaSlayerRoot + "/ninja_slayer_fast_attack";
    public const string NinjaSlayerSlowAttackEvent = NinjaSlayerRoot + "/ninja_slayer_slow_attack";
    public const string NinjaSlayerCastEvent = NinjaSlayerRoot + "/ninja_slayer_cast";
    public const string NinjaSlayerHurtEvent = NinjaSlayerRoot + "/ninja_slayer_hurt";
    public const string NinjaSlayerDeathEvent = NinjaSlayerRoot + "/ninja_slayer_death";
    public const string NinjaSlayerSelectEvent = NinjaSlayerRoot + "/ninja_slayer_select";
    public const string NinjaSlayerTransitionEvent = NinjaSlayerRoot + "/ninja_slayer_transition";

    /// <summary>FMOD clip length for ninja_slayer_transition (6月16日(1).wav).</summary>
    public const float TransitionSeconds = 2.0201361f;
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
