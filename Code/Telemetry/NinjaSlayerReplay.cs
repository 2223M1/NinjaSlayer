using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Code.Telemetry;

// Local journals survive rewinding the native combat-start save. They contain public fields only.
internal static class NinjaSlayerReplay
{
    private const int MaximumJournalBytes = 12 * 1024 * 1024;
    private static RunSavedData<ReplayRunData> _saved = null!;
    private static readonly List<ReplayFrame> Pending = [];
    private static RunState? _run;
    private static ReplayRunData? _journal;
    private static int _pendingBytes;
    private static string? _attempt;
    private static int _floor;
    private static int _room;
    private static int _sequence;
    internal static string Status { get; private set; } = "idle";
    private static string DirectoryPath => ProjectSettings.GlobalizePath("user://NinjaSlayer/public-replays");

    internal static void Register()
    {
        _saved = RitsuLibFramework.GetRunSavedDataStore(NinjaSlayerIds.ModId)
            .Register<ReplayRunData>("public_replay_v1", options: new RunSavedDataOptions { WritePolicy = RunSavedDataWritePolicy.WhenNonDefault });
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(evt => Activate((RunState)evt.RunState));
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(evt => Activate((RunState)evt.RunState));
        RitsuLibFramework.SubscribeLifecycle<RoomEnteredEvent>(evt =>
        {
            if (!NinjaSlayerTelemetryConsent.ReplayEnabled) return;
            var run = (RunState)evt.RunState;
            Activate(run);
            // Native choices accumulate over a whole floor. Do not expose an earlier room on a floor
            // where consent began later (for example, the second boss or an event combat).
            if (run.CurrentMapPointHistoryEntry!.Rooms.Count == 1)
            {
                _journal!.Floors.Add(run.TotalFloor);
                SaveIndex();
            }
        });
        RitsuLibFramework.SubscribeLifecycle<SideTurnStartedEvent>(_ => Flush());
        RitsuLibFramework.SubscribeLifecycle<RoomExitedEvent>(_ => Flush());
    }

    private static ReplayRunData EnsureId(RunState run)
    {
        if (_saved.Get(run).JournalId.Length == 0)
            _saved.Modify(run, data => data.JournalId = Guid.NewGuid().ToString("N"));
        return _saved.Get(run);
    }

    private static string JournalPath(ReplayRunData data)
    {
        string id = data.JournalId;
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid replay journal ID in saved data.");
        return Path.Combine(DirectoryPath, id + ".jsonl");
    }

