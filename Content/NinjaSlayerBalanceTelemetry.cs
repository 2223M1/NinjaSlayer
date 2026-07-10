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
                ApplicantId = Entry.ModId,
                OwnerModId = Entry.ModId,
                DisplayName = Entry.ModId,
                DisplayNameText = ModSettingsText.Literal("NinjaSlayer"),
                Adapter = new PostHogTelemetryAdapter(
                    host: "https://ninjamod2-data.ninja-data.workers.dev",
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
                            )
                    ),
                ],
            }
        );

        Client = TelemetryApi.GetClient(Entry.ModId);
    }

    public class NinjaSlayerBalanceContributionProvider : ITelemetryContributionProvider
    {
        public string ContributorModId => Entry.ModId;

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
