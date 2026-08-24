using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Combat;

public static class KarateCombatPreviewContext
{
    private static Scope? _current;

    public static IDisposable Enter(CardModel? card, Creature? target) =>
        Enter(card, target == null ? [] : [target]);

    public static IDisposable Enter(CardModel? card, IReadOnlyList<Creature> targets)
    {
        IReadOnlyList<Creature> previousTargets = CurrentTargets;
        Creature[] assignedTargets = targets.Distinct().ToArray();
        var scope = new Scope(FindActive(_current), card, assignedTargets);
        _current = scope;
        RefreshAssignedTargets(previousTargets, assignedTargets);
        return scope;
    }

    public static void RefreshAssignedTargets(
        IEnumerable<Creature> previousTargets,
        IEnumerable<Creature> currentTargets)
    {
        foreach (Creature target in previousTargets.Concat(currentTargets).Distinct())
        {
            CombatHealthBar.Refresh(target);
        }
    }

    public static CardModel? TryGetCard(Creature creature) =>
        IsPreviewTarget(creature) ? CurrentCard : null;

    public static bool IsPreviewTarget(Creature creature) =>
        CurrentTargets.Contains(creature);

    public static CardModel? CurrentCard => FindActive(_current)?.Card;

    public static IReadOnlyList<Creature> CurrentTargets => FindActive(_current)?.Targets ?? [];

    public static Creature? CurrentTarget => CurrentTargets.Count > 0 ? CurrentTargets[0] : null;

    private static Scope? FindActive(Scope? scope)
    {
        while (scope?.IsDisposed == true)
        {
            scope = scope.Parent;
        }

        return scope;
    }

    private sealed class Scope(Scope? parent, CardModel? card, Creature[] targets) : IDisposable
    {
        private int _disposed;

        public Scope? Parent { get; } = parent;
        public CardModel? Card { get; } = card;
        public Creature[] Targets { get; } = targets;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (ReferenceEquals(_current, this))
            {
                Creature[] previousTargets = Targets;
                _current = FindActive(Parent);
                RefreshAssignedTargets(previousTargets, CurrentTargets);
            }
        }
    }
}
