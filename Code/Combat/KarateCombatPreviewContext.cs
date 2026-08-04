using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Combat;

public static class KarateCombatPreviewContext
{
    private static Scope? _current;

    public static IDisposable Enter(CardModel? card, Creature? target)
    {
        Creature? previousTarget = CurrentTarget;
        var scope = new Scope(FindActive(_current), card, target);
        _current = scope;
        RefreshAssignedTargets(previousTarget, target);
        return scope;
    }

    public static void RefreshAssignedTargets(Creature? previousTarget, Creature? currentTarget)
    {
        CombatHealthBar.Refresh(previousTarget);
        CombatHealthBar.Refresh(currentTarget);
    }

    public static CardModel? TryGetCard(Creature creature) =>
        CurrentTarget == creature ? CurrentCard : null;

    public static CardModel? CurrentCard => FindActive(_current)?.Card;

    public static Creature? CurrentTarget => FindActive(_current)?.Target;

    private static Scope? FindActive(Scope? scope)
    {
        while (scope?.IsDisposed == true)
        {
            scope = scope.Parent;
        }

        return scope;
    }

    private sealed class Scope(Scope? parent, CardModel? card, Creature? target) : IDisposable
    {
        private int _disposed;

        public Scope? Parent { get; } = parent;
        public CardModel? Card { get; } = card;
        public Creature? Target { get; } = target;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (ReferenceEquals(_current, this))
            {
                Creature? previousTarget = Target;
                _current = FindActive(Parent);
                RefreshAssignedTargets(previousTarget, CurrentTarget);
            }
        }
    }
}
