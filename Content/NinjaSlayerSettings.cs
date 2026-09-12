using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace NinjaSlayer.Content;

public static class NinjaSlayerSettings
{
    private const string DataKey = "ninja_slayer_settings";
    private const string SettingsTable = "settings_ui";

    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool> _forceAllEventsOnce = null!;
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool>? _freeControl;

    public static bool ForceAllEventsOnce => _forceAllEventsOnce.Read();
    internal static bool FreeControlEnabled => _freeControl?.Read() == true;

    public static void Register(string modId)
    {
        ModDataStore.For(modId).Register<NinjaSlayerSettingsData>(
            key: DataKey,
            fileName: "settings.json",
            scope: SaveScope.Global,
            syncToCloud: false,
            defaultFactory: static () => new NinjaSlayerSettingsData(),
            autoCreateIfMissing: true);

        _forceAllEventsOnce = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId,
            DataKey,
            SaveScope.Global,
            static settings => settings.ForceAllEventsOnce,
            static (settings, value) => settings.ForceAllEventsOnce = value);

        _freeControl = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId, DataKey, SaveScope.Global,
            static settings => settings.FreeControlEnabled,
            static (settings, value) => settings.FreeControlEnabled = value);

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
            .AddSection("validation", section => section
                .WithReadOnlyOnHostSurfaces(ModSettingsHostSurface.RunPause | ModSettingsHostSurface.CombatPause)
                .WithTitle(Text(
                    "NINJA_SLAYER_SETTINGS_VALIDATION_SECTION_TITLE",
                    "Validation"))
                .AddToggle(
                    "force_all_events_once",
                    Text(
                        "NINJA_SLAYER_SETTINGS_FORCE_ALL_EVENTS_ONCE_TITLE",
                        "Force each event once"),
                    _forceAllEventsOnce,
                    Text(
                        "NINJA_SLAYER_SETTINGS_FORCE_ALL_EVENTS_ONCE_DESCRIPTION",
                        "Applies to subsequently created single-player Ninja Slayer runs.")))
            .AddSection("hidden", section => section
                .WithTitle(Text("NINJA_SLAYER_SETTINGS_HIDDEN_TITLE", "Hidden features"))
                .AddToggle("free_control",
                    Text("NINJA_SLAYER_SETTINGS_FREE_CONTROL_TITLE", "Free control (playground)"),
                    _freeControl,
                    Text("NINJA_SLAYER_SETTINGS_FREE_CONTROL_DESCRIPTION",
                        "Single-player play phase only. Free movement and attacks; returns home when the turn ends."))));
    }

    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.LocString(SettingsTable, key, fallback);
}
