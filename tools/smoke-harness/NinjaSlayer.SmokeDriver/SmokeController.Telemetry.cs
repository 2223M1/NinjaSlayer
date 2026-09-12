using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private LocalTelemetryCapture? _telemetryCapture;

    private async Task DismissTelemetryNoticeAsync()
    {
        await WaitFrames(2);
        var modal = NGame.Instance!.GetNodeOrNull<Godot.CanvasLayer>("RitsuModSettingsStyledModal");
        if (modal is null) return; // Resumed profiles have already seen the first-launch notice.
        Require(!TelemetryApi.GetClient("NinjaSlayer").IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId),
            "First-launch notice must precede telemetry delivery.");
        Godot.Input.ParseInputEvent(new Godot.InputEventKey { Keycode = Godot.Key.Escape, Pressed = true });
        await WaitFrames(2);
        Godot.Input.ParseInputEvent(new Godot.InputEventKey { Keycode = Godot.Key.Escape, Pressed = false });
        Require(!Godot.GodotObject.IsInstanceValid(modal), "Native Escape did not dismiss the telemetry notice.");
        Require(!TelemetryApi.GetClient("NinjaSlayer").IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId),
            "Dismissing the notice must not enable telemetry in this process.");
        _checkpoints.Write("telemetry.notice-dismissed");
    }

    private async Task RunTelemetryLossPhaseAsync()
    {
        StartTelemetryCapture();
        int endEvents = 0;
        using var observation = RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(evt =>
        {
            endEvents++;
            _checkpoints.Write("telemetry.run-ended", data: new JsonObject { ["victory"] = evt.IsVictory });
        });
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        new AutoSlayer().Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await WaitTaskAsync(_telemetryCapture!.Captured.Task, "Defeat did not deliver its local telemetry envelope.", TimeSpan.FromMinutes(2));
        ValidateTelemetryCapture();
        Require(_telemetryCapture.Captured.Task.Result["applicant_payload"]!["mod_payload"]!["combats"]!.AsObject()
            .All(combat => combat.Value!["won"]!.GetValue<bool>() == false), "Defeat was recorded as a won combat.");
        var cards = _telemetryCapture.Captured.Task.Result["applicant_payload"]!["mod_payload"]!["combats"]!.AsObject()
            .SelectMany(combat => combat.Value!["players"]!.AsArray()).SelectMany(player => player!["cards"]!.AsObject());
        Require(cards.Sum(card => card.Value!["manual_plays"]!.GetValue<int>()) == 1
            && cards.Sum(card => card.Value!["energy_spent"]!.GetValue<int>()) == 1,
            "Native paid card action must record one manual play and exactly one paid energy.");
        RunManager.Instance.OnEnded(isVictory: false);
        await WaitFrames(2);
        Require(endEvents == 1 && _telemetryCapture.CaptureCount == 1, "Repeated native OnEnded duplicated telemetry.");
        _checkpoints.Write("telemetry.loss-completed");
        _tree.Quit(0);
    }

    private async Task ExecuteTelemetryLossAsync(CancellationToken cancellationToken)
    {
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "telemetry loss did not reach player turn", cancellationToken);
        var card = player.PlayerCombatState!.Hand.Cards.First(card => card.Type == CardType.Attack
            && card.EnergyCost.GetWithModifiers(CostModifiers.All) == 1);
        decimal energyBefore = player.PlayerCombatState.Energy;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card,
            CombatManager.Instance.DebugOnlyGetState()!.Enemies.First()));
        await WaitUntilAsync(() => CombatManager.Instance.History.Entries.OfType<CardPlayFinishedEntry>()
            .Any(entry => ReferenceEquals(entry.CardPlay.Card, card)), "paid card action did not finish", cancellationToken);
        Require(energyBefore - player.PlayerCombatState.Energy == 1, "Native action did not actually spend one energy.");
        await CreatureCmd.Kill(player.Creature);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private void StartTelemetryCapture()
    {
        TelemetryApplicant original = TelemetryRegistry.GetApplicants().Single(applicant => applicant.ApplicantId == "NinjaSlayer");
        _telemetryCapture = new LocalTelemetryCapture(Path.ChangeExtension(_configuration.CheckpointPath, ".telemetry.json"));
        TelemetryRegistry.RegisterApplicant(new TelemetryApplicant
        {
            ApplicantId = original.ApplicantId, OwnerModId = original.OwnerModId,
            DisplayName = original.DisplayName, DisplayNameText = original.DisplayNameText,
            Requests = original.Requests, Adapter = _telemetryCapture
        });
        // The harness uses an isolated APPDATA profile and a local file adapter, never the production endpoint.
        RitsuLibFramework.SetTelemetryApplicantConsent("NinjaSlayer", TelemetryConsentState.Denied);
        Require(!TelemetryApi.GetClient("NinjaSlayer").IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId), "Denied telemetry request remained enabled.");
        RitsuLibFramework.SetTelemetryApplicantConsent("NinjaSlayer", TelemetryConsentState.Granted, [NinjaSlayerBalanceTelemetry.BalanceRequestId]);
        Require(TelemetryApi.GetClient("NinjaSlayer").IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId), "Local test telemetry was not enabled.");
    }

    private void ValidateTelemetryCapture()
    {
        Require(_telemetryCapture!.Captured.Task.IsCompletedSuccessfully, "Completed AutoSlay did not deliver its local telemetry envelope.");
        JsonNode payload = _telemetryCapture.Captured.Task.Result;
        var history = payload["applicant_payload"]!["run_history"]!;
        var combats = payload["applicant_payload"]!["mod_payload"]!["combats"]!.AsObject();
        Require(combats.Count > 0, "Completed AutoSlay omitted combat measurements.");
        int nativeCombats = history["map_point_history"]!.AsArray().SelectMany(act => act!.AsArray())
            .SelectMany(floor => floor!["rooms"]!.AsArray())
            .Count(room => room!["room_type"]!.GetValue<string>() is "monster" or "elite" or "boss");
        Require(combats.Count == nativeCombats, $"Combat measurement coverage differs: {combats.Count}/{nativeCombats}.");
        Require(combats.Any(combat => combat.Value!["players"]!.AsArray().Any(player =>
            player!["cards"]!.AsObject().Any(card => card.Value!["started"]!.GetValue<int>() > 0))),
            "Completed AutoSlay did not measure actual card plays.");
        _checkpoints.Write("telemetry.captured", data: new JsonObject { ["combats"] = combats.Count, ["nativeCombats"] = nativeCombats });
    }

    private sealed class LocalTelemetryCapture(string path) : ITelemetryAdapter
    {
        public string AdapterId => "smoke_local_file";
        public string EndpointDescription => "isolated smoke fixture";
        public TaskCompletionSource<JsonNode> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CaptureCount { get; private set; }

        public ValueTask<TelemetrySendResult> SendAsync(TelemetryApplicant applicant,
            IReadOnlyList<TelemetryEnvelope> events, CancellationToken cancellationToken = default)
        {
            foreach (TelemetryEnvelope item in events.Where(item => item.EventName == "run_history.completed"))
            {
                // Exercise the actual PostHog wire mapping without a production network request.
                var buildProperties = typeof(PostHogTelemetryAdapter).GetMethod("BuildProperties", BindingFlags.Static | BindingFlags.NonPublic)!;
                var properties = JsonSerializer.SerializeToNode(buildProperties.Invoke(null, [item]))!.AsObject();
                File.WriteAllText(path, new JsonArray(new JsonObject
                {
                    ["event"] = item.EventName, ["timestamp"] = item.TimestampUtc.ToString("O"),
                    ["distinct_id"] = properties["anonymous_install_id"]!.DeepClone(), ["properties"] = properties
                }).ToJsonString());
                CaptureCount++;
                Captured.TrySetResult(item.Payload!.DeepClone());
            }
            return ValueTask.FromResult(TelemetrySendResult.Ok());
        }
    }
}
