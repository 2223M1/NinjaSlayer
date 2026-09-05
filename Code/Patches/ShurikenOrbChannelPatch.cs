using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ShurikenOrbChannelPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_channel";
    public static string Description =>
        "Fire all Shuriken stock and transfer its transient slot when another orb replaces it.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(OrbCmd),
            nameof(OrbCmd.Channel),
            [typeof(PlayerChoiceContext), typeof(OrbModel), typeof(Player)])
    ];

    public static void Prefix(Player player)
    {
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        var combatState = player.PlayerCombatState
            ?? throw new InvalidOperationException("Orb channeling requires player combat state.");
        var queue = combatState.OrbQueue;
        if (queue.Capacity > 0
            && queue.Orbs.Count >= queue.Capacity
            && queue.Orbs[0] is ShurikenOrb shuriken)
        {
            shuriken.PrepareForReplacementEvoke();
            if (shuriken.OwnsTransientSlot)
            {
                shuriken.TransferTransientSlot();
            }
        }
    }
}

public sealed class ShurikenOrbEvokePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_evoke";
    public static string Description =>
        "Consume one Shuriken stack after an ordinary single or multi-evoke effect.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(OrbCmd),
            "Evoke",
            [
                typeof(PlayerChoiceContext),
                typeof(Player),
                typeof(OrbModel),
                typeof(bool)
            ])
    ];

    public static void Prefix(OrbModel evokedOrb, ref bool dequeue)
    {
        if (CombatManager.Instance.IsOverOrEnding
            || evokedOrb is not ShurikenOrb shuriken
            || shuriken.IsPreparedForReplacementEvoke)
        {
            return;
        }

        if (!dequeue)
        {
            shuriken.PrepareForContinuingEvoke();
            return;
        }

        dequeue = false;
        shuriken.PrepareForSingleStockEvoke();
    }
}

public sealed class ShurikenOrbLayoutPatch : IPatchMethod
{
    private const double LayoutSeconds = 0.45;

    public static string PatchId => "ninjaslayer_shuriken_orb_layout";
    public static string Description =>
        "Keep Shuriken on Ninja Slayer's hand and lay out every later orb in vanilla slots.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NOrbManager), "TweenLayout", [])
    ];

    public static bool Prefix(
        NOrbManager __instance,
        List<NOrb> ____orbs,
        ref Tween? ____curTween)
    {
        if (__instance.GetParent() is not NCreature creatureNode
            || creatureNode.Entity.Player?.Character is not INinjaSlayerCharacter)
        {
            return true;
        }

        NOrb? shuriken = ____orbs.FirstOrDefault(orb => orb.Model is ShurikenOrb);
        if (shuriken == null)
        {
            return true;
        }

        NOrb[] standardOrbs = ____orbs.Where(orb => !ReferenceEquals(orb, shuriken)).ToArray();
        ____curTween?.Kill();
        ____curTween = null;
        if (standardOrbs.Length == 0)
        {
            return false;
        }

        Tween tween = __instance.CreateTween().SetParallel();
        for (int index = 0; index < standardOrbs.Length; index++)
        {
            ShurikenOrbSlotPosition slot = ShurikenOrbLayoutMath.GetStandardPosition(
                standardOrbs.Length,
                index,
                __instance.IsLocal);
            Vector2 position = new(slot.X, slot.Y);
            tween.TweenProperty(standardOrbs[index], "position", position, LayoutSeconds)
                .SetEase(Tween.EaseType.InOut)
                .SetTrans(Tween.TransitionType.Sine);
        }

        ____curTween = tween;
        return false;
    }
}
