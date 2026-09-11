using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Combat;

internal static class NinjaSlayerAttackExecution
{
    private static readonly AsyncLocal<CommandFrame?> Commands = new();
    private static readonly AsyncLocal<SequenceFrame?> Sequences = new();
    private static readonly object RecoveryOwner = new();

    internal static void DeferFinalRecovery()
    {
        if (CurrentPlay is { } play)
        {
            var state = CardPlayResolutionScope.GetOrCreatePlayState(play, RecoveryOwner, () => new DeferredRecovery());
            if (state != null) state.Pending = true;
        }
    }

    internal static bool TakeDeferredRecovery()
    {
        if (CurrentPlay is not { } play) return false;
        var state = CardPlayResolutionScope.GetOrCreatePlayState(play, RecoveryOwner, () => new DeferredRecovery());
        if (state == null || !state.Pending) return false;
        state.Pending = false;
        return true;
    }

    private sealed class DeferredRecovery { internal bool Pending; }

    internal static CardPlay? CurrentPlay
    {
        get
        {
            if (Commands.Value?.Play is { } play)
                return play;
            CardModel? card = RapidCardPresentationContext.CurrentCard;
            return card != null && CardPlayResolutionScope.TryResolveCurrentPlay(card, out play) ? play : null;
        }
    }

    internal static Creature? Target => Commands.Value?.Target ?? CurrentPlay?.Target;
    internal static AttackCommand? CurrentCommand => Commands.Value?.Command;
    internal static int SequenceHitCount => Sequences.Value?.Count ?? 0;
    internal static bool IsFinalSequenceHit => Sequences.Value is { } sequence && sequence.Index + 1 == sequence.Count;
    internal static bool IsMultiHit => Sequences.Value is { Count: > 1 }
        || Commands.Value is { Hits: > 1 };
    internal static bool NeedsDamageRecovery => Sequences.Value is { } sequence && sequence.Index + 1 < sequence.Count
        || Commands.Value is { } command && command.Command.Results.Count() + 1 < command.Hits;

    internal static CommandLease Enter(AttackCommand command, Creature? target)
    {
        CommandFrame? previous = Commands.Value;
        if (command.Attacker?.Player?.Character is INinjaSlayerCharacter && command.ModelSource is CardModel card)
        {
            CardPlayResolutionScope.TryResolveCurrentPlay(card, out CardPlay? play);
#if !NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            play = command.CardPlay ?? play;
#endif
            Commands.Value = new CommandFrame(command, play, target);
        }
        else
        {
            Commands.Value = null;
        }
        return new CommandLease(previous);
    }

    internal static void SetActualHitCount(AttackCommand command, decimal count)
    {
        if (Commands.Value is { } frame && ReferenceEquals(frame.Command, command))
            frame.Hits = (int)Math.Ceiling(Math.Max(0m, count));
    }

    internal static SequenceLease EnterSequence(int count)
    {
        SequenceFrame? previous = Sequences.Value;
        var frame = new SequenceFrame(count);
        Sequences.Value = frame;
        return new SequenceLease(frame, previous);
    }

    internal sealed class CommandFrame(AttackCommand command, CardPlay? play, Creature? target)
    {
        internal AttackCommand Command { get; } = command;
        internal CardPlay? Play { get; } = play;
        internal Creature? Target { get; } = target;
        internal int Hits { get; set; } = 1;
    }

    internal sealed class SequenceFrame(int count)
    {
        internal int Count { get; } = count;
        internal int Index { get; set; }
    }

    internal readonly struct CommandLease(CommandFrame? previous)
    {
        internal void RestoreCaller() => Commands.Value = previous;
    }

    internal readonly struct SequenceLease(SequenceFrame frame, SequenceFrame? previous) : IDisposable
    {
        internal void SetHit(int index) => frame.Index = index;
        public void Dispose() => Sequences.Value = previous;
    }
}
