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
    private readonly Node2D _hand = new() { Name = "StolenCardPos", Position = new(254f, -12f) };
    private readonly Dictionary<CardModel, Node2D> _cards = [];
    private Sprite2D? _home;
    private Sprite2D? _body;

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
        _home ??= body;
        if (GodotObject.IsInstanceValid(_body)) _body!.TreeExiting -= RestoreHand;
        _body = body;
        _body.TreeExiting += RestoreHand;
        if (_hand.GetParent() == null) body.AddChild(_hand);
        else _hand.Reparent(body, keepGlobalTransform: false);
    }

    private void RestoreHand()
    {
        if (_body != _home && GodotObject.IsInstanceValid(_home) && _home!.IsInsideTree())
            ShowOn(_home);
    }

    internal void Refresh()
    {
        CardModel[] stolen = _creature.Powers.OfType<SwipePower>()
            .Select(power => power.StolenCard).OfType<CardModel>().Where(LocalContext.IsMine).ToArray();
        foreach (CardModel removed in _cards.Keys.Except(stolen).ToArray())
        {
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
            node.Scale = Vector2.One * .32f;
            node.Position = -node.Size * new Vector2(.5f, .92f) * .32f;
            node.MouseFilter = Control.MouseFilterEnum.Ignore;
            _cards.Add(card, grip);
        }
        float spread = Math.Min(40f, Math.Max(0, stolen.Length - 1) * 8f);
        for (int index = 0; index < stolen.Length; index++)
            _cards[stolen[index]].RotationDegrees = stolen.Length == 1 ? 0f
                : -spread * .5f + spread * index / (stolen.Length - 1);
    }

    internal void Hide() => _hand.Visible = false;

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_body)) _body!.TreeExiting -= RestoreHand;
        if (GodotObject.IsInstanceValid(_hand)) _hand.QueueFreeSafely();
    }
}
