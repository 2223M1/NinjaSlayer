using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Code.Telemetry;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Content;

public static class NinjaSlayerBalanceTelemetry
{
    public const string BalanceContextContributionId = "ninja_slayer_balance_context";

    private static readonly NinjaSlayerTelemetryIdentityTracker IdentityTracker = new();
    private static ITelemetryClient Client = null!;
    private static bool _initialized;

    public static void Register()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(evt => BeginRun(evt.RunState));
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(evt => BeginRun(evt.RunState));
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(ObserveRunEnded);
        RitsuLibFramework.SubscribeLifecycle<MainMenuReadyEvent>(_ => IdentityTracker.Clear());

        RitsuLibFramework.RegisterTelemetryContributionProvider(new NinjaSlayerBalanceContributionProvider());

        TelemetryRegistry.RegisterApplicant(
            new TelemetryApplicant
            {
                ApplicantId = NinjaSlayerIds.ModId,
                OwnerModId = NinjaSlayerIds.ModId,
                DisplayName = NinjaSlayerIds.ModId,
                DisplayNameText = ModSettingsText.Literal("NinjaSlayer"),
                Adapter = new PostHogTelemetryAdapter(
                    host: "https://ninja-slayer-telemetry.theonetrue2223.workers.dev",
                    projectApiKey: "proxy"
                ),
                Requests =
                [
                    TelemetryRequest.RunHistory(
                        ModSettingsText.Literal(
                            "Completed run history, including card reward choices, final decks and victory results, for balance analysis."),
                        sharedContributionSubscriptions: [BalanceContextContributionId],
                        captureFilter: ShouldCaptureRunHistory
                    ),
                ],
            }
        );

        Client = TelemetryApi.GetClient(NinjaSlayerIds.ModId);
    }

    internal static void RefreshIdentity(RunState runState)
    {
        try
        {
            IdentityTracker.Refresh(runState, LocalContext.NetId, BuildPlayerIdentities(runState.Players));
        }
        catch (Exception exception)
        {
            IdentityTracker.Clear();
            Entry.Logger.Warn($"Failed to refresh NinjaSlayer telemetry identity: {exception.Message}");
        }
    }

    internal static void ClearIdentity() => IdentityTracker.Clear();

    private static void BeginRun(RunState runState)
    {
        IdentityTracker.BeginRun(runState);
        RefreshIdentity(runState);
    }

    private static bool ShouldCaptureRunHistory(RunEndedEvent evt)
    {
        if (!NinjaSlayerPatchCapabilities.TelemetryIdentityEnabled)
        {
            return false;
        }

        return IdentityTracker.TryCaptureCompletedRun(
            evt.Run,
            evt.IsAbandoned,
            LocalContext.NetId,
            BuildPlayerIdentities(evt.Run.Players));
    }

    private static void ObserveRunEnded(RunEndedEvent evt)
    {
        IdentityTracker.ObserveRunEnded(
            evt.Run,
            evt.IsAbandoned,
            LocalContext.NetId,
            BuildPlayerIdentities(evt.Run.Players));
    }

    private static NinjaSlayerTelemetryPlayerIdentity[] BuildPlayerIdentities(IEnumerable<Player> players) =>
        players.Select(player => new NinjaSlayerTelemetryPlayerIdentity(
                player.NetId,
                player.Character switch
                {
                    NinjaSlayerCharacter => NinjaSlayerTelemetryCharacterKind.Official,
                    NinjaSlayerDebugCharacter => NinjaSlayerTelemetryCharacterKind.Debug,
                    null => NinjaSlayerTelemetryCharacterKind.Unknown,
                    _ => NinjaSlayerTelemetryCharacterKind.Other
                }))
            .ToArray();

    private static NinjaSlayerTelemetryPlayerIdentity[] BuildPlayerIdentities(
        IEnumerable<SerializablePlayer> players)
    {
        ModelId officialId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        ModelId debugId = ModelDb.Character<NinjaSlayerDebugCharacter>().Id;
        return players.Select(player => new NinjaSlayerTelemetryPlayerIdentity(
                player.NetId,
                player.CharacterId switch
                {
                    null => NinjaSlayerTelemetryCharacterKind.Unknown,
                    { } characterId when characterId == officialId => NinjaSlayerTelemetryCharacterKind.Official,
                    { } characterId when characterId == debugId => NinjaSlayerTelemetryCharacterKind.Debug,
                    _ => NinjaSlayerTelemetryCharacterKind.Other
                }))
            .ToArray();
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
                ["balance_schema"] = "ninja_slayer_run_history_v1",
            };
        }
    }
}
