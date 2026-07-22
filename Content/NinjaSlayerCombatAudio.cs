using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Scripts;
using System.Diagnostics;

namespace NinjaSlayer.Content;

public readonly struct NinjaSlayerCombatAudioSet
{
    private const float PangbaiDelaySeconds = 1f;

    public string FastAttack { get; init; }
    public string SlowAttack { get; init; }
    public string Cast { get; init; }
    public string Hurt { get; init; }
    public string Death { get; init; }
    public string IntroSpinAttack { get; init; }
    public string LoopSpinAttack { get; init; }
    public string OutroSpinAttack { get; init; }

    public static NinjaSlayerCombatAudioSet For(Creature creature) =>
        NinjaSlayerFormState.IsFullyReleasedNaraku(creature) ? Naraku : NinjaSlayer;

    public static readonly NinjaSlayerCombatAudioSet NinjaSlayer = new()
    {
        FastAttack = NinjaSlayerAudio.NinjaSlayerFastAttackEvent,
        SlowAttack = NinjaSlayerAudio.NinjaSlayerSlowAttackEvent,
        Cast = NinjaSlayerAudio.NinjaSlayerCastEvent,
        Hurt = NinjaSlayerAudio.NinjaSlayerHurtEvent,
        Death = NinjaSlayerAudio.NinjaSlayerDeathEvent,
        IntroSpinAttack = NinjaSlayerAudio.NinjaSlayerIntroSpinAttackEvent,
        LoopSpinAttack = NinjaSlayerAudio.NinjaSlayerLoopSpinAttackEvent,
        OutroSpinAttack = NinjaSlayerAudio.NinjaSlayerOutroSpinAttackEvent,
    };

    public static readonly NinjaSlayerCombatAudioSet Naraku = new()
    {
        FastAttack = NinjaSlayerAudio.NarakuFastAttackEvent,
        SlowAttack = NinjaSlayerAudio.NarakuSlowAttackEvent,
        Cast = NinjaSlayerAudio.NarakuCastEvent,
        Hurt = NinjaSlayerAudio.NarakuHurtEvent,
        Death = NinjaSlayerAudio.NarakuDeathEvent,
        IntroSpinAttack = NinjaSlayerAudio.NinjaSlayerIntroSpinAttackEvent,
        LoopSpinAttack = NinjaSlayerAudio.NinjaSlayerLoopSpinAttackEvent,
        OutroSpinAttack = NinjaSlayerAudio.NinjaSlayerOutroSpinAttackEvent,
    };

    public static void Play(string? eventPath, float volume = 1f)
    {
        if (string.IsNullOrEmpty(eventPath))
        {
            return;
        }

        if (eventPath.StartsWith(NinjaSlayerAudio.PangbaiRoot + "/", StringComparison.Ordinal))
        {
            _ = TaskHelper.RunSafely(PlayDelayed(eventPath, volume));
            return;
        }

        PlayNow(eventPath, volume);
    }

    private static async Task PlayDelayed(string eventPath, float volume)
    {
        await Cmd.Wait(PangbaiDelaySeconds);
        PlayNow(eventPath, volume);
    }

    private static void PlayNow(string eventPath, float volume)
    {
        LogSfx(eventPath);
        SfxCmd.Play(eventPath, volume);
    }

    [Conditional("DEBUG")]
    private static void LogSfx(string eventPath) =>
        Entry.Logger.Info($"Combat SFX: {eventPath}");
}
