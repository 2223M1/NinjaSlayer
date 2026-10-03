using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunMultiHitVigorAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Multi-hit fixture did not start.");
        var combat = player.Creature.CombatState!;
        if (combat.HittableEnemies.Count < 2)
            await CreatureCmd.Add(combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, null));
        foreach (var enemy in combat.HittableEnemies)
        {
            enemy.SetMaxHpInternal(10000);
            enemy.SetCurrentHpInternal(10000);
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        }
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        foreach (var card in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
            await CardPileCmd.RemoveFromCombat(card);
        await CardPileCmd.Add(combat.CreateCard<Chado>(player), PileType.Exhaust);
        await CardPileCmd.Add(combat.CreateCard<Chado>(player), PileType.Exhaust);
        await PowerCmd.Apply<StrengthPower>(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
            player.Creature, 3, player.Creature, null);
        await WaitFrames(45);
        var actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var baselineRoot = actor.GetGlobalTransform();
        var baselineVisuals = actor.Visuals.Transform;
        var observer = new Harmony("NinjaSlayer.SmokeDriver.MultiHitVigor");
        observer.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.BeforeAttack)),
            prefix: new HarmonyMethod(typeof(MultiHitVigorProbe), nameof(MultiHitVigorProbe.BeforeAttack)));
        observer.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim)),
            postfix: new HarmonyMethod(typeof(MultiHitVigorProbe), nameof(MultiHitVigorProbe.AfterAnimation)));
        observer.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterDamageReceived)),
            prefix: new HarmonyMethod(typeof(MultiHitVigorProbe), nameof(MultiHitVigorProbe.Damage)));
        try
        {
            foreach (bool upgraded in new[] { false, true })
            foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast })
            foreach (var model in new CardModel[] { ModelDb.Card<StormFist>(), ModelDb.Card<DragonRoundhouseKick>(),
                         ModelDb.Card<PalmThrust>(), ModelDb.Card<AntiAirBangBangFist>(), ModelDb.Card<TornadoFist>() })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                CardModel card = combat.CreateCard(model, player);
                if (upgraded) card.UpgradeInternal();
                await CardPileCmd.Add(card, PileType.Hand);
                player.PlayerCombatState!.LoseEnergy(player.PlayerCombatState.Energy);
                player.PlayerCombatState.GainEnergy(4);
                await PowerCmd.Apply<VigorPower>(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
                    player.Creature, 7, player.Creature, null);
                MultiHitVigorProbe.Card = card;
                MultiHitVigorProbe.Commands.Clear();
                MultiHitVigorProbe.AnimationGates = 0;
                MultiHitVigorProbe.DamageGates.Clear();
                var action = new PlayCardAction(card, card.TargetType == TargetType.AnyEnemy ? combat.HittableEnemies[0] : null);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
                if (action.Exception != null) throw action.Exception;
                var hits = MultiHitVigorProbe.Commands.SelectMany(a => a.Results).ToArray();
                int expectedHits = card switch { StormFist => 4, DragonRoundhouseKick => 2,
                    PalmThrust => upgraded ? 3 : 2, AntiAirBangBangFist => 3, TornadoFist => 8, _ => throw new InvalidOperationException() };
                int damage = 10 + (card switch { StormFist => upgraded ? 14 : 10, DragonRoundhouseKick => upgraded ? 9 : 7,
                    PalmThrust => 5, AntiAirBangBangFist => upgraded ? 11 : 8, TornadoFist => upgraded ? 6 : 4,
                    _ => throw new InvalidOperationException() });
                int targets = card.TargetType == TargetType.AllEnemies ? combat.HittableEnemies.Count : 1;
                Require(MultiHitVigorProbe.Commands.Count == 1 && hits.Length == expectedHits
                    && hits.All(h => h.Count == targets && h.All(r => r.TotalDamage == damage))
                    && !player.Creature.HasPower<VigorPower>(), "Rendered multi-hit damage/Vigor differs from native semantics.");
                Require(MultiHitVigorProbe.AnimationGates == expectedHits
                    && MultiHitVigorProbe.DamageGates.SequenceEqual(Enumerable.Range(1, expectedHits).SelectMany(i => Enumerable.Repeat(i, targets))),
                    "Damage did not follow exactly one completed animation hit gate per native hit.");
                await WaitFrames(45);
                Require(actor.GetGlobalTransform().IsEqualApprox(baselineRoot) && actor.Visuals.Transform.IsEqualApprox(baselineVisuals),
                    "Multi-hit attack did not restore the body/root pose.");
                _checkpoints.Write("multihit.resolved", data: new JsonObject { ["card"] = card.Id.ToString(),
                    ["upgraded"] = upgraded, ["speed"] = speed.ToString(), ["hits"] = hits.Length,
                    ["targetsPerHit"] = targets, ["damagePerTarget"] = damage, ["animationGates"] = MultiHitVigorProbe.AnimationGates });
            }
            _checkpoints.Write("multihit.completed");
        }
        finally { MultiHitVigorProbe.Card = null; observer.UnpatchAll(observer.Id); }
        NGame.Instance!.Quit();
    }
}

internal static class MultiHitVigorProbe
{
    internal static CardModel? Card;
    internal static readonly List<AttackCommand> Commands = [];
    internal static int AnimationGates;
    internal static readonly List<int> DamageGates = [];
    internal static void BeforeAttack(AttackCommand __1)
    {
        if (ReferenceEquals(__1.ModelSource, Card)) Commands.Add(__1);
    }
    internal static void AfterAnimation(Creature __0, string __1, ref Task __result)
    {
        if (Card != null && ReferenceEquals(__0, Card.Owner.Creature) && __1 is "Attack" or "SlowAttack" or "TornadoFistSpin")
            __result = Complete(__result);
        static async Task Complete(Task original) { await original; AnimationGates++; }
    }
    internal static void Damage(CardModel? cardSource)
    {
        if (Card != null && ReferenceEquals(cardSource, Card)) DamageGates.Add(AnimationGates);
    }
}
