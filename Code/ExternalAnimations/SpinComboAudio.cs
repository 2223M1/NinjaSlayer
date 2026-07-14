using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Spin-combo FMOD for X-cost multi-hit attacks.
/// SfxCmd.Play/PlayLoop/StopLoop follow OwlMagistrate; card-level orchestration follows Whirlwind.
/// </summary>
public static class SpinComboAudio
{
    public static async Task PlaySequence(
        Creature creature,
        int hitCount,
        float perHitDuration,
        Func<Task> executeHits)
    {
        if (hitCount <= 0)
        {
            return;
        }

        var audio = NinjaSlayerCombatAudioSet.For(creature);
        bool isNaraku = creature.HasPower<NarakuPower>();

        if (hitCount == 1)
        {
            await RunWithSuppressedAutomaticSfx(async () =>
            {
                NinjaSlayerCombatAudioSet.Play(audio.SlowAttack);
                await executeHits();
            });
            return;
        }

        if (isNaraku)
        {
            await RunWithSuppressedAutomaticSfx(executeHits);
            return;
        }

        float totalDuration = hitCount * perHitDuration;
        float loopPlayDuration = Math.Max(
            0f,
            totalDuration - NinjaSlayerAudio.IntroSpinAttackSeconds - NinjaSlayerAudio.OutroSpinAttackSeconds);

        NinjaSlayerCombatAudioSet.Play(audio.IntroSpinAttack);
        Task hitsTask = RunWithSuppressedAutomaticSfx(executeHits);

        await Cmd.Wait(NinjaSlayerAudio.IntroSpinAttackSeconds);

        bool loopStarted = false;
        try
        {
            if (loopPlayDuration > 0f)
            {
                SfxCmd.PlayLoop(creature, audio.LoopSpinAttack);
                loopStarted = true;
                await Cmd.Wait(loopPlayDuration);
            }

            await hitsTask;
            NinjaSlayerCombatAudioSet.Play(audio.OutroSpinAttack);
        }
        finally
        {
            if (loopStarted)
            {
                SfxCmd.StopLoop(creature, audio.LoopSpinAttack);
            }
        }
    }

    public static void PlayNarakuSlowAttack(Creature creature) =>
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.Naraku.SlowAttack);

    public static async Task PlayTornadoFistSequence(
        Creature creature,
        int hitCount,
        float perHitDuration,
        Func<Action, Task> executeHits)
    {
        var audio = NinjaSlayerCombatAudioSet.For(creature);
        float totalDuration = hitCount * perHitDuration;
        float loopPlayDuration = Math.Max(
            0f,
            totalDuration - NinjaSlayerAudio.IntroSpinAttackSeconds - NinjaSlayerAudio.OutroSpinAttackSeconds);

        bool loopStarted = false;
        bool outroPlayed = false;
        var earlyOutro = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void FinishEarly()
        {
            if (outroPlayed)
            {
                return;
            }

            if (loopStarted)
            {
                SfxCmd.StopLoop(creature, audio.LoopSpinAttack);
                loopStarted = false;
            }

            outroPlayed = true;
            NinjaSlayerCombatAudioSet.Play(audio.OutroSpinAttack);
            earlyOutro.TrySetResult();
        }

        NinjaSlayerCombatAudioSet.Play(audio.IntroSpinAttack);
        Task hitsTask = RunWithSuppressedAutomaticSfx(() => executeHits(FinishEarly));

        try
        {
            if (await WaitOrFinishEarly(NinjaSlayerAudio.IntroSpinAttackSeconds, earlyOutro.Task))
            {
                await hitsTask;
                return;
            }

            if (loopPlayDuration > 0f)
            {
                SfxCmd.PlayLoop(creature, audio.LoopSpinAttack);
                loopStarted = true;
                if (await WaitOrFinishEarly(loopPlayDuration, earlyOutro.Task))
                {
                    await hitsTask;
                    return;
                }
            }

            await hitsTask;
            if (!outroPlayed)
            {
                outroPlayed = true;
                NinjaSlayerCombatAudioSet.Play(audio.OutroSpinAttack);
            }
        }
        finally
        {
            if (loopStarted)
            {
                SfxCmd.StopLoop(creature, audio.LoopSpinAttack);
            }
        }
    }

    public static async Task RunWithSuppressedAutomaticSfx(Func<Task> action)
    {
        XAttackAudioContext.SuppressAutomaticSfx = true;
        try
        {
            await action();
        }
        finally
        {
            XAttackAudioContext.SuppressAutomaticSfx = false;
        }
    }

    private static async Task<bool> WaitOrFinishEarly(float duration, Task earlyOutro)
    {
        if (earlyOutro.IsCompleted)
        {
            return true;
        }

        Task completed = await Task.WhenAny(Cmd.Wait(duration), earlyOutro);
        return completed == earlyOutro;
    }
}
