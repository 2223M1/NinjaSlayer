using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Code.Telemetry;

// One replaceable record per room: replaying a saved fight must not add another attempt.
internal static class NinjaSlayerCombatTelemetry
{
    private static RunSavedData<BalanceCombatData> _saved = null!;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static void Register()
    {
        _saved = RitsuLibFramework.GetRunSavedDataStore(NinjaSlayerIds.ModId)
            .Register<BalanceCombatData>("balance_combats_v1", options: new RunSavedDataOptions
            {
                WritePolicy = RunSavedDataWritePolicy.WhenNonDefault
            });
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(evt =>
        {
            if (TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId))
                Record((RunState)evt.RunState, evt.Room, won: true);
        });
    }

    public static void Record(RunState run, CombatRoom room, bool won)
    {
        var records = Summarize(room.CombatState, CombatManager.Instance.History);
        if (records.Count == 0) return; // Runs without a Ninja Slayer player have no balance sample.
        int roomIndex = run.CurrentMapPointHistoryEntry!.Rooms.Count - 1;
        var combat = new BalanceCombat
        {
            Floor = run.TotalFloor,
            RoomIndex = roomIndex,
            Encounter = room.Encounter.Id.ToString(),
            Won = won,
            Rounds = room.CombatState.RoundNumber,
            Version = NinjaSlayerVersion.Current,
            Players = records
        };
        _saved.Modify(run, data => data.Combats[$"{combat.Floor}/{roomIndex}"] = combat);
    }

    public static JsonNode Export(RunState run) => JsonSerializer.SerializeToNode(_saved.Get(run), JsonOptions)!;

    internal static List<BalancePlayerCombat> Summarize(ICombatState state, CombatHistory history)
    {
        var players = state.Players.Where(player => player.Character is INinjaSlayerCharacter)
            .ToDictionary(player => player, player => new BalancePlayerCombat { PlayerId = player.NetId.ToString(CultureInfo.InvariantCulture) });
        foreach (CombatHistoryEntry entry in history.Entries)
        {
            if (entry.Actor?.Player is not { } player || !players.TryGetValue(player, out var stats)) continue;
            CardModel? card = entry switch
            {
                CardDrawnEntry drawn => drawn.Card,
                CardPlayStartedEntry play => play.CardPlay.Card,
                CardPlayFinishedEntry play => play.CardPlay.Card,
                _ => null
            };
            if (card is null) continue;
            string id = card.Id.ToString();
            if (!stats.Cards.TryGetValue(id, out var count)) stats.Cards.Add(id, count = new BalanceCardUse());
            switch (entry)
            {
                case CardDrawnEntry:
                    count.Drawn++;
                    break;
                case CardPlayStartedEntry started:
                    count.Started++;
                    // Every replay carries the same ResourceInfo. The series pays only once.
                    if (started.CardPlay.IsFirstInSeries)
                    {
                        if (started.CardPlay.IsAutoPlay) count.AutoPlays++;
                        else count.ManualPlays++;
                        count.EnergySpent += started.CardPlay.Resources.EnergySpent;
                        count.StarsSpent += started.CardPlay.Resources.StarsSpent;
                    }
                    break;
                case CardPlayFinishedEntry:
                    count.Finished++;
                    break;
            }
        }
        return players.Values.ToList();
    }
}

public sealed class BalanceCombatData
{
    public Dictionary<string, BalanceCombat> Combats { get; set; } = new();
}

public sealed class BalanceCombat
{
    public int Floor { get; set; }
    public int RoomIndex { get; set; }
    public string Encounter { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Won { get; set; }
    public int Rounds { get; set; }
    public List<BalancePlayerCombat> Players { get; set; } = new();
}

public sealed class BalancePlayerCombat
{
    public string PlayerId { get; set; } = "";
    public Dictionary<string, BalanceCardUse> Cards { get; set; } = new();
}

public sealed class BalanceCardUse
{
    public int Drawn { get; set; }
    public int Started { get; set; }
    public int Finished { get; set; }
    public int ManualPlays { get; set; }
    public int AutoPlays { get; set; }
    public int EnergySpent { get; set; }
    public int StarsSpent { get; set; }
}
