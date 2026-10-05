using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease114FinishersAsync()
    {
        var registry = typeof(StormFist).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry", true)!;
        var active = AccessTools.Method(registry, "GetActiveSession");
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
        foreach (bool upgraded in new[] { false, true })
        foreach (CardModel canonical in new CardModel[] { ModelDb.Card<StormFist>(), ModelDb.Card<DragonRoundhouseKick>(),
                     ModelDb.Card<PalmThrust>(), ModelDb.Card<AntiAirBangBangFist>() })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
                ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
            await RunManager.Instance.EnterAct(0);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<GremlinMercNormal>().ToMutable());
            var player = run.Players[0];
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Finisher fixture did not start.");
            var combat = player.Creature.CombatState!;
            Require(combat.HittableEnemies.Count == 1, "Finisher fixture requires one initial target.");
            if (canonical is DragonRoundhouseKick)
                await CreatureCmd.Add(combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, null));
            var choice = new BlockingPlayerChoiceContext();
            foreach (var creature in combat.HittableEnemies.Append(player.Creature))
                foreach (var power in creature.Powers.ToArray()) await PowerCmd.Remove(power);
            foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(old);
            await CardPileCmd.Add(combat.CreateCard<Chado>(player), PileType.Exhaust);
            await CardPileCmd.Add(combat.CreateCard<Chado>(player), PileType.Exhaust);
            await PowerCmd.Apply<StrengthPower>(choice, player.Creature, 3, player.Creature, null);
            await PowerCmd.Apply<VigorPower>(choice, player.Creature, 7, player.Creature, null);
            var card = combat.CreateCard(canonical, player);
            if (upgraded) card.UpgradeInternal();
            await CardPileCmd.Add(card, PileType.Hand);
            player.PlayerCombatState!.GainEnergy(4);
            int hits = card is AntiAirBangBangFist ? 3 : card.DynamicVars.Repeat.IntValue;
            int damage = 10 + (card is StormFist ? (int)card.DynamicVars.CalculatedDamage.Calculate(combat.HittableEnemies[0])
                : card.DynamicVars.Damage.IntValue);
            foreach (var target in combat.HittableEnemies)
            {
                target.SetMaxHpInternal(1000);
                await CreatureCmd.SetCurrentHp(target, damage * hits - 2);
                await CreatureCmd.GainBlock(target, 2, ValueProp.Unpowered, null);
            }
            await WaitFrames(30);
            var actor = player.Creature.GetCreatureNode()!;
            Vector2 root = actor.Position;
            Transform2D baseline = actor.Visuals.Transform;
            var pose = actor.Visuals.GetNode<Node2D>("%AimPose");
            Vector2 Core() => actor.GetGlobalTransformWithCanvas().AffineInverse()
                * (Vector2)AccessTools.Property(pose.GetType(), "CoreCanvas").GetValue(pose)!;
            var retreats = new float[hits];
            var shownRetreats = new float[hits];
            var peaks = new Dictionary<int, Vector2>();
            bool early = false;
            float preZoom = 1f;
            FinisherSmokeObserver.Reset();
            var action = new PlayCardAction(card, card.TargetType == TargetType.AnyEnemy ? combat.HittableEnemies[0] : null);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!action.CompletionTask.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Multi-hit finisher did not finish.");
                if (active.Invoke(null, null) is { } session)
                {
                    Type type = session.GetType();
                    int calls = (int)AccessTools.Field(type, "_primaryDamageCalls").GetValue(session)!;
                    int resolved = (int)AccessTools.Property(type, "ResolvedHits").GetValue(session)!;
                    Require(resolved == hits, "Rendered finisher did not resolve the actual command's hit count.");
                    early |= calls == 0;
                    if (AccessTools.Field(type, "_impactStartedAt").GetValue(session) == null)
                    {
                        object camera = AccessTools.Field(type, "_camera").GetValue(session)!;
                        float scale = (float)AccessTools.Property(camera.GetType(), "CurrentScale").GetValue(camera)!;
                        Vector2 baseScale = (Vector2)AccessTools.Property(camera.GetType(), "BaselineScale").GetValue(camera)!;
                        preZoom = Math.Max(preZoom, scale / baseScale.X);
                    }
                    if (calls > 0 && calls < hits)
                    {
                        Vector2 travel = (Vector2)AccessTools.Field(type, "_comboTravel").GetValue(session)!;
                        peaks.TryAdd(calls, Core());
                        retreats[calls] = Math.Max(retreats[calls], travel.Length());
                        shownRetreats[calls] = Math.Max(shownRetreats[calls], Core().DistanceTo(peaks[calls]));
                    }
                    Require(actor.Position.IsEqualApprox(root), "Finisher moved the combat UI root.");
                }
                await WaitFrames(1);
            }
            await action.CompletionTask;
            if (action.Exception != null) throw action.Exception;
            var observed = FinisherSmokeObserver.Snapshots().Single();
            Require(early && observed.ResolvedHits == hits && observed.CompletionObserved && observed.ResourcesReleased
                && observed.CompletionFailure == null, "Combo only entered finisher on its last hit or failed to release.");
            Require(preZoom > 1.01f && preZoom <= 1.3501f, "Combo lost its early 1.35x stage.");
            float distance = card is StormFist or DragonRoundhouseKick ? NinjaSlayerCombatVisuals.SlowAttackLungeDistance
                : NinjaSlayerCombatVisuals.AttackLungeDistance;
            for (int hit = 1; hit < hits; hit++)
                Require(retreats[hit] >= distance * .8f && shownRetreats[hit] >= distance * .5f,
                    $"{card.Id} {speed} upgraded={upgraded} hit={hit}: combo remained attached, travel={retreats[hit]}, rendered={shownRetreats[hit]}, distance={distance}.");
            await WaitFrames(30);
            Require(actor.Position.IsEqualApprox(root) && actor.Visuals.Transform.IsEqualApprox(baseline),
                "Finisher failed to restore the character/UI baseline.");
            _checkpoints.Write("release114.finisher", data: new JsonObject { ["card"] = card.Id.ToString(),
                ["speed"] = speed.ToString(), ["upgraded"] = upgraded, ["hits"] = hits, ["early"] = early,
                ["preZoom"] = preZoom, ["retreatPixels"] = new JsonArray(retreats.Skip(1).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["renderedRetreatPixels"] = new JsonArray(shownRetreats.Skip(1).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) });
            await NGame.Instance.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
    }
}
