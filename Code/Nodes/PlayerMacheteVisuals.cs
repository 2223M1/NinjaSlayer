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
    private readonly Node2D[] _hands = [new() { Name = "Primary" }, new() { Name = "Secondary" }];
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
        visual.AddChild(visual._hands[1]);
        visual.AddChild(visual._hands[0]);
        visual.SyncForPose();
        return visual;
    }

    internal static Node2D? CatchHand(Player player, int hand)
    {
        if (Ensure(player) is not { } visual) return null;
        visual.SyncForPose();
        return visual._hands[hand];
    }

    internal static void Bind(SawatariMachete card, Sprite2D knife)
    {
        // Called before the native generated-card command dispatches pile changes.
        Ensure(card.Owner)!._knives.Add(card, knife);
    }

    internal static void Refresh(Player player)
    {
        SawatariMachete[] cards = PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().ToArray();
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
            int handIndex = cards[i].HeldHand;
            if (handIndex < 0) continue;
            if (!visual._knives.TryGetValue(cards[i], out Sprite2D? knife))
            {
                knife = SawatariWeaponVisuals.CreateMachete();
                knife.Scale = Vector2.One * SawatariWeaponVisuals.BladeScale(handIndex);
                visual._hands[handIndex].AddChild(knife);
                visual._knives.Add(cards[i], knife);
            }
            Node2D hand = visual._hands[handIndex];
            if (knife.GetParent() != hand)
            {
                knife.Reparent(hand, keepGlobalTransform: true);
                knife.Transform = new Transform2D(0f, knife.Scale.Abs(), 0f, Vector2.Zero);
            }
            knife.Show();
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

    internal static async Task Throw(SawatariMachete card, SawatariMachete thrown, int hand,
        Creature target, int returnHand)
    {
        if (Ensure(card.Owner) is not { } visual || target.GetCreatureNode() is not { } victim) return;
        bool held = visual._knives.Remove(thrown, out Sprite2D? knife);
        if (thrown != card && visual._knives.Remove(card, out Sprite2D? remaining))
            visual._knives.Add(thrown, remaining);
        if (!held)
        {
            // Copies, replays and autoplay outside the hand have no held instance yet.
            knife = SawatariWeaponVisuals.CreateMachete();
            knife.Scale = Vector2.One * SawatariWeaponVisuals.BladeScale(hand);
            visual._hands[hand].AddChild(knife);
        }
        knife!.Show();
        NinjaSlayerAimPose.Get(card.Owner.Creature)?.BeginShurikenThrow(target);
        await Cmd.Wait(NinjaSlayerAimPose.ShurikenWindupSeconds);
        if (!GodotObject.IsInstanceValid(knife) || !knife.IsInsideTree()) return;
        visual.SyncForPose();
        SawatariWeaponVisuals? receiver = returnHand >= 0 && target.Monster is SawatariMonster { ActThree: true, MacheteCount: < 2 }
            ? SawatariWeaponVisuals.Get(target) : null;
        Node2D destination = receiver?.ReturnHand(returnHand) ?? victim.Visuals.VfxSpawnPosition;
        if (await SawatariWeaponVisuals.FlyWeapon(knife, destination, catchWeapon: receiver != null))
            receiver!.Receive(knife);
        Refresh(card.Owner);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_pose) && ReferenceEquals(_pose!.Machetes, this)) _pose.Machetes = null;
    }
}
