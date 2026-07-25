using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal static class YamotoKokiIntentVisuals
{
    private const string SummonIconPath =
        "res://NinjaSlayer/images/intents/yamoto_koki_summon.png";
    private const string IaiSlashIconPath =
        "res://NinjaSlayer/images/intents/yamoto_koki_iai_slash.png";

    private static readonly ConditionalWeakTable<NIntent, Texture2D> Icons = new();
    private static Texture2D? _summonIcon;
    private static Texture2D? _iaiSlashIcon;

    public static void Update(NIntent intentNode, AbstractIntent intent, Creature owner)
    {
        Icons.Remove(intentNode);
        if (owner.Monster is not YamotoKokiMonster)
        {
            return;
        }

        Texture2D? icon = intent switch
        {
            SummonIntent => _summonIcon ??= GD.Load<Texture2D>(SummonIconPath),
            AttackIntent => _iaiSlashIcon ??= GD.Load<Texture2D>(IaiSlashIconPath),
            _ => null
        };
        if (icon == null)
        {
            return;
        }

        Icons.Add(intentNode, icon);
        ApplyFrame(intentNode, icon);
        CpuParticles2D? particles = intentNode.GetNodeOrNull<CpuParticles2D>("%IntentParticle");
        if (particles != null)
        {
            particles.Texture = icon;
        }
    }

    public static void ApplyFrame(NIntent intentNode)
    {
        if (Icons.TryGetValue(intentNode, out Texture2D? icon))
        {
            ApplyFrame(intentNode, icon);
        }
    }

    private static void ApplyFrame(NIntent intentNode, Texture2D icon)
    {
        Sprite2D? sprite = intentNode.GetNodeOrNull<Sprite2D>("%Intent");
        if (sprite != null)
        {
            sprite.Texture = icon;
        }
    }
}

public sealed class YamotoKokiIntentUpdatePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_intent_update";
    public static string Description => "Use Yamoto Koki's copied pink intent icons.";
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
        Creature owner) =>
        YamotoKokiIntentVisuals.Update(__instance, intent, owner);
}

public sealed class YamotoKokiIntentFramePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_intent_frame";
    public static string Description => "Keep copied Yamoto Koki icons across vanilla intent frames.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NIntent), nameof(NIntent._Process), [typeof(double)])
    ];

    public static void Postfix(NIntent __instance) =>
        YamotoKokiIntentVisuals.ApplyFrame(__instance);
}
