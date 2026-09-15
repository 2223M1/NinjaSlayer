using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class PlayerMacheteVisuals : Node2D
{
    private Player _player = null!;
    private Sprite2D _source = null!;
    private Sprite2D? _overlay;
    private NinjaSlayerAimPose? _pose;
    private readonly Node2D[] _hands = [new() { Name = "Primary", ZIndex = 110 }, new() { Name = "Secondary", ZIndex = 100 }];
    // Runtime ownership only. Native card/pile state reconstructs these nodes after a load.
    private readonly Dictionary<CardModel, Sprite2D> _knives = [];

    internal static PlayerMacheteVisuals? Get(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.FindChild("PlayerMachetes", true, false) as PlayerMacheteVisuals;

    private static PlayerMacheteVisuals? Ensure(Player player)
    {
        if (player.Character is not INinjaSlayerCharacter || player.Creature.GetCreatureNode() is not { } actor) return null;
        if (Get(player.Creature) is { } existing) return existing;
        var visual = new PlayerMacheteVisuals { Name = "PlayerMachetes", _player = player };
        visual._source = NinjaSlayerVisualRig.GetBodySprite(actor.Visuals)!;
        visual._overlay = actor.Visuals.FindChild("NarakuVisualOverlay", true, false) as Sprite2D;
        visual._pose = NinjaSlayerAimPose.Get(player.Creature);
        if (visual._pose != null) visual._pose.Machetes = visual;
        visual._source.GetParent().AddChild(visual);
        foreach (Node2D hand in visual._hands) visual.AddChild(hand);
        visual.SyncForPose();
        return visual;
    }

    internal static Node2D? CatchHand(Player player)
    {
        if (Ensure(player) is not { } visual) return null;
        visual.SyncForPose();
        int count = PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().Count();
        return visual._hands[Math.Min(count, 1)];
    }

    internal static void Bind(SawatariMachete card, Sprite2D knife)
    {
        // Called before the native generated-card command dispatches pile changes.
        Ensure(card.Owner)!._knives.Add(card, knife);
    }

    internal static void Refresh(Player player)
    {
        CardModel[] cards = PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().ToArray();
        PlayerMacheteVisuals? visual = Get(player.Creature) ?? (cards.Length > 0 ? Ensure(player) : null);
        if (visual == null) return;
        foreach (var (card, knife) in visual._knives.ToArray())
        {
            // Moving Hand -> Play precedes OnPlay; keep that same knife for release.
            if (card.Pile?.Type is PileType.Hand or PileType.Play) continue;
            knife.QueueFree();
            visual._knives.Remove(card);
        }
        for (int i = 0; i < cards.Length; i++)
        {
            if (!visual._knives.TryGetValue(cards[i], out Sprite2D? knife))
            {
                knife = SawatariWeaponVisuals.CreateMachete();
                knife.Scale = Vector2.One * SawatariWeaponVisuals.BladeScale(Math.Min(i, 1));
                visual._hands[Math.Min(i, 1)].AddChild(knife);
                visual._knives.Add(cards[i], knife);
            }
            Node2D hand = visual._hands[Math.Min(i, 1)];
            if (knife.GetParent() != hand)
            {
                knife.Reparent(hand, keepGlobalTransform: true);
                knife.Transform = new Transform2D(0f, knife.Scale.Abs(), 0f, Vector2.Zero);
            }
            knife.Visible = i < 2;
        }
        visual.SyncForPose();
    }

    internal void SyncForPose()
    {
        Visible = _player.Creature.IsAlive;
        Sprite2D body = _overlay is { Visible: true } ? _overlay : _source;
        Transform2D bodyCanvas = _pose?.HellTornado is { Active: true } tornado
            ? tornado.BodyCanvasTransform : body.GetGlobalTransformWithCanvas();
        Transform = GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse() * bodyCanvas;
        NinjaSlayerFormKind kind = NinjaSlayerFormState.GetPresentation(_player.Creature).Kind;
        var grips = kind switch
        {
            NinjaSlayerFormKind.Normal => (new Vector2(798, -45), new Vector2(432, 35)),
            NinjaSlayerFormKind.Naraku => (new Vector2(798, -45), new Vector2(432, 33)),
            NinjaSlayerFormKind.FullyReleasedNaraku => (new Vector2(471, 310), new Vector2(-364, 316)),
            NinjaSlayerFormKind.OneBodyOneSoul => (new Vector2(-566.5f, -706), new Vector2(-483.5f, -536)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        float scale = 1f / NinjaSlayerFormCalibration.For(kind).Scale;
        var reflection = new Transform2D(new Vector2(body.FlipH ? -1 : 1, 0), new Vector2(0, body.FlipV ? -1 : 1), Vector2.Zero);
        for (int hand = 0; hand < 2; hand++)
        {
            Vector2 point = hand == 0 ? grips.Item1 : grips.Item2;
            Transform2D grip = reflection * new Transform2D(Mathf.DegToRad(122.9726773f + hand * 20f), new Vector2(scale, -scale), 0, point);
            grip.Origin += body.Offset + (body.Centered ? Vector2.Zero : body.Texture.GetSize() * .5f);
            _hands[hand].Transform = grip;
        }
    }

    internal static async Task Throw(SawatariMachete card, Creature target)
    {
        if (Ensure(card.Owner) is not { } visual || target.GetCreatureNode() is not { } victim) return;
        if (!visual._knives.Remove(card, out Sprite2D? knife))
        {
            // Copies, replays and autoplay outside the hand have no held instance yet.
            knife = SawatariWeaponVisuals.CreateMachete();
            knife.Scale = Vector2.One * SawatariWeaponVisuals.BladeScale(0);
            visual._hands[0].AddChild(knife);
        }
        knife.Show();
        NinjaSlayerAimPose.Get(card.Owner.Creature)?.BeginShurikenThrow(target);
        await Cmd.Wait(NinjaSlayerAimPose.ShurikenWindupSeconds);
        if (!GodotObject.IsInstanceValid(knife) || !knife.IsInsideTree()) return;
        visual.SyncForPose();
        SawatariWeaponVisuals? receiver = target.Monster is SawatariMonster { ActThree: true, MacheteCount: < 2 }
            ? SawatariWeaponVisuals.Get(target) : null;
        Node2D destination = receiver?.ReturnHand() ?? victim.Visuals.VfxSpawnPosition;
        if (await SawatariWeaponVisuals.FlyWeapon(knife, destination, catchWeapon: receiver != null))
            receiver!.Receive(knife);
        Refresh(card.Owner);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_pose) && ReferenceEquals(_pose!.Machetes, this)) _pose.Machetes = null;
    }
}
