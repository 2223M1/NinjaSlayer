using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Migrations;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.Telemetry;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBalanceTelemetry()
    {
        Type collector = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Telemetry.NinjaSlayerCombatTelemetry", true)!;
        var summarize = AccessTools.Method(collector, "Summarize")
            .CreateDelegate<Func<ICombatState, CombatHistory, List<BalancePlayerCombat>>>();
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            var card = AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardPileCmd.Draw(Choice, 1, combat.Player);
            await PowerCmd.Apply<EchoFormPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await card.OnPlayWrapper(Choice, combat.Enemy, false,
                new ResourceInfo { EnergySpent = 1, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, skipCardPileVisuals: true);
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            var stats = summarize(combat.State, CombatManager.Instance.History).Single().Cards[card.Id.ToString()];
            Require(stats.Drawn == 1 && stats.Started == 3 && stats.Finished == 3
                && stats.ManualPlays == 1 && stats.AutoPlays == 1 && stats.EnergySpent == 1,
                "Telemetry must count native draw/replay/autoplay separately and charge replay resources only once.");
            card.UpgradeInternal();
            Require(summarize(combat.State, CombatManager.Instance.History).Single().Cards.Count == 1,
                "Telemetry must not invent a past upgrade state from a mutable card reference.");
        }
        using (var combat = new OrbCombat())
            Require(summarize(combat.State, CombatManager.Instance.History).Count == 0,
                "Non-Ninja-Slayer combat must not create a player sample.");

        var first = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 76561198000000001);
        var second = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 76561198000000002);
        first.InitializeSeed("telemetry-contract"); second.InitializeSeed("telemetry-contract");
        var choice = new CardChoiceHistoryEntry(first.Deck.Cards.First(), true);
        var native = new SerializableRun
        {
            Players = [first.ToSerializable(), second.ToSerializable()],
            StartTime = 1789180000, Ascension = 10,
            SerializableRng = new SerializableRunRngSet { Seed = "telemetry-contract" },
            MapPointHistory = [[new MapPointHistoryEntry
            {
                Rooms = [new MapPointRoomHistoryEntry { RoomType = RoomType.Monster, ModelId = new ModelId("ENCOUNTER", "TEST"), TurnsTaken = 3 }],
                PlayerStats = [new PlayerMapPointHistoryEntry { PlayerId = first.NetId, CardChoices = [choice] },
                    new PlayerMapPointHistoryEntry { PlayerId = second.NetId }]
            }]]
        };
        var build = AccessTools.Method(typeof(NinjaSlayerBalanceTelemetry), "BuildRunPayload")
            .CreateDelegate<Func<SerializableRun, JsonNode>>();
        JsonNode payload = build(native);
        Require(payload["players"]![0]!["net_id"]!.GetValue<string>() == first.NetId.ToString(CultureInfo.InvariantCulture)
            && payload["map_point_history"]![0]![0]!["player_stats"]![1]!["player_id"]!.GetValue<string>() == second.NetId.ToString(CultureInfo.InvariantCulture),
            "UInt64 IDs lost precision or no longer join with native choice records.");
        var envelope = new JsonArray(new JsonObject
        {
            ["event"] = "run_history.completed", ["timestamp"] = "2026-09-12T10:00:00Z",
            ["properties"] = new JsonObject
            {
                ["applicant_id"] = "NinjaSlayer", ["is_victory"] = true, ["is_abandoned"] = false,
                ["payload"] = new JsonObject
                {
                    ["applicant_payload"] = new JsonObject { ["run_history"] = payload },
                    ["private_contributions"] = new JsonObject
                    {
                        ["NinjaSlayer"] = new JsonObject { [NinjaSlayerBalanceTelemetry.BalanceContextContributionId] =
                            new NinjaSlayerBalanceTelemetry.NinjaSlayerBalanceContributionProvider().Build(new()
                            {
                                ApplicantId = "NinjaSlayer", RequestId = NinjaSlayerBalanceTelemetry.BalanceRequestId, EventName = "run_history.completed"
                            }) }
                    }
                }
            }
        });
        string? fixture = System.Environment.GetEnvironmentVariable("NINJASLAYER_TELEMETRY_FIXTURE_OUT");
        if (fixture is not null) System.IO.File.WriteAllText(fixture, envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PASS native telemetry draw, manual play, replay, free autoplay and exact multiplayer identifiers");
        VerifyBalanceTelemetrySavedRun(collector);
    }

    private static void VerifyBalanceTelemetrySavedRun(Type collector)
    {
        string? fixture = System.Environment.GetEnvironmentVariable("NINJASLAYER_TELEMETRY_SAVE_FIXTURE");
        if (fixture is null)
        {
            GD.Print("NOT RUN: live-run telemetry save reload (no same-host save fixture supplied).");
            return;
        }
        string source = System.IO.File.ReadAllText(fixture);
        using (STS2RitsuLib.RitsuLibFramework.BeginModDataRegistration("NinjaSlayer"))
            AccessTools.Method(collector, "Register").Invoke(null, null);
        var files = new MockGodotFileIo("user://telemetry-save-contract");
        var saves = new RunSaveManager(1, files, new MigrationManager(files), forceSynchronous: true);
        string savePath = RunSaveManager.GetRunSavePath(1, RunSaveManager.runSaveFileName);
        files.WriteFile(savePath, source);
        var loaded = saves.LoadRunSave();
        Require(loaded.Success, "Native telemetry save fixture did not load.");
        RunState restored = RunState.FromSerializable(loaded.SaveData!);
        var export = AccessTools.Method(collector, "Export").CreateDelegate<Func<RunState, JsonNode>>();
        var actual = export(restored)["combats"]!.AsObject();
        JsonNode native = JsonNode.Parse(source)!;
        var expected = native["_ritsulib"]!["run_saved_data"]!["NinjaSlayer"]!["balance_combats_v1"]!["data"]!.Deserialize<BalanceCombatData>()!;
        JsonNode expectedJson = JsonSerializer.SerializeToNode(expected, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        Require(actual.Count > 0 && JsonNode.DeepEquals(export(restored), expectedJson),
            "Run reload lost, duplicated or changed completed combat measurements.");
        GD.Print($"PASS live native save reload retains {actual.Count} combat records");
    }
}
