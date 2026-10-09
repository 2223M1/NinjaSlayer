using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyFeedbackV120()
    {
        if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_FEEDBACK_V120") == "sandpit")
        {
            await VerifySandpitOwnsPlayerPosition();
            return;
        }
        if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_FEEDBACK_V120") == "motion")
        {
            await VerifyRepeatedStandaloneAttacks();
            return;
        }
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(ShurikenOrb).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Feedback contracts"));
        if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_FEEDBACK_V120") != "orb")
        {
            using (var combat = new OrbCombat())
            {
                Player survivor = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
                survivor.InitializeSeed("surviving-player");
                combat.State.AddPlayer(survivor);
                survivor.ResetCombatState();
                var queued = AddCard<BlackFlame>(combat);
                combat.Player.Creature.SetCurrentHpInternal(0);
                // Native multiplayer death clears the dying player's piles while the
                // other player's combat and already queued turn-end effects continue.
                await CardPileCmd.RemoveFromCombat(queued);
                Require(CombatManager.Instance.IsInProgress && queued.CombatState == null,
                    "Death fixture must retain combat with a detached queued flame.");
                await (Task)AccessTools.Method(typeof(BlackFlame), "OnTurnEndInHand").Invoke(queued, [Choice])!;
                Require(combat.Enemy.CurrentHp == 1000 && survivor.Creature.IsAlive,
                    "A dead player's pending flame damaged the remaining combat.");
            }
            GD.Print("PASS feedback: multiplayer death cancels queued Black Flame without stopping surviving players.");
        }

        using (var combat = new OrbCombat())
        {
            await AddStock(combat.Player, 1);
            var orb = new HeldContractOrb();
            AccessTools.Property(typeof(NOrb), "Model").SetValue(orb, combat.Orb);
            var labels = new Control { Name = "LabelContainer" };
            orb.AddChild(labels);
            labels.Owner = orb;
            labels.UniqueNameInOwner = true;
            var container = new Control();
            orb.AddChild(container);
            AddChild(orb);
            var conflict = new Harmony("NinjaSlayer.OrbContracts.ForeignOrbFactory");
            conflict.Patch(AccessTools.Method(typeof(OrbModel), nameof(OrbModel.CreateSprite)),
                prefix: new HarmonyMethod(GetType(), nameof(RejectForeignOrbSprite)) { priority = Priority.First });
            Node2D? sprite = null;
            Tween? tween = null;
            try
            {
                ShurikenOrbVisualPatch.Prefix(orb, container, ref sprite, ref tween);
                Require(sprite is NinjaSlayer.Code.Nodes.ShurikenOrbVisual,
                    "Shuriken did not use its registered sprite factory.");
                foreach (CanvasItem item in sprite!.FindChildren("*", "CanvasItem", true, false).Cast<CanvasItem>())
                    Require(item.ZIndex == 0 && item.ZAsRelative,
                        "Held shuriken escaped the native orb draw layer.");
                Require(labels.ZIndex == 0, "Shuriken labels escaped the native orb draw layer.");
            }
            finally
            {
                conflict.UnpatchAll(conflict.Id);
                tween?.Kill();
                orb.Free();
            }
        }
        GD.Print("PASS feedback: dedicated shuriken uses its public factory despite a competing native-orb patch; art and labels stay on the native layer.");
        await VerifyRepeatedStandaloneAttacks();
        await VerifySandpitOwnsPlayerPosition();
    }

    private async Task VerifySandpitOwnsPlayerPosition()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        var actor = new AimContractCreature { Position = new(300f, 500f) };
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, combat.Player.Creature);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, new AimContractVisuals());
        actor.AddChild(actor.Visuals);
        AddChild(actor);
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.NinjaSlayerAllyLayoutMotion", true)!;
        var motion = (Node)AccessTools.Method(type, "Ensure").Invoke(null, [actor])!;
        try
        {
            motion._Process(0);
            AccessTools.Method(type, "OnLayout").Invoke(motion, [new Vector2(400f, 500f), 1]);
            var sandpit = (SandpitPower)ModelDb.Power<SandpitPower>().ToMutable();
            sandpit.Target = combat.Player.Creature;
            await PowerCmd.Apply(Choice, sandpit, combat.Enemy, 4, combat.Enemy, null);
            Vector2 nativePull = new(560f, 500f);
            actor.Position = nativePull;
            motion._Process(.25);
            Require(actor.Position.IsEqualApprox(nativePull),
                "Companion layout overwrote the native Sandpit pull.");
        }
        finally { actor.Free(); }
        GD.Print("PASS feedback: Sandpit keeps ownership of the player's position over a pending companion reflow.");
    }

    private async Task VerifyRepeatedStandaloneAttacks()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        var patch = new Harmony("NinjaSlayer.OrbContracts.StandaloneAttacks");
        _hurtRoom = new FreeInputContractRoom();
        var actor = new AimContractCreature { Position = new(200f, 500f) };
        var visuals = new AimContractVisuals();
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, combat.Player.Creature);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, visuals);
        actor.AddChild(visuals);
        _hurtRoom.AddChild(actor);
        ((List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(_hurtRoom)!).Add(actor);
        AimActors.Add(combat.Player.Creature, actor);
        patch.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        patch.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
        AddChild(_hurtRoom);
        var speed = SaveManager.Instance.PrefsSave.FastMode;
        try
        {
            foreach (var mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                Vector2 root = actor.Position, body = visuals.Position;
                var attacks = new List<Task>();
                for (int index = 0; index < 12; index++)
                {
                    attacks.Add(NinjaSlayer.Code.ExternalAnimations.FastAttackAnimation.Play(combat.Player.Creature, .1f));
                    await ToSignal(GetTree().CreateTimer(.03), SceneTreeTimer.SignalName.Timeout);
                }
                await Task.WhenAll(attacks);
                await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
                Require(actor.Position.DistanceTo(root) < .01f && visuals.Position.DistanceTo(body) < .01f,
                    $"Repeated native attack triggers drifted in {mode}: root {actor.Position - root}, body {visuals.Position - body}.");
            }
        }
        finally
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            _hurtRoom.Free();
            _hurtRoom = null;
            AimActors.Remove(combat.Player.Creature);
            patch.UnpatchAll(patch.Id);
        }
        GD.Print("PASS feedback: repeated standalone attacks return to the same body/root origin at Normal/Fast/Instant speed.");
    }

    private static void RejectForeignOrbSprite(OrbModel __instance)
    {
        if (__instance is ShurikenOrb)
            throw new InvalidOperationException("Foreign skin factory tried to load the default vanilla shuriken path.");
    }
}
