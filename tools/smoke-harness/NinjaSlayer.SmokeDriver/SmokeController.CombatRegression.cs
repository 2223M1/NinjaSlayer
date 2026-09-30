using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunCombatRegressionAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        var point = run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster);
        await RunManager.Instance.EnterMapCoord(point.coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Combat regression did not reach the player turn", CancellationToken.None);
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        foreach (var enemy in combat.HittableEnemies)
        {
            enemy.SetMaxHpInternal(1000);
            enemy.SetCurrentHpInternal(1000);
        }
        await RelicCmd.Obtain<ToriiPactRelic>(player);
        var pet = await PlayerCmd.AddPet<YukanoMonster>(player);
        var relic = player.GetRelic<ToriiPactRelic>()!;
        var product = typeof(YukanoMonster).Assembly;
        var observer = new Harmony("NinjaSlayer.SmokeDriver.CombatRegression");
        var events = new JsonArray();
        CombatRegressionProbe.Record = (name, detail) => events.Add(new JsonObject
        {
            ["event"] = name, ["detail"] = detail, ["frame"] = Engine.GetFramesDrawn(),
            ["timeUsec"] = Time.GetTicksUsec()
        });
        var probe = typeof(CombatRegressionProbe);
        void Observe(Type type, string method, string prefix) => observer.Patch(AccessTools.Method(type, method),
            prefix: new HarmonyMethod(probe, prefix));
        Observe(typeof(YukanoMonster), "ArrowMove", nameof(CombatRegressionProbe.Action));
        Observe(product.GetType("NinjaSlayer.Code.ExternalAnimations.YukanoCombatAnimations", true)!,
            "PlayProjectile", nameof(CombatRegressionProbe.Projectile));
        Observe(product.GetType("NinjaSlayer.Code.ExternalAnimations.YukanoArrowPopup", true)!,
            "BeforeDraw", nameof(CombatRegressionProbe.Film));
        observer.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.LoseHpInternal)),
            postfix: new HarmonyMethod(probe, nameof(CombatRegressionProbe.Damage)));
        try
        {
            foreach (string mode in new[] { "first-popup", "ordinary", "popup-fast", "popup-pause", "fallback-before", "fallback-after", "cancel-before", "target-invalid" })
            {
                events.Clear();
                if (mode != "ordinary")
                {
                    var popupType = product.GetType("NinjaSlayer.Code.ExternalAnimations.YukanoArrowPopup", true)!;
                    object slot = AccessTools.Field(popupType, "_runData").GetValue(null)!;
                    object data = AccessTools.Method(slot.GetType(), "Get").Invoke(slot, [run])!;
                    AccessTools.Property(data.GetType(), "Shown").SetValue(data, false);
                }
                SaveManager.Instance.PrefsSave.FastMode = mode == "popup-fast" ? FastModeType.Fast : FastModeType.Normal;
                AccessTools.Method(typeof(YukanoMonster), "SetOpeningMove").Invoke(pet.Monster, [false]);
                Task attack = relic.AfterPlayerTurnStart(new BlockingPlayerChoiceContext(), player);
                Creature? invalidatedTarget = null;
                if (mode is "popup-pause" or "fallback-before" or "fallback-after" or "cancel-before" or "target-invalid")
                {
                    Node? popup = null;
                    await WaitUntilAsync(() => (popup = NCombatRoom.Instance!.GetNodeOrNull<Node>("YukanoArrowPopup")) != null,
                        "Expected the first-use movie", CancellationToken.None);
                    Type type = popup!.GetType();
                    if (mode == "fallback-after")
                        await WaitUntilAsync(() => (bool)AccessTools.Field(type, "_released").GetValue(popup)!,
                            "Movie did not release its arrow", CancellationToken.None);
                    if (mode == "popup-pause")
                    {
                        AccessTools.Property(typeof(CombatManager), "IsPaused").SetValue(CombatManager.Instance, true);
                        try
                        {
                            await WaitFrames(2);
                            double before = (double)AccessTools.Property(type, "PlaybackPosition").GetValue(popup)!;
                            await WaitFrames(20);
                            Require((double)AccessTools.Property(type, "PlaybackPosition").GetValue(popup)! == before,
                                "Movie release clock advanced while paused");
                        }
                        finally { AccessTools.Property(typeof(CombatManager), "IsPaused").SetValue(CombatManager.Instance, false); }
                    }
                    else if (mode == "cancel-before")
                        AccessTools.Method(type, "Cancel").Invoke(popup, null);
                    else if (mode == "target-invalid")
                    {
                        invalidatedTarget = (Creature)AccessTools.Field(type, "_target").GetValue(popup)!;
                        invalidatedTarget.SetCurrentHpInternal(0);
                    }
                    else
                    {
                        var result = type.GetNestedType("LaunchResult", BindingFlags.NonPublic)!;
                        AccessTools.Method(type, "Stop").Invoke(popup, [Enum.Parse(result, "Fallback")]);
                    }
                }
                await attack;
                await WaitFrames(90);
                invalidatedTarget?.SetCurrentHpInternal(1000);
                _checkpoints.Write("combat-regression.arrow", data: new JsonObject
                {
                    ["mode"] = mode, ["events"] = events.DeepClone(),
                    ["damageCount"] = events.Count(e => e!["event"]!.GetValue<string>() == "damage")
                });
                int expected = mode is "cancel-before" or "target-invalid" ? 0 : 1;
                Require(events.Count(e => e!["event"]!.GetValue<string>() == "projectile") == expected,
                    mode + $": expected {expected} projectile(s)");
                Require(events.Count(e => e!["event"]!.GetValue<string>() == "damage") == expected,
                    mode + $": expected {expected} primary HP-loss call(s)");
            }
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
            player.RemoveRelicInternal(relic);
            foreach (string monsterName in new[] { "PhrogParasite", "Axebot", "GremlinMerc" })
            foreach (string targeting in new[] { "random", "all", "fixed" })
            {
                EncounterModel encounter = monsterName switch
                {
                    "PhrogParasite" => ModelDb.Encounter<PhrogParasiteElite>(),
                    "Axebot" => ModelDb.Encounter<AxebotsNormal>(),
                    _ => ModelDb.Encounter<GremlinMercNormal>()
                };
                await RunManager.Instance.EnterRoom(new CombatRoom(encounter.ToMutable(), run));
                await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                    "Continuation encounter did not start", CancellationToken.None);
                combat = CombatManager.Instance.DebugOnlyGetState()!;
                Creature parent = combat.HittableEnemies.Single();
                parent.SetCurrentHpInternal(1);
                Require(Hook.ShouldStopCombatFromEnding(combat), monsterName + " native continuation flag is absent");
                CardModel card = targeting switch
                {
                    "random" => combat.CreateCard<PalmThrust>(player),
                    "all" => combat.CreateCard<DaggerSpray>(player),
                    _ => combat.CreateCard<TwinStrike>(player)
                };
                card.UpgradeInternal();
                await CardPileCmd.Add(card, PileType.Hand, skipVisuals: true);
                FinisherSmokeObserver.Reset();
                CombatRegressionProbe.Targets.Clear();
                await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, targeting == "fixed" ? parent : null);
                Creature[] hits = CombatRegressionProbe.Targets.ToArray();
                _checkpoints.Write("combat-regression.continuation", data: new JsonObject
                {
                    ["monster"] = monsterName, ["targeting"] = targeting,
                    ["targets"] = new JsonArray(hits.Select(t => (JsonNode?)JsonValue.Create(t.Monster!.Id.ToString())).ToArray()),
                    ["finishers"] = FinisherSmokeObserver.Snapshots().Length
                });
                Require(parent.IsDead && hits.Count(t => t == parent) == 1, monsterName + ": a later segment hit the dead parent");
                Require(FinisherSmokeObserver.Snapshots().Length == 0, "An intermediate phase incorrectly started a finisher");
                Require(targeting == "fixed" ? hits.Length == 1 : hits.Skip(1).Any(t => t != parent),
                    monsterName + ": remaining segments did not follow native targeting");
            }
            CombatRegressionProbe.Record = null;
            var choice = new BlockingPlayerChoiceContext();
            var owner = player.Creature;
            await PowerCmd.Apply<NarakuLifePower>(choice, owner, 12, owner, null);
            await WaitFrames(3);
            var container = FindDescendant<NPowerContainer>(owner.GetCreatureNode()!)!;
            NPower Icon() => container.GetChildren().OfType<NPower>().Single(n => n.Model is NarakuLifePower);
            Require(Icon().IsVisibleInTree() && Icon().GetNode<Label>("%AmountLabel").Text == "12", "Naraku Life icon/count is missing");
            await CreatureCmd.Damage(choice, [owner], 5, ValueProp.Unpowered, owner, null
#if !NINJASLAYER_CHANNEL_STABLE
                , null
#endif
            );
            await WaitFrames(3);
            Require(Icon().GetNode<Label>("%AmountLabel").Text == "7", "Naraku Life count did not follow absorption");
            await CreatureCmd.Damage(choice, [owner], 8, ValueProp.Unpowered, owner, null
#if !NINJASLAYER_CHANNEL_STABLE
                , null
#endif
            );
            await WaitFrames(3);
            Require(!container.GetChildren().OfType<NPower>().Any(n => n.Model is NarakuLifePower), "Empty Naraku Life left an icon");
            _checkpoints.Write("combat-regression.naraku-icon");
            var lastEnemies = combat.HittableEnemies.ToArray();
            foreach (var enemy in lastEnemies.Skip(1)) await CreatureCmd.Kill(enemy);
            lastEnemies[0].SetCurrentHpInternal(1);
            var strike = combat.CreateCard<StrikeNinjaSlayer>(player);
            await CardPileCmd.Add(strike, PileType.Hand, skipVisuals: true);
            FinisherSmokeObserver.Reset();
            await CardCmd.AutoPlay(choice, strike, lastEnemies[0]);
            Require(lastEnemies[0].IsDead && FinisherSmokeObserver.Snapshots().Length == 1,
                "The final phase did not retain its ordinary finisher");
            _checkpoints.Write("combat-regression.final-finisher");
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            CombatRegressionProbe.Record = null;
        }
        _checkpoints.Write("combat-regression.completed");
        NGame.Instance.Quit();
    }
}

internal static class CombatRegressionProbe
{
    internal static Action<string, string>? Record;
    internal static readonly List<Creature> Targets = [];
    public static void Action() => Record?.Invoke("action", "ARROW");
    public static void Projectile(bool spin) => Record?.Invoke("projectile", spin ? "shuriken" : "arrow");
    public static void Film(object __instance)
    {
        var type = __instance.GetType();
        if ((bool)AccessTools.Field(type, "_released").GetValue(__instance)!) return;
        double position = (double)AccessTools.Property(type, "PlaybackPosition").GetValue(__instance)!;
        if (position >= 1d) Record?.Invoke("film-release-window", position.ToString("F4"));
    }
    public static void Damage(Creature __instance, decimal amount, DamageResult __result)
    {
        if (__instance.Side == CombatSide.Enemy && amount > 0)
        {
            bool commit = System.Environment.StackTrace.Contains("KillWithoutCheckingWinCondition", StringComparison.Ordinal);
            if (!commit) Targets.Add(__instance);
            Record?.Invoke(commit ? "death-commit" : "damage", $"{__instance.Monster?.Id}: amount={amount}, hp={__instance.CurrentHp}, loss={__result.UnblockedDamage}; {System.Environment.StackTrace}");
        }
    }
}
