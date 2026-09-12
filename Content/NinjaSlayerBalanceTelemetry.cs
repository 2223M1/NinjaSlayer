using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.Telemetry;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Content;

public static class NinjaSlayerBalanceTelemetry
{
    public const string BalanceContextContributionId = "ninja_slayer_balance_context";

    public const string BalanceRequestId = "balance_runs";
    public const string ReplayRequestId = "public_replays";

    public static void Register()
    {
        NinjaSlayerCombatTelemetry.Register();
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(ObserveRunEnded);

        RitsuLibFramework.RegisterTelemetryContributionProvider(new NinjaSlayerBalanceContributionProvider());

        TelemetryRegistry.RegisterApplicant(
            new TelemetryApplicant
            {
                ApplicantId = NinjaSlayerIds.ModId,
                OwnerModId = NinjaSlayerIds.ModId,
                DisplayName = NinjaSlayerIds.ModId,
                DisplayNameText = ModSettingsText.Literal("Ninja Slayer"),
                Adapter = new PostHogTelemetryAdapter(
                    host: "https://ninja-slayer-telemetry.theonetrue2223.workers.dev",
                    projectApiKey: "proxy"
                ),
                Requests =
                [
                    // A custom request avoids the framework's automatic run_history capture.
                    new TelemetryRequest
                    {
                        RequestId = BalanceRequestId,
                        Category = TelemetryDataCategory.RunHistory,
                        Description = "Completed runs: card reward choices, final decks, results and per-combat draws, plays and resources paid, for balance analysis.",
                        DescriptionText = NinjaSlayerTelemetryConsent.Text("DESCRIPTION"),
                        ContributionSubscriptions = [BalanceContextContributionId]
                    },
                    new TelemetryRequest
                    {
                        RequestId = ReplayRequestId,
                        Category = TelemetryDataCategory.RunHistory,
                        Description = "Publish an anonymous action-by-action battle report for 90 days. Off until explicitly enabled.",
                        DescriptionText = NinjaSlayerTelemetryConsent.Text("REPLAY_DESCRIPTION")
                    }
                ],
            }
        );
        NinjaSlayerTelemetryConsent.Register();
    }

    private static void ObserveRunEnded(RunEndedEvent evt)
    {
        if (evt.IsAbandoned
            || LocalContext.GetMe(evt.Run)?.CharacterId != ModelDb.Character<NinjaSlayerCharacter>().Id) return;

        // RitsuLib publishes this synchronously from RunManager.OnEnded while State still owns the run.
        RunState run = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("RunEnded was published without its active run.");
        if (NinjaSlayerFreeControl.WasUsed(run)) return;
        if (!evt.IsVictory && run.CurrentRoom is CombatRoom room)
            NinjaSlayerCombatTelemetry.Record(run, room, won: false);

        if (NinjaSlayerReplay.BuildUpload(run, evt.Run, evt.IsVictory) is { } replay)
        {
            TelemetryApi.GetClient(NinjaSlayerIds.ModId).CapturePayload("battle_report.completed", ReplayRequestId, replay);
            NinjaSlayerReplay.Submitted();
        }
        if (!TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(BalanceRequestId)) return;

        TelemetryApi.GetClient(NinjaSlayerIds.ModId).CapturePayload("run_history.completed", BalanceRequestId,
            new JsonObject
            {
                ["run_history"] = BuildRunPayload(evt.Run),
                ["mod_payload"] = NinjaSlayerCombatTelemetry.Export(run)
            },
            properties: new Dictionary<string, object?>
            {
                ["is_victory"] = evt.IsVictory,
                ["is_abandoned"] = evt.IsAbandoned,
                ["occurred_at_utc"] = evt.OccurredAtUtc.ToString("O"),
                ["run_floor_reached"] = evt.Run.FloorReached,
                ["run_ascension"] = evt.Run.Ascension,
                ["run_player_count"] = evt.Run.Players.Count,
                ["run_game_mode"] = evt.Run.GameMode.ToString(),
                ["run_reload_count"] = evt.Run.NumReloads
            });
    }

    internal static JsonNode BuildRunPayload(SerializableRun run)
    {
        JsonNode json = JsonSerializer.SerializeToNode(run, JsonSerializationUtility.GetTypeInfo<SerializableRun>())!;
        // The backend uses JavaScript numbers. UInt64 IDs must cross that boundary as exact digits.
        foreach (JsonNode? player in json["players"]!.AsArray())
            player!["net_id"] = player["net_id"]!.ToJsonString();
        foreach (JsonNode? act in json["map_point_history"]?.AsArray() ?? [])
        foreach (JsonNode? floor in act!.AsArray())
        foreach (JsonNode? player in floor!["player_stats"]!.AsArray())
            player!["player_id"] = player["player_id"]!.ToJsonString();
        return json;
    }

    public class NinjaSlayerBalanceContributionProvider : ITelemetryContributionProvider
    {
        public string ContributorModId => NinjaSlayerIds.ModId;

        public string ContributionId => BalanceContextContributionId;

        public TelemetryDataCategory Category => TelemetryDataCategory.RunHistory;

        public TelemetryContributionVisibility Visibility =>
            TelemetryContributionVisibility.PrivateToApplicant;

        public JsonNode? Build(TelemetryContributionContext context)
        {
            return new JsonObject
            {
                ["version"] = NinjaSlayerVersion.Current,
                ["balance_schema"] = "ninja_slayer_run_history_v3",
            };
        }
    }
}
