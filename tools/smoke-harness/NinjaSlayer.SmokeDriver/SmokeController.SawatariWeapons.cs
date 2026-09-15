using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifySawatariWeaponPresentation(string directory, CancellationToken ct)
    {
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(combat.RunState)!;
        var room = NCombatRoom.Instance!;
        var choice = new BlockingPlayerChoiceContext();
        player.Creature.SetMaxHpInternal(1000);
        await CreatureCmd.SetCurrentHp(player.Creature, 1000);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;

        if (_configuration.PreviewFormFinisher == "SawatariEvent")
        {
            foreach (Creature enemy in combat.HittableEnemies.ToArray())
                await CreatureCmd.Kill(enemy, force: true);
            await VerifySawatariThirdActEvent(ct);
            return;
        }

        async Task<SawatariMonster> Replace(bool third)
        {
            Creature[] previous = combat.HittableEnemies.ToArray();
            Vector2 position = room.GetCreatureNode(previous[0])!.Position;
            var model = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            model.ActThree = third;
            Creature creature = combat.CreateCreature(model, CombatSide.Enemy, null);
            await CreatureCmd.Add(creature);
            foreach (Creature old in previous)
            {
                var node = room.GetCreatureNode(old)!;
                room.RemoveCreatureNode(node);
                node.QueueFree();
                CombatManager.Instance.RemoveCreature(old);
                if (combat.ContainsCreature(old)) combat.RemoveCreature(old);
            }
            room.GetCreatureNode(creature)!.Position = position;
            creature.SetMaxHpInternal(1000);
            await CreatureCmd.SetCurrentHp(creature, 1000);
            model.RollMove(combat.PlayerCreatures);
            await WaitFrames(30);
            return model;
        }
        async Task Move(SawatariMonster model)
        {
            await model.PerformMove();
            model.RollMove(combat.PlayerCreatures);
            Node visual = room.GetCreatureNode(model.Creature)!.Visuals.GetNode("SawatariWeapons");
            AccessTools.Method(visual.GetType(), "Refresh").Invoke(visual, [null]);
            await WaitFrames(30);
        }
        async Task Capture(string stage)
        {
            await WaitFrames(30);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, stage + ".png"));
            _checkpoints.Write("sawatari.weapons." + stage);
        }

        if (IsBladeFeedbackPreview)
        {
            await VerifyBladeFeedback(directory, Replace);
            return;
        }

        if (_configuration.PreviewFormFinisher == "SawatariMotion")
        {
            await VerifySawatariMotion(directory, Replace);
            return;
        }

        if (_configuration.PreviewFormFinisher == "SawatariRig")
        {
            var model = await Replace(true);
            var actor = room.GetCreatureNode(model.Creature)!;
            Node visual = actor.Visuals.GetNode("SawatariWeapons");
            var refresh = AccessTools.Method(visual.GetType(), "Refresh");
            var body = actor.Visuals.GetNode<Sprite2D>("%Visuals");
            var weapons = body.GetNode<Node2D>("WeaponRig");
            var hands = weapons.GetChildren().OfType<Node2D>().Take(2).ToArray();
            var fist = weapons.GetNode<Sprite2D>("InnerFist");
            Require(hands[0].ZIndex == 10 && hands[1].ZIndex == 20 && fist.ZIndex == 30,
                "Accepted weapon/fist layer order was not preserved.");
            Vector2 bodyPosition = body.Position;
            Vector2 foot = body.ToGlobal(new Vector2(-20.5f, 259.5f));
            Require(foot.DistanceTo(actor.Visuals.GetNode<Node2D>("GroundContact").GlobalPosition) < .1f,
                "New body is not aligned to the existing ground contact.");
            foreach (int mask in new[] { 3, 1, 2, 0 })
            {
                AccessTools.Property(typeof(SawatariMonster), "HeldMachetes").SetValue(model, mask);
                foreach (bool raised in new[] { true, false })
                {
                    refresh.Invoke(visual, [raised]);
                    for (int frame = 0; frame < 16; frame++)
                    {
                        await WaitFrames(1);
                        Require(body.Position.IsEqualApprox(bodyPosition)
                            && hands[0].ZIndex == 10 && hands[1].ZIndex == 20 && fist.ZIndex == 30,
                            "Weapon rotation changed the body position or fixed layer order.");
                    }
                    Require(hands[0].Visible == ((mask & 1) != 0)
                        && hands[1].Visible == ((mask & 2) != 0) && fist.Visible,
                        "Weapon ownership hid the wrong knife or removed the inner fist.");
                    await Capture($"rig-{mask}-{(raised ? "upright" : "horizontal")}");
                }
            }
            AccessTools.Property(typeof(SawatariMonster), "HeldMachetes").SetValue(model, 3);
            body.FlipH = !body.FlipH;
            refresh.Invoke(visual, [true]);
            await WaitFrames(20);
            Vector2 mirroredFoot = body.ToGlobal(new Vector2(20.5f, 259.5f));
            Require(mirroredFoot.DistanceTo(actor.Visuals.GetNode<Node2D>("GroundContact").GlobalPosition) < .1f,
                "Mirroring the accepted body moved its foot off the ground contact.");
            await Capture("rig-mirrored");
            body.FlipH = !body.FlipH;
            refresh.Invoke(visual, [false]);
            await WaitFrames(20);

            var anchor = actor.Visuals.GetNode<Node2D>("AirborneAnchor");
            Transform2D restingTransform = anchor.Transform;
            Vector2 restingCenter = actor.Visuals.VfxSpawnPosition.Position;
            Vector2 intentPosition = actor.Visuals.GetNode<Node2D>("%IntentPos").Position;
            Vector2 intentDisplay = actor.IntentContainer.Position;
            int initialVfx = room.CombatVfxContainer.GetChildCount();
            bool bodyMoved = false;
            bool capturedRelease = false;
            Task throwing = (Task)AccessTools.Method(visual.GetType(), "PlayThrow")
                .Invoke(null, [model, player.Creature, 0])!;
            while (!throwing.IsCompleted)
            {
                await WaitFrames(1);
                bodyMoved |= !anchor.Transform.IsEqualApprox(restingTransform);
                Require(actor.Visuals.GetNode<Node2D>("%IntentPos").Position.IsEqualApprox(intentPosition)
                    && actor.IntentContainer.Position.IsEqualApprox(intentDisplay),
                    "Throwing moved the fixed intent position.");
                if (!capturedRelease && !hands[0].Visible)
                {
                    _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "rig-throw-release.png"));
                    capturedRelease = true;
                }
            }
            await throwing;
            await WaitFrames(2);
            Require(bodyMoved && anchor.Transform.IsEqualApprox(restingTransform)
                && actor.Visuals.VfxSpawnPosition.Position.IsEqualApprox(restingCenter),
                "Throwing did not animate the body or failed to restore its resting pose.");
            Require(room.CombatVfxContainer.GetChildCount() == initialVfx,
                "The native thrown knife remained after landing.");
            refresh.Invoke(visual, [false]);
            await Capture("rig-throw-restored");
            _checkpoints.Write("sawatari.accepted-rig-completed");
            return;
        }

        var arrow = await Replace(false);
        var archer = room.GetCreatureNode(arrow.Creature)!;
        var bowVisual = archer.Visuals.GetNode("SawatariWeapons");
        var bowBody = archer.Visuals.GetNode<Sprite2D>("%Visuals");
        var showBow = AccessTools.Method(bowVisual.GetType(), "ShowBow");
        Vector2 restingBowPosition = default;
        foreach (bool mirrored in new[] { false, true })
        {
            bowBody.FlipH = mirrored;
            foreach (bool nocked in new[] { true, false })
            {
                showBow.Invoke(bowVisual, [nocked]);
                Vector2 foot = bowBody.ToGlobal(new Vector2(mirrored ? 20.5f : -20.5f, 259.5f));
                Require(bowBody.Texture.GetSize() == new Vector2(461, 537)
                    && foot.DistanceTo(archer.Visuals.GetNode<Node2D>("GroundContact").GlobalPosition) < .1f,
                    "Hatted bow state changed canvas or foot alignment.");
                if (nocked) restingBowPosition = bowBody.Position;
                else Require(bowBody.Position.IsEqualApprox(restingBowPosition),
                    "Releasing the arrow moved the hatted body.");
                await Capture($"bow-{(mirrored ? "mirrored" : "enemy")}-{(nocked ? "nocked" : "released")}");
            }
        }
        bowBody.FlipH = false;
        await arrow.AfterSideTurnStart(CombatSide.Player, combat.PlayerCreatures, combat);
        await Capture("act1-before-arrow");
        var arrowNode = bowBody.GetNode<Sprite2D>("WeaponRig/BowAndArrow/Arrow");
        bool capturedArrow = false;
        int releaseFrames = 0;
        Task arrowMove = Move(arrow);
        while (!arrowMove.IsCompleted)
        {
            await WaitFrames(1);
            if (!capturedArrow && GodotObject.IsInstanceValid(arrowNode) && arrowNode.GetParent() == room.CombatVfxContainer
                && ++releaseFrames == 3)
            {
                _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "act1-arrow-flight.png"));
                capturedArrow = true;
            }
        }
        await arrowMove;
        Require(arrow.Creature.GetPowerAmount<StrengthPower>() == 2,
            "Act-one arrow did not finish with two Strength.");
        Require(capturedArrow && bowBody.Texture.ResourcePath == SawatariMonster.TexturePath,
            "The real opening move did not release the arrow and restore bamboo.");
        Require(bowBody.ToGlobal(new Vector2(-20.5f, 259.5f))
            .DistanceTo(archer.Visuals.GetNode<Node2D>("GroundContact").GlobalPosition) < .1f,
            "Switching from the hatted bow to bamboo moved the shared foot anchor.");
        await Capture("act1-after-arrow");
        await Move(arrow);
        if (_configuration.PreviewFormFinisher == "SawatariBow")
        {
            _checkpoints.Write("sawatari.accepted-bow-completed");
            return;
        }

        var dual = await Replace(true);
        var enemyNode = room.GetCreatureNode(dual.Creature)!;
        Vector2 root = enemyNode.Position;
        Vector2 intent = enemyNode.Visuals.GetNode<Node2D>("%IntentPos").Position;
        await Capture("act3-dual-ready");
        await Move(dual);
        Require(dual.NextMove.Id == SawatariMonster.ThrowMoveId, "Dual attack did not schedule a throw.");
        await Capture("act3-throw-ready");
        foreach (CardModel card in PileType.Hand.GetPile(player).Cards.ToArray())
            await CardPileCmd.Add(card, PileType.Discard);
        var originalKnives = enemyNode.Visuals.GetNode<Sprite2D>("%Visuals").GetNode("WeaponRig")
            .GetChildren().OfType<Node2D>().Take(2).Select(hand => hand.GetChild<Sprite2D>(0)).ToArray();
        await Move(dual);
        Require(dual.MacheteCount == 1, "First visual throw did not remove one weapon.");
        Require(originalKnives[0].GetParent().Name == "Primary", "First throw replaced the held knife instead of transferring it.");
        await Capture("act3-one-each");
        await Move(dual);
        var cards = PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().ToArray();
        Require(cards.Length == 2 && dual.MacheteCount == 0, "Second visual throw did not transfer the other weapon.");
        Require(originalKnives[1].GetParent().Name == "Secondary", "Second throw replaced the other held knife.");
        Require(player.Creature.GetCreatureNode()!.Visuals.FindChild("PlayerMachetes", true, false) is Node2D { Visible: true },
            "Player hand cards did not create held-weapon visuals.");
        await Capture("act3-player-dual-bamboo-ready");
        await Move(dual);
        await CardCmd.AutoPlay(choice, cards[0], dual.Creature);
        Require(originalKnives[0].GetParent().GetParent().Name == "WeaponRig", "Playing Machete did not return the same sprite.");
        await Capture("act3-return-one");
        Require(dual.NextMove.Id == SawatariMonster.ThrowMoveId, "One returned weapon did not select throwing.");
        await CardCmd.AutoPlay(choice, cards[1], dual.Creature);
        Require(originalKnives[1].GetParent().GetParent().Name == "WeaponRig", "Second Machete did not return its sprite.");
        await Capture("act3-return-two");
        Require(dual.NextMove.Id == SawatariMonster.DualMoveId && dual.MacheteCount == 2,
            "Two returned weapons did not select the dual attack.");
        Require(enemyNode.Position.IsEqualApprox(root)
            && enemyNode.Visuals.GetNode<Node2D>("%IntentPos").Position.IsEqualApprox(intent),
            "Weapon actions moved the creature root or intent anchor.");
        await VerifyPlayerWeaponForms(directory);
        await Move(dual);
        await Move(dual);
        await CreatureCmd.Kill(dual.Creature, force: true);
        await WaitFrames(45);

        await VerifySawatariThirdActEvent(ct);
        _checkpoints.Write("sawatari.weapons-and-act3-event-completed");
    }

    private async Task VerifySawatariThirdActEvent(CancellationToken ct)
    {
        // Exercise the console sequence delivered to players, including a normal encounter to assist.
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var act = new ActConsoleCmd().Process(player, ["3"]);
        Require(act.success, act.msg);
        await act.task!;
        var room = new RoomConsoleCmd().Process(player, ["Monster"]);
        Require(room.success, room.msg);
        await room.task!;
        var win = new WinConsoleCmd().Process(player, []);
        Require(win.success, win.msg);
        await win.task!;
        var entry = new EventConsoleCmd().Process(player, [ModelDb.Event<SawatariEvent>().Id.Entry]);
        Require(entry.success, entry.msg);
        await entry.task!;
        // Console-selected encounters are not the fixed Finisher fixture.
        // The fixed-encounter SawatariSameCombat suite checks Finisher ownership separately.
        await VerifySawatariEventCombat(ct, verifyFinisher: false);
        _checkpoints.Write("sawatari.act3-event-completed");
    }
}
