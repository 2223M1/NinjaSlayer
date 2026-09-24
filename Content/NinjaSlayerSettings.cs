using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace NinjaSlayer.Content;

public static class NinjaSlayerSettings
{
    private const string DataKey = "ninja_slayer_settings";
    private const string SettingsTable = "settings_ui";

    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool>? _freeControl;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool>? _narration;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool> _radio = null!;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool> _mangaSelectPortrait = null!;

    internal static bool FreeControlEnabled => _freeControl?.Read() == true;
    internal static bool NarrationEnabled => _narration?.Read() ?? true;
    internal static bool MangaSelectPortraitEnabled => _mangaSelectPortrait.Read();
    internal static event Action? SelectPortraitChanged;

    public static void Register(string modId)
    {
        ModDataStore.For(modId).Register<NinjaSlayerSettingsData>(
            key: DataKey,
            fileName: "settings.json",
            scope: SaveScope.Global,
            syncToCloud: false,
            defaultFactory: static () => new NinjaSlayerSettingsData(),
            autoCreateIfMissing: true);

        _freeControl = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId, DataKey, SaveScope.Global,
            static settings => settings.FreeControlEnabled,
            static (settings, value) => settings.FreeControlEnabled = value);

        _narration = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId, DataKey, SaveScope.Global,
            static settings => settings.NarrationEnabled,
            static (settings, value) => settings.NarrationEnabled = value);

        _radio = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId, DataKey, SaveScope.Global,
            static settings => settings.RadioEnabled,
            static (settings, value) => settings.RadioEnabled = value);

        _mangaSelectPortrait = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId, DataKey, SaveScope.Global,
            static settings => settings.MangaSelectPortraitEnabled,
            static (settings, value) =>
            {
                settings.MangaSelectPortraitEnabled = value;
                SelectPortraitChanged?.Invoke();
            });

        RitsuLibFramework.RegisterModSettings(modId, page => page
            .WithTitle(Text(
                "NINJA_SLAYER_SETTINGS_PAGE_TITLE",
                "Ninja Slayer Settings"))
            .WithModDisplayName(Text(
                "NINJA_SLAYER_SETTINGS_PAGE_TITLE",
                "Ninja Slayer Settings"))
            .WithVisibleOnHostSurfaces(
                ModSettingsHostSurface.MainMenu
                | ModSettingsHostSurface.RunPause
                | ModSettingsHostSurface.CombatPause)
            .AddSection("telemetry", section => section
                .WithTitle(NinjaSlayerTelemetryConsent.Text("TITLE"))
                .AddToggle("balance_telemetry", NinjaSlayerTelemetryConsent.Text("TOGGLE"),
                    ModSettingsBindings.Callback(modId, "balance_telemetry",
                        () => NinjaSlayerTelemetryConsent.SwitchEnabled, NinjaSlayerTelemetryConsent.SetEnabled,
                        static () => { }), // The native consent setter persists immediately.
                    NinjaSlayerTelemetryConsent.Text("DESCRIPTION"))
                .AddParagraph("telemetry_status", ModSettingsText.DynamicFullRefreshOnly(NinjaSlayerTelemetryConsent.StatusText))
                .AddToggle("public_replay", NinjaSlayerTelemetryConsent.Text("REPLAY_TOGGLE"),
                    ModSettingsBindings.Callback(modId, "public_replay",
                        () => NinjaSlayerTelemetryConsent.ReplayEnabled, NinjaSlayerTelemetryConsent.SetReplayEnabled,
                        static () => { }), NinjaSlayerTelemetryConsent.Text("REPLAY_DESCRIPTION"))
                .AddParagraph("replay_status", ModSettingsText.DynamicFullRefreshOnly(NinjaSlayerTelemetryConsent.ReplayStatusText))
                .AddButton("observatory", NinjaSlayerTelemetryConsent.Text("WEBSITE"),
                    NinjaSlayerTelemetryConsent.Text("OPEN_WEBSITE"),
                    () => Godot.OS.ShellOpen(NinjaSlayerTelemetryConsent.ObservatoryUrl)))
            .AddSection("appearance", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_APPEARANCE_TITLE", "Appearance"))
                .AddToggle("manga_select_portrait",
                    Text("NINJA_SLAYER_SETTINGS_MANGA_SELECT_TITLE", "Manga selection portrait"),
                    _mangaSelectPortrait,
                    Text("NINJA_SLAYER_SETTINGS_MANGA_SELECT_DESCRIPTION",
                        "Off: animated official front portrait. On: manga portrait with the original hand animation. Only affects character selection.")))
            .AddSection("audio", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_AUDIO_TITLE", "Audio"))
                .AddToggle("narration",
                    Text("NINJA_SLAYER_SETTINGS_NARRATION_TITLE", "Narration"),
                    _narration,
                    Text("NINJA_SLAYER_SETTINGS_NARRATION_DESCRIPTION", "Play narrator voice lines."))
                .AddToggle("radio", Text("NINJA_SLAYER_SETTINGS_RADIO_TITLE", "Ninja Slayer Radio"),
                    _radio, Text("NINJA_SLAYER_SETTINGS_RADIO_DESCRIPTION",
                        "Selected music will replace most game tracks. Tracks are coming later; current music is unchanged.")))
            .AddSection("hidden", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_HIDDEN_TITLE", "Hidden features"))
                .AddToggle("free_control",
                    Text("NINJA_SLAYER_SETTINGS_FREE_CONTROL_TITLE", "Ether Reflux"),
                    _freeControl,
                    Text("NINJA_SLAYER_SETTINGS_FREE_CONTROL_DESCRIPTION",
                        "Single-player play phase only. Free movement and attacks; returns home when the turn ends."))));
    }

    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.LocString(SettingsTable, key, fallback);
}
