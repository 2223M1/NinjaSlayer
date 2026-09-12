using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Content;

internal static class NinjaSlayerTelemetryConsent
{
    internal const string ObservatoryUrl = "https://2223m1.github.io/NinjaSlayer/";
    private const string SettingsKey = "ninja_slayer_settings";

    // RitsuLib exposes writes and IsEnabled, but no public read distinguishing Unknown from Denied.
    private static readonly MethodInfo ReadConsent = AccessTools.Method(
        typeof(TelemetryApi).Assembly.GetType("STS2RitsuLib.Telemetry.TelemetryConsentStore", throwOnError: true),
        "GetApplicantConsent", [typeof(string)]) ?? throw new MissingMethodException("TelemetryConsentStore.GetApplicantConsent");
    private static readonly PropertyInfo ConsentProperty = ReadConsent.ReturnType.GetProperty("Consent")
        ?? throw new MissingMemberException("TelemetryApplicantConsent.Consent");
    private static bool _handledMainMenu;

    internal static TelemetryConsentState State => (TelemetryConsentState)ConsentProperty.GetValue(
        ReadConsent.Invoke(null, [NinjaSlayerIds.ModId]))!;

    internal static bool SwitchEnabled => State == TelemetryConsentState.Unknown
        || TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId);

    internal static bool ReplayEnabled => ModDataStore.For(NinjaSlayerIds.ModId)
        .Get<NinjaSlayerSettingsData>(SettingsKey).PublicReplayEnabled
        && TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(NinjaSlayerBalanceTelemetry.ReplayRequestId);

    internal static void SetEnabled(bool enabled)
    {
        SetRequests(enabled, ReplayEnabled);
        NinjaSlayer.Code.Telemetry.NinjaSlayerCombatTelemetry.ConsentChanged();
    }

    internal static void SetReplayEnabled(bool enabled)
    {
        bool wasEnabled = ReplayEnabled;
        bool balance = TelemetryApi.GetClient(NinjaSlayerIds.ModId)
            .IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId);
        ModDataStore.For(NinjaSlayerIds.ModId).Modify<NinjaSlayerSettingsData>(SettingsKey,
            data => data.PublicReplayEnabled = enabled);
        SetRequests(balance, enabled);
        if (enabled && !wasEnabled) NinjaSlayer.Code.Telemetry.NinjaSlayerReplay.BeginConsent();
        NinjaSlayer.Code.Telemetry.NinjaSlayerCombatTelemetry.ConsentChanged();
    }

    private static void SetRequests(bool balance, bool replay)
    {
        List<string> requests = [];
        if (balance) requests.Add(NinjaSlayerBalanceTelemetry.BalanceRequestId);
        if (replay) requests.Add(NinjaSlayerBalanceTelemetry.ReplayRequestId);
        RitsuLibFramework.SetTelemetryApplicantConsent(NinjaSlayerIds.ModId,
            requests.Count > 0 ? TelemetryConsentState.Granted : TelemetryConsentState.Denied, requests);
    }

    internal static string StatusText() => Text(State switch
    {
        TelemetryConsentState.Unknown => "PENDING",
        _ when SwitchEnabled => "ENABLED",
        _ => "DISABLED"
    }).Resolve();

    internal static string ReplayStatusText() => Text("REPLAY_" + NinjaSlayer.Code.Telemetry.NinjaSlayerReplay.Status.ToUpperInvariant()).Resolve();

    internal static void Register() => RitsuLibFramework.SubscribeLifecycle<MainMenuReadyEvent>(_ => OnMainMenu());

    private static void OnMainMenu()
    {
        if (_handledMainMenu) return;
        _handledMainMenu = true;
        if (State != TelemetryConsentState.Unknown) return;
        var store = ModDataStore.For(NinjaSlayerIds.ModId);
        if (store.Get<NinjaSlayerSettingsData>(SettingsKey).TelemetryNoticeShown)
        {
            SetEnabled(true);
            return;
        }

        ModSettingsUiFactory.ShowStyledConfirm(NGame.Instance ?? throw new InvalidOperationException("Main menu has no game node."),
            Text("NOTICE_TITLE").Resolve(), Text("NOTICE_BODY").Resolve(),
            Text("DECLINE").Resolve(), Text("ACCEPT").Resolve(), false,
            () => SetEnabled(true), showCancel: true, onCancel: () => SetEnabled(false),
            escapeTriggersCancel: false);
        store.Modify<NinjaSlayerSettingsData>(SettingsKey, data => data.TelemetryNoticeShown = true);
    }

    internal static ModSettingsText Text(string suffix) => ModSettingsText.LocString(
        new LocString("settings_ui", "NINJA_SLAYER_TELEMETRY_" + suffix));
}
