using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

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
        bool forcePerHitComboAudio = NinjaSlayerFormState.GetPresentation(creature).ForcePerHitComboAudio;

        if (hitCount == 1)
        {
            await RunWithSuppressedAutomaticSfx(async () =>
            {
                NinjaSlayerCombatAudioSet.Play(audio.SlowAttack);
                await executeHits();
            });
            return;
        }

        if (forcePerHitComboAudio)
        {
            await RunWithSuppressedAutomaticSfx(executeHits);
            return;
        }

        float totalDuration = hitCount * perHitDuration;
        float loopPlayDuration = Math.Max(
            0f,
            totalDuration - NinjaSlayerAudio.IntroSpinAttackSeconds - NinjaSlayerAudio.OutroSpinAttackSeconds);

        NinjaSlayerCombatAudioSet.Play(audio.IntroSpinAttack);
        Task hitsTask = ObserveFaults(RunWithSuppressedAutomaticSfx(executeHits), "spin combo");

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

    public static void PlayFormSlowAttack(Creature creature) =>
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(creature).SlowAttack);

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
            totalDuration - NinjaSlayerAudio.IntroSpinAttackSeconds);

        bool loopStarted = false;
        bool outroPlayed = false;
        var earlyOutro = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void FinishAudio()
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
        Task hitsTask = ObserveFaults(
            RunWithSuppressedAutomaticSfx(() => executeHits(FinishAudio)),
            "Tornado Fist combo");

        try
        {
            Task completed = await Task.WhenAny(
                Cmd.Wait(NinjaSlayerAudio.IntroSpinAttackSeconds),
                earlyOutro.Task,
                hitsTask);
            if (completed == earlyOutro.Task)
            {
                await hitsTask;
                return;
            }
            if (completed == hitsTask)
            {
                await hitsTask;
                FinishAudio();
                return;
            }

            if (loopPlayDuration > 0f)
            {
                SfxCmd.PlayLoop(creature, audio.LoopSpinAttack);
                loopStarted = true;
                completed = await Task.WhenAny(
                    Cmd.Wait(loopPlayDuration),
                    earlyOutro.Task,
                    hitsTask);
                if (completed == earlyOutro.Task)
                {
                    await hitsTask;
                    return;
                }
                if (completed == hitsTask)
                {
                    await hitsTask;
                    FinishAudio();
                    return;
                }
            }

            await hitsTask;
            FinishAudio();
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
        using IDisposable suppression = XAttackAudioContext.Suppress();
        await action();
    }

    private static Task ObserveFaults(Task task, string operation)
    {
        _ = task.ContinueWith(
            faultedTask => Entry.Logger.Error(
                $"NinjaSlayer {operation} hit task failed: {faultedTask.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }
}
