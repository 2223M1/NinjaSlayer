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
        using (STS2RitsuLib.RitsuLibFramework.BeginModDataRegistration("NinjaSlayer"))
            AccessTools.Method(collector, "Register").Invoke(null, null);
        STS2RitsuLib.RitsuLibFramework.SetTelemetryApplicantConsent("NinjaSlayer",
            STS2RitsuLib.Telemetry.TelemetryConsentState.Granted, [NinjaSlayerBalanceTelemetry.BalanceRequestId]);
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        var start = AccessTools.Method(collector, "Start").CreateDelegate<Action<RunState, ICombatState>>();
        var stop = AccessTools.Method(collector, "Stop").CreateDelegate<Action>();
        BalanceCombat? measurement() => (BalanceCombat?)AccessTools.Field(collector, "_combat").GetValue(null);
        void begin(OrbCombat combat)
        {
            var run = RunState.CreateForTest([combat.Player], seed: "telemetry-contract");
            run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster, RoomType.Monster, new ModelId("ENCOUNTER", "TEST"));
            start(run, combat.State);
        }
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            combat.Player.Creature.SetMaxHpInternal(50);
            combat.Player.Creature.SetCurrentHpInternal(30);
            begin(combat);
            var card = AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardPileCmd.Draw(Choice, 1, combat.Player);
            await PowerCmd.Apply<EchoFormPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await card.OnPlayWrapper(Choice, combat.Enemy, false,
                new ResourceInfo { EnergySpent = 1, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, skipCardPileVisuals: true);
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            var player = measurement()!.Players.Single();
            var stats = player.Cards[card.Id.ToString()];
            Require(stats.Drawn == 1 && stats.Started == 3 && stats.Finished == 3
                && stats.ManualPlays == 1 && stats.AutoPlays == 1 && stats.EnergySpent == 1,
                "Telemetry must count native draw/replay/autoplay separately and charge replay resources only once.");
            card.UpgradeInternal();
            Require(player.Metrics!.CardVariants[card.Id + "/0"].Started == 3
                && !player.Metrics.CardVariants.ContainsKey(card.Id + "/1"),
                "Telemetry must not invent a past upgrade state from a mutable card reference.");
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(player.Metrics.CardVariants[card.Id + "/1"].AutoPlays == 1
                && player.Metrics.CardVariants[card.Id + "/0"].Started == 3,
                "Upgraded play must be snapshotted separately from the same instance's earlier base plays.");
            await PowerCmd.Apply<NinjaSlayer.Powers.NarakuLifePower>(Choice, combat.Player.Creature, 20, combat.Player.Creature, null);
            await CreatureCmd.GainBlock(combat.Player.Creature, 5, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
            await CreatureCmd.Damage(Choice, combat.Player.Creature, 30, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, combat.Enemy);
            await CreatureCmd.Heal(combat.Player.Creature, 3);
            Require(player.Metrics.HpLost == 5 && player.Metrics.Healed == 3 && player.Metrics.Blocked == 5
                && player.Metrics.Mechanics["naraku_absorbed"] == 20,
                "Actual HP loss, healing, block and Naraku absorption must remain separate native measurements.");
            var strength = await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 5, combat.Player.Creature, null);
            string strengthId = ModelDb.Power<StrengthPower>().Id.ToString();
            Require(player.Metrics.PowerChanges[strengthId] == 5, "Native power application must be measured once.");
            strength!.SetAmount(3);
            Require(player.Metrics.PowerChanges[strengthId] == 3, "Direct native power changes must update the measured amount.");
            await PowerCmd.Remove(strength);
            Require(player.Metrics.PowerChanges[strengthId] == 0, "Native power removal must record the remaining amount once.");
            Require(player.Metrics.Mechanics["naraku_gained"] == 20,
                "Naraku gain and absorption must not be duplicated by native power/history callbacks.");
            await CreatureCmd.Heal(combat.Enemy, 2);
            Require(player.Metrics.Healed == 3, "Enemy healing must not inflate the player's healing total.");
            var before = JsonSerializer.Serialize(player);
            STS2RitsuLib.RitsuLibFramework.SetTelemetryApplicantConsent("NinjaSlayer", STS2RitsuLib.Telemetry.TelemetryConsentState.Denied);
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(measurement() is null && JsonSerializer.Serialize(player) == before,
                "Native consent withdrawal must stop immediate collection without retroactive samples.");
        }
        using (var combat = new OrbCombat())
        {
            begin(combat);
            Require(measurement() is null,
                "Non-Ninja-Slayer combat must not create a player sample.");
        }
        stop();

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
        JsonNode expectedJson = JsonSerializer.SerializeToNode(expected, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })!;
        Require(actual.Count > 0 && JsonNode.DeepEquals(export(restored), expectedJson),
            "Run reload lost, duplicated or changed completed combat measurements.");
        GD.Print($"PASS live native save reload retains {actual.Count} combat records");
    }
}
