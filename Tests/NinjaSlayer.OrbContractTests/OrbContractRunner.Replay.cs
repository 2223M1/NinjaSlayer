using System.IO.Compression;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.Telemetry;
using NinjaSlayer.Content;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyReplayJournal()
    {
        var assembly = typeof(NinjaSlayerSettings).Assembly;
        var collector = assembly.GetType("NinjaSlayer.Code.Telemetry.NinjaSlayerCombatTelemetry", true)!;
        var replay = assembly.GetType("NinjaSlayer.Code.Telemetry.NinjaSlayerReplay", true)!;
        var consent = assembly.GetType("NinjaSlayer.Content.NinjaSlayerTelemetryConsent", true)!;
        var setReplay = AccessTools.Method(consent, "SetReplayEnabled").CreateDelegate<Action<bool>>();
        var store = (RunSavedData<ReplayRunData>)AccessTools.Field(replay, "_saved").GetValue(null)!;
        ReplayRunData journal() => (ReplayRunData)AccessTools.Field(replay, "_journal").GetValue(null)!;
        RunState begin(OrbCombat combat, string? journalId = null)
        {
            var run = RunState.CreateForTest([combat.Player], seed: "replay-contract");
            run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, new ModelId("ENCOUNTER", "TEST"));
            if (journalId is not null) store.Modify(run, data => data.JournalId = journalId);
            AccessTools.Method(collector, "Start").Invoke(null, [run, combat.State]);
            journal().Floors.Add(1); // This fixture enters the first room with explicit consent.
            return run;
        }
        JsonNode report(RunState run, OrbCombat combat)
        {
            var native = new SerializableRun { Players = [combat.Player.ToSerializable()], StartTime = 12345,
                SerializableRng = new SerializableRunRngSet { Seed = "replay-contract" }, MapPointHistory = run.MapPointHistory.Select(act => act.ToList()).ToList() };
            var upload = (JsonObject)AccessTools.Method(replay, "BuildUpload").Invoke(null, [run, native, true])!;
            using var bytes = new MemoryStream(Convert.FromBase64String(upload["report"]!.GetValue<string>()));
            using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return JsonNode.Parse(reader.ReadToEnd())!;
        }
        setReplay(true);
        string original;
        using (var first = new OrbCombat(ninjaSlayer: true))
        {
            begin(first);
            original = journal().JournalId;
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(first, PileType.Hand), first.Enemy);
            AccessTools.Method(replay, "Flush").Invoke(null, null);
        }
        using (var resumed = new OrbCombat(ninjaSlayer: true))
        {
            var run = begin(resumed, original);
            Require(journal().JournalId == original, "Save rewind must continue its durable public journal.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(resumed, PileType.Hand), resumed.Enemy);
            await CreatureCmd.Heal(resumed.Enemy, 2);
            AccessTools.Method(replay, "EndCombat").Invoke(null, [run, new BalanceCombat { Floor = 1, RoomIndex = 0, Encounter = "ENCOUNTER.TEST", Won = true }]);
            var data = report(run, resumed);
            var frames = data["frames"]!.AsArray();
            Require(frames.Any(frame => frame!["action"]!["kind"]!.GetValue<string>() == "heal"
                && frame["action"]!["actor"]!.GetValue<string>() != "self"
                && frame["action"]!["amount"]!.GetValue<int>() == 2),
                "Enemy healing must update public battle HP without being attributed to the player.");
            Require(data["coverage"]!.GetValue<string>() == "complete"
                && frames.Count(frame => frame!["action"]!["kind"]!.GetValue<string>() == "attempt_interrupted") == 1
                && frames.Count(frame => frame!["action"]!["kind"]!.GetValue<string>() == "play") == 2,
                "SL must retain both attempts once, mark interruption and finish the final attempt.");
            setReplay(false);
            Require(journal().Withdrawn && journal().Floors.Count == 0, "Revocation must persist outside the rewound game save.");
        }
        using (var oldSave = new OrbCombat(ninjaSlayer: true))
        {
            setReplay(true);
            var run = begin(oldSave, original);
            Require(journal().JournalId != original, "Loading a withdrawn save must not republish the old journal.");
            AccessTools.Method(replay, "EndCombat").Invoke(null, [run, new BalanceCombat { Floor = 1, RoomIndex = 0, Encounter = "ENCOUNTER.TEST", Won = true }]);
            Require(!report(run, oldSave)["frames"]!.AsArray().Any(frame => frame!["action"]!["kind"]!.GetValue<string>() == "play"),
                "New consent must exclude plays captured under earlier consent.");
        }
        setReplay(false);
        AccessTools.Method(collector, "Stop").Invoke(null, null);
        async Task<string[]> drawSequence(bool enabled)
        {
            setReplay(enabled);
            using var combat = new OrbCombat(ninjaSlayer: true);
            var run = RunState.CreateForTest([combat.Player], seed: "replay-determinism");
            run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, new ModelId("ENCOUNTER", "TEST"));
            AccessTools.Method(collector, "Start").Invoke(null, [run, combat.State]);
            for (int index = 0; index < 5; index++)
            {
                AddCard<StrikeIronclad>(combat, PileType.Discard);
                AddCard<DefendIronclad>(combat, PileType.Discard);
            }
            await CardPileCmd.Shuffle(Choice, combat.Player);
            await CardPileCmd.Draw(Choice, 5, combat.Player);
            return combat.Player.Piles.SelectMany(pile => pile.Cards.Select(card => pile.Type + "/" + card.Id)).ToArray();
        }
        Require((await drawSequence(false)).SequenceEqual(await drawSequence(true)),
            "Public collection must not alter native seeded shuffle, draw order or card piles.");
        setReplay(false);
        AccessTools.Method(collector, "Stop").Invoke(null, null);
        GD.Print("PASS public replay native plays, durable SL attempts, revocation and fresh consent isolation");
    }
}
