using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Content;

public static class NinjaSlayerSettings
{
    private const string DataKey = "ninja_slayer_settings";
    private const string SettingsTable = "settings_ui";

    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool>? _freeControl;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool>? _narration;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool> _radio = null!;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool> _mangaSelectPortrait = null!;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool>? _briefBossGreeting;

    internal static bool FreeControlEnabled => _freeControl?.Read() == true;
    internal static bool NarrationEnabled => _narration?.Read() ?? true;
    internal static bool MangaSelectPortraitEnabled => _mangaSelectPortrait.Read();
    internal static bool BriefBossGreetingEnabled => _briefBossGreeting?.Read() == true;
    internal static event Action? SelectPortraitChanged;

    internal static void CompleteFirstBossGreeting()
    {
        var store = ModDataStore.For(NinjaSlayerIds.ModId);
        var settings = store.Get<NinjaSlayerSettingsData>(DataKey);
        if (settings.BriefBossGreetingEnabled.HasValue) return;
        settings.CompleteFirstBossGreeting();
        store.Save(DataKey);
    }

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

        _briefBossGreeting = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId, DataKey, SaveScope.Global,
            static settings => settings.BriefBossGreetingEnabled == true,
            static (settings, value) => settings.BriefBossGreetingEnabled = value);

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
                    Text("NINJA_SLAYER_SETTINGS_MANGA_SELECT_TITLE", "Manga character portrait"),
                    _mangaSelectPortrait,
                    Text("NINJA_SLAYER_SETTINGS_MANGA_SELECT_DESCRIPTION",
                        "Use the manga portrait on the character selection screen. Turn off to use the official website artwork. Combat appearance is unchanged.")))
            .AddSection("combat_presentation", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_COMBAT_PRESENTATION_TITLE", "Battle presentation"))
                .AddToggle("brief_boss_greeting",
                    Text("NINJA_SLAYER_SETTINGS_BRIEF_BOSS_GREETING_TITLE", "Brief greetings"),
                    _briefBossGreeting,
                    Text("NINJA_SLAYER_SETTINGS_BRIEF_BOSS_GREETING_DESCRIPTION",
                        "Turns on after your first full greeting. Manual choices are saved and apply from the next greeting. Press Space during a full greeting to shorten it. In multiplayer, the host chooses the setting and is the only player who can shorten a greeting.")))
            .AddSection("audio", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_AUDIO_TITLE", "Audio"))
                .AddToggle("narration",
                    Text("NINJA_SLAYER_SETTINGS_NARRATION_TITLE", "Narrator voice"),
                    _narration,
                    Text("NINJA_SLAYER_SETTINGS_NARRATION_DESCRIPTION", "Only affects the narrator. Character voices and music stay on."))
                .AddToggle("radio", Text("NINJA_SLAYER_SETTINGS_RADIO_TITLE", "Ninja Slayer Radio"),
                    _radio, Text("NINJA_SLAYER_SETTINGS_RADIO_DESCRIPTION",
                        "Tracks have not been added yet. This switch does not currently change the music.")))
            .AddSection("hidden", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_HIDDEN_TITLE", "Extras"))
                .AddToggle("free_control",
                    Text("NINJA_SLAYER_SETTINGS_FREE_CONTROL_TITLE", "Free control"),
                    _freeControl,
                    Text("NINJA_SLAYER_SETTINGS_FREE_CONTROL_DESCRIPTION",
                        "Single-player only, during your action phase. Move, drag and attack freely, then return when the turn ends. Runs that use this option are excluded from balance statistics."))));
    }

    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.LocString(SettingsTable, key, fallback);
}
