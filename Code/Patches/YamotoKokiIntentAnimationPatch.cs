using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal static class YamotoKokiIntentVisuals
{
    private const string CustomIconNodeName = "YamotoKokiIntentIcon";

    public static void Update(
        NIntent intentNode,
        AbstractIntent intent,
        IEnumerable<Creature> targets,
        Creature owner)
    {
        Sprite2D? vanillaIcon = intentNode.GetNodeOrNull<Sprite2D>("%Intent");
        if (vanillaIcon?.GetParent() is not Node holder)
        {
            return;
        }

        Sprite2D? customIcon = holder.GetNodeOrNull<Sprite2D>(CustomIconNodeName);
        bool isYamotoKokiIntent = owner.Monster is YamotoKokiMonster
            && intent is YamotoKokiSummonIntent or YamotoKokiIaiSlashIntent;
        if (!isYamotoKokiIntent)
        {
            vanillaIcon.Show();
            customIcon?.Hide();
            return;
        }

        customIcon ??= CreateCustomIcon(holder, vanillaIcon);
        customIcon.Position = vanillaIcon.Position;
        customIcon.Texture = intent.GetTexture(targets, owner);
        customIcon.Show();
        vanillaIcon.Hide();
    }

    private static Sprite2D CreateCustomIcon(Node holder, Sprite2D vanillaIcon)
    {
        Sprite2D customIcon = new()
        {
            Name = CustomIconNodeName,
            Position = vanillaIcon.Position
        };
        holder.AddChild(customIcon);
        holder.MoveChild(customIcon, vanillaIcon.GetIndex() + 1);
        return customIcon;
    }
}

public sealed class YamotoKokiIntentUpdatePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_intent_update";
    public static string Description => "Render Yamoto Koki's friendly intent icons on the vanilla intent layer.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NIntent),
            nameof(NIntent.UpdateIntent),
            [typeof(AbstractIntent), typeof(IEnumerable<Creature>), typeof(Creature)])
    ];

    public static void Postfix(
        NIntent __instance,
        AbstractIntent intent,
        IEnumerable<Creature> targets,
        Creature owner) =>
        YamotoKokiIntentVisuals.Update(__instance, intent, targets, owner);
}
