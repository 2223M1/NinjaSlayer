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
using MegaCrit.Sts2.Core.Models.Cards;
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
        var statusChoice = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
        await PowerCmd.Apply<NinjaSlayer.Powers.DevourFlamePower>(statusChoice, player.Creature, 5, player.Creature, null);
        foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
            await NinjaSlayer.Code.Commands.NinjaSlayerCardCmd.AddGeneratedCard<Wound>(player, pile);
        Require(player.Creature.GetPowerAmount<NinjaSlayer.Powers.NarakuLifePower>() == 15,
            "Rendered status generation must grant Naraku Life once per card.");
        foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
            await CardPileCmd.RemoveFromCombat(old);
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        var selected = combat.CreateCard<DefendIronclad>(player);
        var rekindle = combat.CreateCard<Rekindle>(player);
        await CardPileCmd.Add(selected, PileType.Hand);
        await CardPileCmd.Add(rekindle, PileType.Hand);
        player.PlayerCombatState!.GainEnergy(3);
        var statusSelector = new MegaCrit.Sts2.Core.TestSupport.TestCardSelector();
        statusSelector.PrepareToSelect([0]);
        using (CardSelectCmd.UseSelector(statusSelector))
        {
            var action = new PlayCardAction(rekindle, null);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
            if (action.Exception != null) throw action.Exception;
        }
        Require(!player.Creature.HasPower<NinjaSlayer.Powers.NarakuLifePower>(), "Rekindle must not reward itself.");
        var followupSkill = combat.CreateCard<DefendIronclad>(player);
        await CardPileCmd.Add(followupSkill, PileType.Hand);
        var followup = new PlayCardAction(followupSkill, null);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(followup);
        await followup.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
        if (followup.Exception != null) throw followup.Exception;
        Require(player.Creature.GetPowerAmount<NinjaSlayer.Powers.NarakuLifePower>() == 3,
            "A subsequent rendered Skill must grant Rekindle Naraku Life.");
        _checkpoints.Write("release111.status-life", data: new JsonObject { ["generatedStatuses"] = 3,
            ["narakuFromGeneration"] = 15, ["rekindleSelfReward"] = 0, ["followingSkillReward"] = 3 });
        foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
            await CardPileCmd.RemoveFromCombat(old);
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        await CardPileCmd.Add(combat.CreateCard<Chado>(player), PileType.Exhaust);
        await CardPileCmd.Add(combat.CreateCard<Chado>(player), PileType.Exhaust);
        await PowerCmd.Apply<StrengthPower>(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
            player.Creature, 3, player.Creature, null);
        await WaitFrames(45);
        var actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var baselineRoot = actor.GetGlobalTransform();
        var baselineVisuals = actor.Visuals.Transform;
        Node2D aimPose = actor.Visuals.GetNode<Node2D>("%AimPose");
        var baselinePose = aimPose.Transform;
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
            foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            foreach (var model in new CardModel[] { ModelDb.Card<StormFist>(), ModelDb.Card<DragonRoundhouseKick>(),
                         ModelDb.Card<PalmThrust>(), ModelDb.Card<PressTheAttack>(), ModelDb.Card<Endurance>(), ModelDb.Card<AntiAirBangBangFist>(), ModelDb.Card<TornadoFist>() })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                foreach (var old in player.Piles.Where(p => p.Type is PileType.Hand or PileType.Draw or PileType.Discard)
                             .SelectMany(p => p.Cards).ToArray())
                    await CardPileCmd.RemoveFromCombat(old);
                CardModel card = combat.CreateCard(model, player);
                if (upgraded) card.UpgradeInternal();
                await CardPileCmd.Add(card, PileType.Hand);
                player.PlayerCombatState!.LoseEnergy(player.PlayerCombatState.Energy);
                player.PlayerCombatState.GainEnergy(4);
                if (player.Creature.GetPower<NinjaSlayer.Powers.KaratePower>() is { } karate)
                    await PowerCmd.Remove(karate);
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
                    PalmThrust => upgraded ? 3 : 2, PressTheAttack => 1, Endurance => 3, AntiAirBangBangFist => 3, TornadoFist => 8, _ => throw new InvalidOperationException() };
                int damage = 10 + (card switch { StormFist => upgraded ? 14 : 10, DragonRoundhouseKick => upgraded ? 9 : 7,
                    PalmThrust => 6, PressTheAttack => upgraded ? 10 : 7, Endurance => upgraded ? 5 : 4, AntiAirBangBangFist => upgraded ? 11 : 8, TornadoFist => upgraded ? 6 : 4,
                    _ => throw new InvalidOperationException() });
                int targets = card.TargetType == TargetType.AllEnemies ? combat.HittableEnemies.Count : 1;
                Require(MultiHitVigorProbe.Commands.Count == 1 && hits.Length == expectedHits
                    && hits.All(h => h.Count == targets && h.All(r => r.TotalDamage == damage))
                    && !player.Creature.HasPower<VigorPower>(), $"Rendered multi-hit damage/Vigor differs: {card.Id}, upgraded={upgraded}, speed={speed}, commands={MultiHitVigorProbe.Commands.Count}, hits={hits.Length}/{expectedHits}, damage={string.Join(";", hits.Select(h => string.Join(",", h.Select(r => r.TotalDamage))))}/{damage}, vigor={player.Creature.GetPowerAmount<VigorPower>()}, hand={PileType.Hand.GetPile(player).Cards.Count}.");
                Require(MultiHitVigorProbe.AnimationGates == expectedHits
                    && MultiHitVigorProbe.DamageGates.SequenceEqual(Enumerable.Range(1, expectedHits).SelectMany(i => Enumerable.Repeat(i, targets))),
                    "Damage did not follow exactly one completed animation hit gate per native hit.");
                if (card is Endurance)
                    Require(player.Creature.GetPowerAmount<NinjaSlayer.Powers.KaratePower>() == (upgraded ? 5 : 4)
                        && !player.Creature.HasPower<NinjaSlayer.Powers.EndurancePower>(), "Endurance must grant Karate after its hits without its former delayed power.");
                if (card is PalmThrust or Endurance && speed != FastModeType.Instant)
                {
                    // The native card action has completed; its visible tail must
                    // still move for longer than the former 0.1s return.
                    await _tree.ToSignal(_tree.CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
                    Require(!aimPose.Transform.IsEqualApprox(baselinePose), "The settled attack snapped to idle before its vanilla visual tail finished.");
                    _checkpoints.Write("attack-tail.after-settlement", data: new JsonObject {
                        ["card"] = card.Id.ToString(), ["speed"] = speed.ToString(), ["upgraded"] = upgraded });
                }
                await WaitUntilAsync(() => actor.GetGlobalTransform().IsEqualApprox(baselineRoot)
                    && actor.Visuals.Transform.IsEqualApprox(baselineVisuals) && aimPose.Transform.IsEqualApprox(baselinePose),
                    "Attack visual tail did not return to idle.");
                Require(actor.GetGlobalTransform().IsEqualApprox(baselineRoot) && actor.Visuals.Transform.IsEqualApprox(baselineVisuals),
                    "Multi-hit attack did not restore the body/root pose.");
                _checkpoints.Write("multihit.resolved", data: new JsonObject { ["card"] = card.Id.ToString(),
                    ["upgraded"] = upgraded, ["speed"] = speed.ToString(), ["hits"] = hits.Length,
                    ["targetsPerHit"] = targets, ["damagePerTarget"] = damage, ["animationGates"] = MultiHitVigorProbe.AnimationGates });
            }
            _checkpoints.Write("multihit.completed");
            MultiHitVigorProbe.Card = null;
            foreach (bool upgraded in new[] { false, true })
            foreach (bool hasTea in new[] { false, true })
            foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                    await CardPileCmd.RemoveFromCombat(old);
                var tea = hasTea ? combat.CreateCard<Chado>(player) : null;
                if (tea != null) await CardPileCmd.Add(tea, PileType.Hand);
                var drawnTea = combat.CreateCard<Chado>(player);
                await CardPileCmd.Add(drawnTea, PileType.Draw);
                for (int i = 0; i < 15; i++) await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player), PileType.Draw);
                var kick = combat.CreateCard<DragonFlyingKick>(player);
                if (upgraded) kick.UpgradeInternal();
                await CardPileCmd.Add(kick, PileType.Hand);
                player.PlayerCombatState!.GainEnergy(3);
                var action = new PlayCardAction(kick, combat.HittableEnemies[0]);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
                if (action.Exception != null) throw action.Exception;
                var hand = PileType.Hand.GetPile(player).Cards;
                var breathedTea = tea ?? hand.OfType<Chado>().Single(c => c != drawnTea);
                Require(hand.Count == CardPile.MaxCardsInHand && breathedTea.Pile?.Type == PileType.Hand
                    && breathedTea.DynamicVars.Energy.BaseValue == (upgraded ? 3 : 2) + (hasTea ? 1 : 0)
                    && drawnTea.Pile?.Type == PileType.Hand && drawnTea.DynamicVars.Energy.BaseValue == 1
                    && kick.Pile?.Type == PileType.Exhaust,
                    "Dragon Flying Kick must breathe before drawing to capacity; newly drawn Chado must remain unchanged.");
                _checkpoints.Write("dragon-kick.breath-before-draw", data: new JsonObject {
                    ["upgraded"] = upgraded, ["hadTea"] = hasTea, ["speed"] = speed.ToString(),
                    ["handCount"] = hand.Count, ["breathedEnergy"] = breathedTea.DynamicVars.Energy.IntValue,
                    ["drawnTeaEnergy"] = drawnTea.DynamicVars.Energy.IntValue });
            }
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
