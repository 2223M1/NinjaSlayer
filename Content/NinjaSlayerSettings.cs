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
    private static ModSettingsValueBinding<NinjaSlayerSettingsData, bool> _useRedesignV1 = null!;

    public static bool ForceAllEventsOnce => _forceAllEventsOnce.Read();
    public static bool UseRedesignV1 => _useRedesignV1.Read();

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

        _useRedesignV1 = new ModSettingsValueBinding<NinjaSlayerSettingsData, bool>(
            modId,
            DataKey,
            SaveScope.Global,
            static settings => settings.UseRedesignV1,
            static (settings, value) => settings.UseRedesignV1 = value);

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
                        "Applies to subsequently created single-player Ninja Slayer runs.")))
            .AddSection("experiments", section => section
                .WithTitle(Text(
                    "NINJA_SLAYER_SETTINGS_EXPERIMENTS_SECTION_TITLE",
                    "Experiments"))
                .AddToggle(
                    "use_redesign_v1",
                    Text(
                        "NINJA_SLAYER_SETTINGS_USE_REDESIGN_V1_TITLE",
                        "Redesigned deck experiment"),
                    _useRedesignV1,
                    Text(
                        "NINJA_SLAYER_SETTINGS_USE_REDESIGN_V1_DESCRIPTION",
                        "Uses the redesigned Ninja Slayer deck in subsequently created runs. Existing runs are unchanged."))));
    }

    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.LocString(SettingsTable, key, fallback);
}
