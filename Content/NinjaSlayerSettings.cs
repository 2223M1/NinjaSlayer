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

    public static bool ForceAllEventsOnce => _forceAllEventsOnce.Read();

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
            .WithReadOnlyOnHostSurfaces(
                ModSettingsHostSurface.RunPause
                | ModSettingsHostSurface.CombatPause)
            .AddSection("validation", section => section
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
                        "Applies to subsequently created single-player Ninja Slayer runs."))));
    }

    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.LocString(SettingsTable, key, fallback);
}
