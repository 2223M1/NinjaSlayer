using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static NGame _consentGame = null!;
    private static Action? _acceptTelemetry, _declineTelemetry;
    private static int _consentPrompts;
    private static FieldInfo _nativeConsentDocument = null!;
    private static string _savedConsent = "";

    private static void VerifyTelemetryConsent()
    {
        Type product = typeof(NinjaSlayerSettings).Assembly.GetType("NinjaSlayer.Content.NinjaSlayerTelemetryConsent", true)!;
        var onMenu = AccessTools.Method(product, "OnMainMenu").CreateDelegate<Action>();
        var setEnabled = AccessTools.Method(product, "SetEnabled").CreateDelegate<Action<bool>>();
        var switchEnabled = AccessTools.PropertyGetter(product, "SwitchEnabled").CreateDelegate<Func<bool>>();
        var processHandled = AccessTools.Field(product, "_handledMainMenu");
        Type nativeStore = typeof(TelemetryApi).Assembly.GetType("STS2RitsuLib.Telemetry.TelemetryConsentStore", true)!;
        _nativeConsentDocument = AccessTools.Field(nativeStore, "_document");
        object? originalConsent = _nativeConsentDocument.GetValue(null);
        var patches = new Harmony("NinjaSlayer.OrbContracts.TelemetryConsent");
        _consentGame = new NGame();
        patches.Patch(AccessTools.PropertyGetter(typeof(NGame), nameof(NGame.Instance)), prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ConsentGame)));
        patches.Patch(AccessTools.Method(typeof(ModSettingsUiFactory), "ShowStyledConfirm"), prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(CaptureConsentPrompt)));
        // Exercise the native consent model and setter while keeping this contract's data in memory.
        patches.Patch(AccessTools.Method(nativeStore, "Save"), prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(SaveConsentFixture)));
        try
        {
            using (RitsuLibFramework.BeginModDataRegistration("NinjaSlayer")) NinjaSlayerSettings.Register("NinjaSlayer");
            TelemetryRegistry.RegisterApplicant(new TelemetryApplicant
            {
                ApplicantId = "NinjaSlayer", OwnerModId = "NinjaSlayer", DisplayName = "Ninja Slayer",
                Adapter = new ConsentTestAdapter(), Requests = [new TelemetryRequest
                { RequestId = NinjaSlayerBalanceTelemetry.BalanceRequestId, Category = TelemetryDataCategory.RunHistory, Description = "Consent contract" }]
            });
            LocManager.Instance.GetTable("settings_ui").MergeWith(JsonSerializer.Deserialize<Dictionary<string, string>>(
                System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://../../NinjaSlayer/localization/zhs/settings_ui.json")))!);
            var store = ModDataStore.For("NinjaSlayer");
            bool enabled() => TelemetryApi.GetClient("NinjaSlayer").IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId);
            void newProcess() { processHandled.SetValue(null, false); onMenu(); }
            void reset()
            {
                _nativeConsentDocument.SetValue(null, Activator.CreateInstance(_nativeConsentDocument.FieldType));
                store.Modify<NinjaSlayerSettingsData>("ninja_slayer_settings", data => data.TelemetryNoticeShown = false);
                _consentPrompts = 0;
            }

            reset();
            newProcess();
            Require(_consentPrompts == 1 && switchEnabled() && !enabled(), "First notice must precede default-on delivery.");
            onMenu();
            Require(_consentPrompts == 1 && !enabled(), "Returning to the menu must not count as restarting the game.");
            newProcess();
            Require(_consentPrompts == 1 && enabled(), "Dismissed notice should enable only on the next process.");
            setEnabled(false);
            _nativeConsentDocument.SetValue(null, JsonSerializer.Deserialize(_savedConsent, _nativeConsentDocument.FieldType));
            newProcess();
            Require(!enabled() && !switchEnabled(), "An explicit opt-out must survive serialization and restart.");

            reset(); newProcess(); _acceptTelemetry!();
            Require(enabled(), "Accept must enable the current session immediately.");
            setEnabled(false);
            Require(!enabled(), "The settings switch must disable delivery immediately.");
            reset(); newProcess(); _declineTelemetry!(); newProcess();
            Require(!enabled() && _consentPrompts == 1, "Declining the notice must remain disabled after restarting.");
            reset();
            RitsuLibFramework.SetTelemetryApplicantConsent("NinjaSlayer", TelemetryConsentState.Denied);
            newProcess();
            Require(!enabled() && _consentPrompts == 0, "Existing native rejection must be preserved without prompting.");
            reset();
            RitsuLibFramework.SetTelemetryApplicantConsent("NinjaSlayer", TelemetryConsentState.Granted, ["run_history"]);
            newProcess();
            Require(!enabled() && _consentPrompts == 0, "Historical run_history permission must not grant balance_runs.");
            GD.Print("PASS telemetry notice, immediate toggle, dismissal, native rejection and saved consent");
        }
        finally
        {
            patches.UnpatchAll(patches.Id);
            _nativeConsentDocument.SetValue(null, originalConsent);
            _consentGame.Free();
        }
    }

    private static bool ConsentGame(ref NGame __result) { __result = _consentGame; return false; }
    private static bool CaptureConsentPrompt(Action onConfirm, Action? onCancel, bool escapeTriggersCancel)
    {
        Require(!escapeTriggersCancel, "Dismissing the notice must not be treated as explicit rejection.");
        _acceptTelemetry = onConfirm; _declineTelemetry = onCancel; _consentPrompts++;
        return false;
    }
    private static bool SaveConsentFixture()
    {
        _savedConsent = JsonSerializer.Serialize(_nativeConsentDocument.GetValue(null), _nativeConsentDocument.FieldType);
        return false;
    }
    private sealed class ConsentTestAdapter : ITelemetryAdapter
    {
        public string AdapterId => "consent_contract";
        public string EndpointDescription => "in-memory contract";
        public ValueTask<TelemetrySendResult> SendAsync(TelemetryApplicant applicant, IReadOnlyList<TelemetryEnvelope> events,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(TelemetrySendResult.Ok());
    }
}