    private static void Activate(RunState run)
    {
        if (ReferenceEquals(_run, run)) return;
        if (_run is not null && _attempt is not null)
            Append(_run, new BattleAction { Kind = "attempt_interrupted", Side = "None" });
        Flush(); // Pending belongs to the previous journal, even when a save has been rewound.
        Pending.Clear(); _pendingBytes = 0; _attempt = null;
        _run = run;
        _journal = EnsureId(run);
        string path = JournalPath(_journal) + ".index";
        try
        {
            if (System.IO.File.Exists(path))
                _journal = JsonSerializer.Deserialize<ReplayRunData>(System.IO.File.ReadAllText(path))
                    ?? throw new JsonException("Empty replay index.");
            else if (System.IO.File.Exists(JournalPath(_journal))) _journal.Gapped = true;
            if (System.IO.File.Exists(JournalPath(_journal)))
            {
                string? last = System.IO.File.ReadLines(JournalPath(_journal)).LastOrDefault();
                if (last is not null)
                {
                    var frame = JsonSerializer.Deserialize<ReplayFrame>(last, NinjaSlayerCombatTelemetry.JsonOptions)!;
                    if (frame.Action.Kind is not ("attempt_end" or "attempt_interrupted"))
                    {
                        _attempt = frame.Attempt; _floor = frame.Floor; _room = frame.Room; _sequence = frame.Sequence + 1;
                        Append(run, new BattleAction { Kind = "attempt_interrupted", Round = frame.Action.Round, Side = "None" });
                        Flush();
                        _attempt = null;
                    }
                }
            }
            if (Directory.Exists(DirectoryPath))
                foreach (string file in Directory.EnumerateFiles(DirectoryPath))
                    if (System.IO.File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-90)) System.IO.File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _journal.Gapped = true; Status = "storage_error";
        }
        if (!NinjaSlayerTelemetryConsent.ReplayEnabled) WithdrawActiveRun();
        else if (_journal.Withdrawn) BeginConsent();
    }

    private static void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = JournalPath(_journal!) + ".index";
            System.IO.File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(_journal));
            System.IO.File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _journal!.Gapped = true; Status = "storage_error";
        }
    }

    internal static void StartCombat(RunState run, BalanceCombat combat)
    {
        Activate(run);
        Flush();
        _attempt = Guid.NewGuid().ToString("N");
        _floor = combat.Floor; _room = combat.RoomIndex; _sequence = 0;
        Status = _journal!.Truncated ? "truncated" : "recording";
    }

    internal static void Append(RunState run, BattleAction action)
    {
        if (_attempt is null || _journal!.Truncated) return;
        var frame = new ReplayFrame { Attempt = _attempt, Floor = _floor, Room = _room, Sequence = _sequence, Action = action };
        int bytes = JsonSerializer.SerializeToUtf8Bytes(frame, NinjaSlayerCombatTelemetry.JsonOptions).Length + 1;
        if (_journal.Bytes + _pendingBytes + bytes > MaximumJournalBytes || _journal.Frames + Pending.Count >= 50000)
        {
            _journal.Truncated = true; Status = "truncated"; SaveIndex();
            return;
        }
        _sequence++; _pendingBytes += bytes;
        Pending.Add(frame);
        // No networking in combat. A turn boundary writes this small buffer to the local journal.
    }

    internal static void EndCombat(RunState run, BalanceCombat combat)
    {
        Append(run, new BattleAction { Kind = "attempt_end", Round = combat.Rounds, Side = "None",
            Model = combat.Encounter, Amount = combat.Won ? 1 : 0 });
        _attempt = null;
    }

    internal static void WithdrawActiveRun()
    {
        if (_journal is null) return;
        Pending.Clear(); _pendingBytes = 0; _attempt = null;
        // Persist the withdrawal outside the native save too; loading an older combat cannot undo it.
        if (_journal.Withdrawn) return;
        _journal.Withdrawn = true;
        _journal.Floors.Clear();
        SaveIndex();
        Status = "disabled";
    }

    internal static void BeginConsent()
    {
        if (_run is null) return;
        _saved.Modify(_run, data => data.JournalId = Guid.NewGuid().ToString("N"));
        _journal = _saved.Get(_run);
        _journal.Floors.Clear(); _journal.Truncated = false; _journal.Gapped = false;
        _journal.Withdrawn = false; _journal.Bytes = 0; _journal.Frames = 0;
        Status = "waiting";
        SaveIndex();
    }

    private static void Flush()
    {
        if (Pending.Count == 0) return;
        if (!NinjaSlayerTelemetryConsent.ReplayEnabled) { WithdrawActiveRun(); return; }
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = JournalPath(_journal!);
            string lines = string.Concat(Pending.Select(frame => JsonSerializer.Serialize(frame, NinjaSlayerCombatTelemetry.JsonOptions) + "\n"));
            long size = System.IO.File.Exists(path) ? new FileInfo(path).Length : 0;
            if (size + Encoding.UTF8.GetByteCount(lines) > MaximumJournalBytes)
            {
                _journal!.Truncated = true; Status = "truncated";
            }
            else
            {
                using (var file = new FileStream(path, FileMode.OpenOrCreate, System.IO.FileAccess.Write))
                {
                    file.Position = size;
                    try { file.Write(Encoding.UTF8.GetBytes(lines)); }
                    catch (IOException)
                    {
                        file.SetLength(size); // Retry the complete pending buffer after a partial write.
                        throw;
                    }
                }
                _journal!.Bytes = size + Encoding.UTF8.GetByteCount(lines);
                _journal.Frames += Pending.Count;
            }
            Pending.Clear(); _pendingBytes = 0;
            SaveIndex();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _journal!.Gapped = true; Status = "storage_error";
            // Keep the buffer for the next room/turn write; report the missing durable segment.
        }
    }

    internal static JsonObject? BuildUpload(RunState run, SerializableRun history, bool won)
    {
        try { return BuildReport(run, history, won); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = "storage_error";
            return null; // Keep the local journal; this is not a successfully queued report.
        }
    }

    private static JsonObject? BuildReport(RunState run, SerializableRun history, bool won)
    {
        if (!NinjaSlayerTelemetryConsent.ReplayEnabled) return null;
        Activate(run);
        Flush();
        var saved = _journal!;
        if (saved.Withdrawn) return null;
        string path = JournalPath(saved);
        if (!System.IO.File.Exists(path)) return null;
        var local = LocalContext.GetMe(history)!;
        JsonNode native = NinjaSlayerBalanceTelemetry.BuildRunPayload(history);
        var frames = System.IO.File.ReadLines(path).Where(line => line.Length > 0)
            .Select(line => JsonNode.Parse(line)!).ToArray();
        var floors = new JsonArray();
        var expectedCombats = new HashSet<(int Floor, int Room)>();
        int floor = 0;
        foreach (var act in history.MapPointHistory)
        foreach (var point in act)
        {
            floor++;
            for (int room = 0; room < point.Rooms.Count; room++)
                if (point.Rooms[room].RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
                    expectedCombats.Add((floor, room));
            if (!saved.Floors.Contains(floor)) continue;
            var stats = point.PlayerStats.Single(p => p.PlayerId == local.NetId);
            floors.Add(JsonSerializer.SerializeToNode(new
            {
                floor,
                rooms = point.Rooms.Select(room => new { type = room.RoomType.ToString(), model = room.ModelId?.ToString() }),
                hp = stats.CurrentHp, max_hp = stats.MaxHp, gold = stats.CurrentGold,
                damage_taken = stats.DamageTaken, healed = stats.HpHealed,
                cards_gained = stats.CardsGained.Select(card => new { id = card.Id!.ToString(), upgrade = card.CurrentUpgradeLevel }),
                card_choices = stats.CardChoices.Select(choice => new { id = choice.Card.Id!.ToString(), upgrade = choice.Card.CurrentUpgradeLevel, picked = choice.wasPicked }),
                relic_choices = stats.RelicChoices.Select(choice => new { id = choice.choice.ToString(), picked = choice.wasPicked }),
                potion_choices = stats.PotionChoices.Select(choice => new { id = choice.choice.ToString(), picked = choice.wasPicked }),
                cards_removed = stats.CardsRemoved.Select(card => card.Id!.ToString()),
                upgraded = stats.UpgradedCards.Select(id => id.ToString()),
                transformed = stats.CardsTransformed.Select(change => new { from = change.OriginalCard.Id!.ToString(), to = change.FinalCard.Id!.ToString() }),
                enchanted = stats.CardsEnchanted.Select(change => new { card = change.Card.Id!.ToString(), enchantment = change.Enchantment.ToString() }),
                events = stats.EventChoices.Select(choice => choice.Title.LocEntryKey),
                rests = stats.RestSiteChoices,
                bought_relics = stats.BoughtRelics.Select(id => id.ToString()),
                bought_potions = stats.BoughtPotions.Select(id => id.ToString()),
                potions_used = stats.PotionUsed.Select(id => id.ToString())
            }));
        }
        var report = new JsonObject
        {
            ["schema"] = "ninja_slayer_replay_v1", ["version"] = NinjaSlayerVersion.Current,
            ["won"] = won, ["ascension"] = history.Ascension, ["mode"] = history.GameMode.ToString(),
            ["party"] = history.Players.Count, ["contributor"] = history.Players.FindIndex(player => player.NetId == local.NetId),
            ["reloads"] = history.NumReloads, ["duration"] = history.RunTime,
            ["coverage"] = saved.Truncated ? "truncated" : saved.Gapped || Pending.Count > 0 || floors.Count != history.FloorReached
                || !HasCompleteCombats(frames, expectedCombats) ? "gapped" : "complete",
            ["floors"] = floors, ["frames"] = new JsonArray(frames)
        };
        // This private deduplication digest is HMACed again by the server before becoming a public ID.
        string identity = native["rng"]!["seed"]!.GetValue<string>() + "/" + history.StartTime + "/"
            + string.Join(",", history.Players.Select(player => player.NetId).Order());
        string runKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        byte[] reportBytes = JsonSerializer.SerializeToUtf8Bytes(report);
        if (reportBytes.Length > MaximumJournalBytes) { Status = "oversized"; return null; }
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(reportBytes);
        if (compressed.Length > 2 * 1024 * 1024) { Status = "oversized"; return null; }
        return new JsonObject { ["run_key"] = runKey, ["encoding"] = "gzip+base64", ["report"] = Convert.ToBase64String(compressed.ToArray()) };
    }

    internal static void Submitted() => Status = "submitted"; // Queued by RitsuLib, not a remote receipt.

    private static bool HasCompleteCombats(JsonNode[] frames, HashSet<(int Floor, int Room)> expected)
    {
        var completed = new HashSet<(int Floor, int Room)>();
        foreach (var attempt in frames.GroupBy(frame => frame["attempt"]!.GetValue<string>()))
        {
            var first = attempt.First();
            var last = attempt.Last();
            if (first["action"]!["kind"]!.GetValue<string>() != "combat_start"
                || last["action"]!["kind"]!.GetValue<string>() is not ("attempt_end" or "attempt_interrupted")) return false;
            var room = (first["floor"]!.GetValue<int>(), first["room"]!.GetValue<int>());
            if (last["action"]!["kind"]!.GetValue<string>() == "attempt_end") completed.Add(room);
            else completed.Remove(room);
        }
        return expected.SetEquals(completed);
    }
}

public sealed class ReplayRunData
{
    public string JournalId { get; set; } = "";
    public HashSet<int> Floors { get; set; } = new();
    public bool Truncated { get; set; }
    public bool Gapped { get; set; }
    public bool Withdrawn { get; set; }
    public long Bytes { get; set; }
    public int Frames { get; set; }
}
public sealed class ReplayFrame
{
    public string Attempt { get; set; } = "";
    public int Floor { get; set; }
    public int Room { get; set; }
    public int Sequence { get; set; }
    public BattleAction Action { get; set; } = new();
}
