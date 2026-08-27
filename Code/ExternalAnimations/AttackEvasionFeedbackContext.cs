using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class AttackEvasionFeedbackContext
{
    private static readonly FieldInfo SingleTarget =
        AccessTools.Field(typeof(AttackCommand), "_singleTarget")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_singleTarget");
    private static readonly AsyncLocal<Frame?> Current = new();

    public static Frame? Enter(AttackCommand command)
    {
        if (command.Attacker is not { IsDead: false } attacker
            || !command.DamageProps.IsCardOrMonsterMove())
        {
            return null;
        }

        Frame frame = new(Current.Value, command, attacker);
        Current.Value = frame;
        return frame;
    }

    public static void RestoreCaller(Frame frame)
    {
        if (ReferenceEquals(Current.Value, frame))
        {
            Current.Value = frame.Previous;
        }
    }

    public static async Task<AttackCommand> Complete(Task<AttackCommand> task, Frame frame)
    {
        try
        {
            return await task;
        }
        finally
        {
            frame.IsActive = false;
        }
    }

    public static bool ShouldSuppressTargetVfx(Creature target, string path)
    {
        Frame? frame = Current.Value;
        return frame is { IsActive: true }
            && string.Equals(path, frame.Command.HitVfx, StringComparison.Ordinal)
            && CanEvade(frame, target);
    }

    public static bool ShouldSuppressSideVfx(CombatSide side, string path)
    {
        Frame? frame = Current.Value;
        return frame is { IsActive: true }
            && side == frame.Command.TargetSide
            && string.Equals(path, frame.Command.HitVfx, StringComparison.Ordinal)
            && AllTargetsEvade(frame);
    }

    public static bool ShouldSuppressFmodHitSfx(string path)
    {
        Frame? frame = Current.Value;
        if (frame is not { IsActive: true, IsReplayingHitSfx: false }
            || !string.Equals(path, frame.Command.HitSfx, StringComparison.Ordinal))
        {
            return false;
        }

        if (frame.Command.IsRandomlyTargeted && AnyTargetCanEvade(frame))
        {
            frame.PendingFmodHitSfx = path;
            return true;
        }

        return AllTargetsEvade(frame);
    }

    public static bool ShouldSuppressTemporaryHitSfx(string path)
    {
        Frame? frame = Current.Value;
        if (frame is not { IsActive: true, IsReplayingHitSfx: false }
            || !string.Equals(path, frame.Command.TmpHitSfx, StringComparison.Ordinal))
        {
            return false;
        }

        if (frame.Command.IsRandomlyTargeted && AnyTargetCanEvade(frame))
        {
            frame.PendingTemporaryHitSfx = path;
            return true;
        }

        return AllTargetsEvade(frame);
    }

    public static bool ShouldSuppressCustomHitVfx(AttackCommand command, Creature target)
    {
        Frame? frame = Current.Value;
        return frame is { IsActive: true }
            && ReferenceEquals(command, frame.Command)
            && CanEvade(frame, target);
    }

    public static void ResolveDeferredHitSfx(bool connected)
    {
        Frame? frame = Current.Value;
        if (frame is not { IsActive: true })
        {
            return;
        }

        string? fmod = frame.PendingFmodHitSfx;
        string? temporary = frame.PendingTemporaryHitSfx;
        frame.PendingFmodHitSfx = null;
        frame.PendingTemporaryHitSfx = null;
        if (!connected)
        {
            return;
        }

        frame.IsReplayingHitSfx = true;
        try
        {
            if (fmod is not null)
            {
                SfxCmd.Play(fmod);
            }
            else if (temporary is not null)
            {
                NDebugAudioManager.Instance?.Play(temporary);
            }
        }
        finally
        {
            frame.IsReplayingHitSfx = false;
        }
    }

    private static bool AllTargetsEvade(Frame frame)
    {
        IReadOnlyList<Creature> targets = ResolveTargets(frame);
        return targets.Count > 0 && targets.All(target => CanEvade(frame, target));
    }

    private static bool AnyTargetCanEvade(Frame frame) =>
        ResolveTargets(frame).Any(target => CanEvade(frame, target));

    private static IReadOnlyList<Creature> ResolveTargets(Frame frame)
    {
        if (frame.Command.IsSingleTargeted)
        {
            Creature? target = SingleTarget.GetValue(frame.Command) switch
            {
                null => null,
                Creature creature => creature,
                _ => throw new InvalidOperationException(
                    "AttackCommand._singleTarget has an unexpected runtime type.")
            };
            return target is null ? [] : [target];
        }

        return frame.Command.IsMultiTargeted && frame.Attacker.CombatState is { } combatState
            ? combatState.GetCreaturesOnSide(frame.Command.TargetSide)
            : [];
    }

    private static bool CanEvade(Frame frame, Creature target)
    {
        return target is { IsDead: false }
            && target.Side != frame.Attacker.Side
            && target.GetPower<EvasionPower>() is { } evasion
            && evasion.CanEvade(target, frame.Command.DamageProps, frame.Attacker);
    }

    internal sealed class Frame(Frame? previous, AttackCommand command, Creature attacker)
    {
        public Frame? Previous { get; } = previous;
        public AttackCommand Command { get; } = command;
        public Creature Attacker { get; } = attacker;
        public bool IsActive { get; set; } = true;
        public bool IsReplayingHitSfx { get; set; }
        public string? PendingFmodHitSfx { get; set; }
        public string? PendingTemporaryHitSfx { get; set; }
    }
}
