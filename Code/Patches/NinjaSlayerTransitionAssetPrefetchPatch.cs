using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionAssetRetentionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_asset_prefetch_retention";

    public static string Description =>
        "Retain only the NinjaSlayer run assets owned by the pending transition prefetch lease.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(AssetCache), nameof(AssetCache.UnloadAssets), [typeof(IEnumerable<string>)])];

    public static void Prefix(ref IEnumerable<string> assetsToUnloadSet)
    {
        if (NinjaSlayerPatchCapabilities.TransitionAssetPrefetchEnabled)
        {
            assetsToUnloadSet = NinjaSlayerRunAssetPrefetcher.FilterAssetsToUnload(assetsToUnloadSet);
        }
    }
}

public sealed class NinjaSlayerTransitionMainMenuAssetPrefetchPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_main_menu_asset_prefetch";

    public static string Description =>
        "Prefetch NinjaSlayer run candidates, or the saved NinjaSlayer run, while the main menu is idle.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NMainMenu), nameof(NMainMenu.RefreshButtons))];

    public static void Postfix(NMainMenu __instance)
    {
        if (!NinjaSlayerPatchCapabilities.TransitionAssetPrefetchEnabled)
        {
            return;
        }

        NinjaSlayerRunAssetPrefetcher.ResetForMainMenu();
        if (GameCompatibility.AssetLoading.TryGetPendingRunSave(__instance, out SerializableRun? save)
            && save is not null)
        {
            NinjaSlayerRunAssetPrefetcher.PrefetchSavedMetadata(save);
        }
        else
        {
            NinjaSlayerRunAssetPrefetcher.PrefetchMainMenuCandidates();
        }
    }
}

public sealed class NinjaSlayerTransitionCharacterAssetPrefetchPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_character_asset_prefetch";

    public static string Description =>
        "Refresh the run asset prefetch after NinjaSlayer is selected.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NCharacterSelectScreen),
            nameof(NCharacterSelectScreen.SelectCharacter),
            [typeof(NCharacterSelectButton), typeof(CharacterModel)])
    ];

    public static void Postfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        if (NinjaSlayerPatchCapabilities.TransitionAssetPrefetchEnabled
            && characterModel is INinjaSlayerCharacter)
        {
            NinjaSlayerRunAssetPrefetcher.PrefetchSelection(
                characterModel,
                __instance.Lobby.NetService.Type.IsMultiplayer());
        }
    }
}

public sealed class NinjaSlayerTransitionEmbarkAssetPrefetchPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_embark_asset_prefetch";

    public static string Description =>
        "Resolve the selected Act and refresh the NinjaSlayer prefetch before embark playback.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCharacterSelectScreen), "StartNewSingleplayerRun", [typeof(string), typeof(List<ActModel>)]),
        new(typeof(NCharacterSelectScreen), "StartNewMultiplayerRun", [typeof(string), typeof(List<ActModel>)])
    ];

    public static void Prefix(NCharacterSelectScreen __instance, List<ActModel> acts)
    {
        if (!NinjaSlayerPatchCapabilities.TransitionAssetPrefetchEnabled)
        {
            return;
        }

        CharacterModel[] characters = __instance.Lobby.Players
            .Select(player => player.character)
            .ToArray();
        NinjaSlayerRunAssetPrefetcher.PrefetchEmbark(
            characters,
            __instance.Lobby.NetService.Type.IsMultiplayer(),
            acts);
    }
}

public sealed class NinjaSlayerTransitionSavedRunAssetPrefetchPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_saved_run_asset_prefetch";

    public static string Description =>
        "Prefetch the exact current Act and restored room after a NinjaSlayer save is initialized.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RunManager), "InitializeSavedRun", [typeof(SerializableRun)])];

    public static void Postfix(RunManager __instance, SerializableRun save)
    {
        if (NinjaSlayerPatchCapabilities.TransitionAssetPrefetchEnabled
            && __instance.State is { } runState)
        {
            NinjaSlayerRunAssetPrefetcher.PrefetchSavedRun(runState, save.PreFinishedRoom);
        }
    }
}
