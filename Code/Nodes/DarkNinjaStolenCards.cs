using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class DarkNinjaStolenCards : Node
{
    private Creature _creature = null!;
    private readonly Node2D _hand = new() { Name = "StolenCardPos", Position = new(254f, 0f) };
    private readonly Dictionary<CardModel, Node2D> _cards = [];
    private readonly Dictionary<CardModel, NativeStolenCardMotion> _motions = [];
    private Sprite2D? _home;
    private Sprite2D? _body;
    private Vector2 _bodyBaseScale;
    private Vector2 _heldScale;

    internal static DarkNinjaStolenCards? Get(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.GetNodeOrNull<DarkNinjaStolenCards>("StolenCards");

    internal static void Create(Creature creature)
    {
        if (creature.GetCreatureNode() is not { } actor) return;
        var cards = new DarkNinjaStolenCards { Name = "StolenCards", _creature = creature };
        actor.Visuals.AddChild(cards);
        cards.ShowOn(NinjaSlayerVisualRig.GetBodySprite(actor.Visuals)!);
        cards.Refresh();
    }

    internal void ShowOn(Sprite2D body)
    {
        if (_home == null)
        {
            _home = body;
            _bodyBaseScale = body.GlobalScale.Abs();
        }
        if (GodotObject.IsInstanceValid(_body)) _body!.TreeExiting -= RestoreHand;
        _body = body;
        _body.TreeExiting += RestoreHand;
        if (_hand.GetParent() == null) body.AddChild(_hand);
        else if (_hand.GetParent() != body) _hand.Reparent(body, keepGlobalTransform: false);
    }

    private void RestoreHand()
    {
        if (_body != _home && GodotObject.IsInstanceValid(_home) && _home!.IsInsideTree())
            ShowOn(_home);
    }

    internal void Refresh(CardModel? incoming = null)
    {
        CardModel[] stolen = _creature.Powers.OfType<SwipePower>()
            .Select(power => power.StolenCard).OfType<CardModel>().Where(LocalContext.IsMine).ToArray();
        if (incoming != null && LocalContext.IsMine(incoming) && !stolen.Contains(incoming))
            stolen = [.. stolen, incoming];
        foreach (CardModel removed in _cards.Keys.Except(stolen).ToArray())
        {
            if (_motions.Remove(removed, out var motion)) motion.Stop();
            _hand.RemoveChild(_cards[removed]);
            _cards[removed].QueueFreeSafely();
            _cards.Remove(removed);
        }
        foreach (CardModel card in stolen)
        {
            if (_cards.ContainsKey(card)) continue;
            var grip = new Node2D();
            _hand.AddChild(grip);
            NCard node = NCard.Create(card)!;
            grip.AddChildSafely(node);
            node.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            node.Scale = Vector2.One;
            node.MouseFilter = Control.MouseFilterEnum.Ignore;
            _cards.Add(card, grip);
            Node2D target = card.Owner.Creature.GetCreatureNode()!.Visuals.VfxSpawnPosition;
            NativeStolenCardMotion motion = NativeStolenCardMotion.Create(grip, death: false, () =>
            {
                _motions.Remove(card);
                if (GodotObject.IsInstanceValid(grip)) grip.Transform = GripPose(card);
            }, () => _hand.GlobalTransform * GripPose(card),
                target: target, mirrored: _home!.GlobalPosition.X < target.GlobalPosition.X);
            _heldScale = motion.HeldScale;
            node.Position = new Vector2(0f, NCard.defaultSize.Y * .5f - 8f / _heldScale.Y);
            grip.Transform = GripPose(card);
            if (Combat.CombatActionTimingRuntime.VisualSeconds(1f) <= 0f)
            {
                grip.Visible = true;
                motion.Stop();
            }
            else _motions[card] = motion;
        }
        foreach (CardModel card in stolen)
        {
            // New cards are appended last, so native sibling order keeps them
            // above older cards without raising the stack above the victim.
            if (!_motions.ContainsKey(card)) _cards[card].Transform = GripPose(card);
        }
    }

    private Transform2D GripPose(CardModel card)
    {
        int index = _cards[card].GetIndex();
        float spread = Math.Min(40f, Math.Max(0, _cards.Count - 1) * 8f);
        float degrees = _cards.Count <= 1 ? 0f : -spread * .5f + spread * index / (_cards.Count - 1);
        return new Transform2D(Mathf.DegToRad(degrees), _heldScale / _bodyBaseScale, 0f, Vector2.Zero);
    }

    internal void Hide() => _hand.Visible = false;

    internal void Drop()
    {
        foreach (var motion in _motions.Values) motion.Stop();
        _motions.Clear();
        foreach (Node2D grip in _cards.Values)
        {
            if (Combat.CombatActionTimingRuntime.VisualSeconds(1f) <= 0f) { grip.QueueFreeSafely(); continue; }
            grip.Reparent(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.SceneContainer);
            grip.Visible = true;
            NativeStolenCardMotion.Create(grip, death: true);
        }
        _cards.Clear();
    }

    public override void _ExitTree()
    {
        foreach (var motion in _motions.Values) if (GodotObject.IsInstanceValid(motion)) motion.Stop();
        _motions.Clear();
        if (GodotObject.IsInstanceValid(_body)) _body!.TreeExiting -= RestoreHand;
        if (GodotObject.IsInstanceValid(_hand)) _hand.QueueFreeSafely();
    }
}
