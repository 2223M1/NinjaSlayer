using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease103Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        var entranceProbe = new Harmony("NinjaSlayer.SmokeDriver.ReviveEntrance");
        var entranceMethod = typeof(NinjaSlayer.Code.ExternalAnimations.AncientEntranceAnimation).GetMethods()
            .Single(m => m.Name == "Play" && m.GetParameters().Length == 4);
        entranceProbe.Patch(entranceMethod, prefix: new HarmonyMethod(typeof(Release103EntranceProbe), nameof(Release103EntranceProbe.Prefix)));
        foreach (string source in new[] { "Decay", "enemy-unpowered", "enemy-attack" })
        {
            // Two real Player models in a rendered game, driven locally.
            // This fixture does not prove cross-client death/revival synchronization.
            var player = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
            var teammate = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            var run = RunState.CreateForNewRun([player, teammate], ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(),
                [], GameMode.Standard, 0, _configuration.Seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), true, false);
            AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(RunManager.Instance, null);
            await (Task)AccessTools.Method(typeof(NGame), "StartRun").Invoke(NGame.Instance, [run])!;
            await RunManager.Instance.EnterAct(0);
            await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "1.0.3 fixture did not start.");
            var combat = player.Creature.CombatState!;
            var room = NCombatRoom.Instance!;
            var node = room.GetCreatureNode(player.Creature)!;
            var choice = new BlockingPlayerChoiceContext();
            foreach (var enemy in combat.Enemies) { enemy.SetMaxHpInternal(10000); enemy.SetCurrentHpInternal(10000); }
            if (source == "Decay" && !ModelDb.Card<TornadoFist>().DynamicVars.ContainsKey("VulnerablePower"))
            {
                var tornado = combat.CreateCard<TornadoFist>(player);
                await CardPileCmd.Add(tornado, PileType.Hand);
                player.PlayerCombatState!.LoseEnergy(player.PlayerCombatState.Energy);
                player.PlayerCombatState.GainEnergy(4);
                await PowerCmd.Apply<VigorPower>(choice, player.Creature, 7, player.Creature, null);
                int before = combat.Enemies[0].CurrentHp;
                await CardCmd.AutoPlay(choice, tornado, null);
                Require(combat.Enemies[0].CurrentHp == before - 88, "Rendered Tornado did not resolve eight Vigor-enhanced hits.");
                _checkpoints.Write("release103.tornado");
            }
            foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
            player.Creature.LoseBlockInternal(player.Creature.Block);
            await CreatureCmd.SetCurrentHp(player.Creature, 1);
            await WaitFrames(5);
            var body = NinjaSlayerVisualRig.GetBodySprite(node.Visuals)!;
            Vector2 beforeDeath = node.GlobalPosition;
            Release103EntranceProbe.Count = 0;
            if (source == "Decay")
            {
                var curse = combat.CreateCard<Decay>(player);
                await CardPileCmd.Add(curse, PileType.Hand);
                await curse.OnTurnEndInHandWrapper(choice);
            }
            else if (source == "enemy-attack")
                await DamageCmd.Attack(2).FromMonster(combat.Enemies[0].Monster!).Execute(choice);
            else await CreatureCmd.Damage(choice, player.Creature, 2, ValueProp.Unpowered | ValueProp.Unblockable, combat.Enemies[0]);
            await WaitUntilAsync(() => node.DeathAnimationTask?.IsCompleted == true, "Non-attack death visuals did not finish.");
            Require(player.Creature.IsDead && teammate.Creature.IsAlive, "Fixture must retain a living teammate.");
            Require(source == "enemy-attack" || node.GlobalPosition.IsEqualApprox(beforeDeath) && body.GetParent().Name == "NinjaSlayerDeathJitter",
                "Non-attack death must use the suicide fall rather than a reverse-finisher flight.");
            _checkpoints.Write("release103.dead." + source);
            bool won = false;
            void OnWon(MegaCrit.Sts2.Core.Rooms.CombatRoom _) => won = true;
            CombatManager.Instance.CombatWon += OnWon;
            foreach (var enemy in combat.Enemies.ToArray())
                await CreatureCmd.Damage(choice, enemy, 100000, ValueProp.Move, teammate.Creature);
            await WaitUntilAsync(() => won && !CombatManager.Instance.IsInProgress && player.Creature.IsAlive,
                "Teammate victory did not finish settlement and revive NinjaSlayer.");
            CombatManager.Instance.CombatWon -= OnWon;
            await WaitFrames(150);
            Require(Release103EntranceProbe.Count == (source == "enemy-attack" ? 1 : 0),
                "Only flight death may restart the shared random entrance on revival.");
            Require(NinjaSlayerVisualRig.GetBodySprite(node.Visuals) == body && body.GetParent().Name != "NinjaSlayerDeathJitter",
                "Native combat-end revival left the suicide rig attached.");
            Require(body.GetParent().GetNode<NarakuVisualOverlay>("NarakuVisualOverlay").GetParent() == body.GetParent()
                && node.GlobalPosition.IsEqualApprox(beforeDeath)
                && node.Visuals.FindChild("NinjaSlayerDeathPivot", true, false) == null,
                "Revival must restore the body/overlay siblings, original position and remove the death pivot.");
            _checkpoints.Write("release103.settled." + source, data: new JsonObject { ["hp"] = player.Creature.CurrentHp });
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        entranceProbe.UnpatchAll(entranceProbe.Id);
        _checkpoints.Write("release103.completed");
        NGame.Instance!.Quit();
    }
}

internal static class Release103EntranceProbe
{
    internal static int Count;
    public static void Prefix() => Count++;
}
