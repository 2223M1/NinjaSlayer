using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Content;

public static class NinjaSlayerBalanceTelemetry
{
    public const string BalanceContextContributionId = "ninja_slayer_balance_context";

    private static ITelemetryClient Client = null!;
    private static bool _initialized;

    public static void Register()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

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
                        captureFilter: evt =>
                            !evt.IsAbandoned
                            && evt.Run.Players.Any(player =>
                                player.CharacterId == ModelDb.Character<NinjaSlayerCharacter>().Id
                                || player.CharacterId == ModelDb.Character<NinjaSlayerDebugCharacter>().Id
                            )
                    ),
                ],
            }
        );

        Client = TelemetryApi.GetClient(NinjaSlayerIds.ModId);
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
