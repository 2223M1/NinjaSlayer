using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherCardVisualSuppression : IDisposable
{
    private static FinisherCardVisualSuppression? _active;

    private readonly NCombatRoom _room;
    private readonly Player _player;
    private readonly HashSet<ulong> _baselineCardIds;
    private readonly Dictionary<NCard, bool> _hiddenCards = new(ReferenceEqualityComparer.Instance);
    private readonly FinisherCardVisualMonitor _monitor;
    private bool _accepting = true;
    private bool _disposed;

    private FinisherCardVisualSuppression(NCombatRoom room, CardPlay cardPlay)
    {
        _room = room;
        _player = cardPlay.Card.Owner;
        _baselineCardIds = EnumerateCards(room.Ui)
            .Select(card => card.GetInstanceId())
            .ToHashSet();
        _monitor = new FinisherCardVisualMonitor
        {
            Name = "NinjaSlayerFinisherCardVisualMonitor",
            ProcessMode = Node.ProcessModeEnum.Always
        };
        _monitor.Initialize(this);
        room.AddChild(_monitor);

        NCard? playedCard = NCard.FindOnTable(cardPlay.Card);
        if (playedCard != null)
        {
            Hide(playedCard, allowBaseline: true);
        }
    }

    public static FinisherCardVisualSuppression Acquire(NCombatRoom room, CardPlay cardPlay)
    {
        _active?.Dispose();
        var suppression = new FinisherCardVisualSuppression(room, cardPlay);
        _active = suppression;
        return suppression;
    }

    public static void OnCardEnteredTree(NCard card)
    {
        _active?.Hide(card, allowBaseline: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accepting = false;
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }

        RestoreCardsInHand();
        FinishMonitorIfIdle();
    }

    internal void ProcessHiddenCards()
    {
        if (!_disposed)
        {
            return;
        }

        foreach (NCard card in _hiddenCards.Keys.ToList())
        {
            if (!GodotObject.IsInstanceValid(card) || !card.IsInsideTree())
            {
                _hiddenCards.Remove(card);
                continue;
            }

            if (IsInHand(card))
            {
                Restore(card);
            }
        }

        FinishMonitorIfIdle();
    }

    internal void OnMonitorExitingTree()
    {
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }

        RestoreCardsInHand();
        _hiddenCards.Clear();
    }

    private void Hide(NCard card, bool allowBaseline)
    {
        if (!_accepting
            || !GodotObject.IsInstanceValid(card)
            || card.Model?.Owner != _player
            || !_room.IsAncestorOf(card)
            || !allowBaseline && _baselineCardIds.Contains(card.GetInstanceId())
            || _hiddenCards.ContainsKey(card))
        {
            return;
        }

        _hiddenCards.Add(card, card.Visible);
        card.Visible = false;
    }

    private void RestoreCardsInHand()
    {
        foreach (NCard card in _hiddenCards.Keys.ToList())
        {
            if (GodotObject.IsInstanceValid(card) && card.IsInsideTree() && IsInHand(card))
            {
                Restore(card);
            }
        }
    }

    private bool IsInHand(NCard card) =>
        GodotObject.IsInstanceValid(_room.Ui.Hand)
        && _room.Ui.Hand.IsAncestorOf(card);

    private void Restore(NCard card)
    {
        if (_hiddenCards.Remove(card, out bool wasVisible) && GodotObject.IsInstanceValid(card))
        {
            card.Visible = wasVisible;
        }
    }

    private void FinishMonitorIfIdle()
    {
        if (_disposed
            && _hiddenCards.Count == 0
            && GodotObject.IsInstanceValid(_monitor)
            && !_monitor.IsQueuedForDeletion())
        {
            _monitor.QueueFree();
        }
    }

    private static IEnumerable<NCard> EnumerateCards(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NCard card)
            {
                yield return card;
            }

            foreach (NCard descendant in EnumerateCards(child))
            {
                yield return descendant;
            }
        }
    }
}

internal partial class FinisherCardVisualMonitor : Node
{
    private FinisherCardVisualSuppression? _owner;

    public void Initialize(FinisherCardVisualSuppression owner)
    {
        _owner = owner;
    }

    public override void _Process(double delta)
    {
        _owner?.ProcessHiddenCards();
    }

    public override void _ExitTree()
    {
        _owner?.OnMonitorExitingTree();
        _owner = null;
    }
}
