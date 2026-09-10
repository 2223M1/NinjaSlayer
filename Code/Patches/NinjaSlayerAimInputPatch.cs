using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class NinjaSlayerAimStartPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_aim_start";
    public static string Description => "Preview aimed body poses while a targeted card is being dragged.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NMouseCardPlay), nameof(NMouseCardPlay.Start), Type.EmptyTypes),
        new(typeof(NControllerCardPlay), nameof(NControllerCardPlay.Start), Type.EmptyTypes)
    ];

    public static void Postfix(NCardPlay __instance)
    {
        CardModel? card = __instance.Holder?.CardNode?.Model;
        if (card?.Owner.Character is INinjaSlayerCharacter
            && (card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer
                || card is TornadoFistRedesignV1)
            && NinjaSlayerAimPose.Get(card.Owner.Creature) is { } pose)
            __instance.AddChild(new NinjaSlayerAimInput { Name = "NinjaSlayerAimInput", Card = card, Pose = pose });
    }
}

internal sealed class NinjaSlayerAimHoverPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_aim_hover";
    public static string Description => "Use the selected creature's actual hit center for card aiming.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NCardPlay), "OnCreatureHover", [typeof(NCreature)])];
    public static void Postfix(NCardPlay __instance, NCreature creature)
    {
        if (__instance.GetNodeOrNull<NinjaSlayerAimInput>("NinjaSlayerAimInput") is { } input)
            input.Hovered = creature.Entity;
    }
}

internal sealed class NinjaSlayerAimUnhoverPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_aim_unhover";
    public static string Description => "Return card aiming to the pointer when a creature is unselected.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NCardPlay), "OnCreatureUnhover", [typeof(NCreature)])];
    public static void Postfix(NCardPlay __instance)
    {
        if (__instance.GetNodeOrNull<NinjaSlayerAimInput>("NinjaSlayerAimInput") is { } input)
            input.Hovered = null;
    }
}

internal sealed class NinjaSlayerAimFinishPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_aim_finish";
    public static string Description => "Transfer or restore the card-drag pose when card input ends.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NCardPlay), "Cleanup", [typeof(bool)])];
    public static void Prefix(NCardPlay __instance, bool isFinished) =>
        __instance.GetNodeOrNull<NinjaSlayerAimInput>("NinjaSlayerAimInput")?.Finish(isFinished);
}

public partial class NinjaSlayerAimInput : Node
{
    internal CardModel Card { get; init; } = null!;
    internal NinjaSlayerAimPose Pose { get; init; } = null!;
    internal Creature? Hovered { get; set; }
    private bool _finished;

    public override void _Process(double delta)
    {
        if (_finished || !GodotObject.IsInstanceValid(Pose)) return;
        Vector2 pointer = GetViewport().GetMousePosition();
        Creature? target = Hovered;
        if (GetParent() is NMouseCardPlay && NCombatRoom.Instance is { } room)
        {
            target = room.CreatureNodes.FirstOrDefault(node =>
            {
                if (!node.Entity.IsAlive || !node.Entity.IsHittable) return false;
                if (Card is TornadoFistRedesignV1)
                {
                    if (node.Entity.Side == Card.Owner.Creature.Side) return false;
                }
                else if (!Card.CanPlayTargeting(node.Entity)) return false;
                Vector2 local = node.Hitbox.GetGlobalTransformWithCanvas().AffineInverse() * pointer;
                return new Rect2(Vector2.Zero, node.Hitbox.Size).HasPoint(local);
            })?.Entity;
        }
        Pose.Drag(GetParent(), Card, pointer, target);
    }

    internal void Finish(bool played)
    {
        if (_finished) return;
        _finished = true;
        if (GodotObject.IsInstanceValid(Pose)) Pose.EndDrag(GetParent(), played);
    }

    public override void _ExitTree() => Finish(false);
}
