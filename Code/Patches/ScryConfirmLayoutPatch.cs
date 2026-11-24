using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using NinjaSlayer.Code.Commands;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ScryConfirmLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_scry_confirm_layout";
    public static string Description => "Keep Scry confirmation clear of the combat exhaust-pile button revealed when the selection closes.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NSimpleCardSelectScreen), nameof(NSimpleCardSelectScreen.Create),
            [typeof(IReadOnlyList<CardModel>), typeof(CardSelectorPrefs)])];

    public static void Postfix(CardSelectorPrefs prefs, NSimpleCardSelectScreen __result)
    {
        if (prefs.Prompt.LocTable != "card_selection" || prefs.Prompt.LocEntryKey != ScryCmd.SelectionPromptKey)
            return;

        __result.GetNode<NCardGrid>("%CardGrid").SetMeta(ScryGridHighlightPatch.ScryGrid, true);

        // Set scene offsets before NConfirmButton._Ready caches its animated positions.
        // Native show/hide, resizing, hotkeys and multiplayer selection remain unchanged.
        var confirm = __result.GetNode<Control>("%Confirm");
        confirm.OffsetTop -= 120f;
        confirm.OffsetBottom -= 120f;
    }
}

public sealed class ScryGridHighlightPatch : IPatchMethod
{
    internal static readonly StringName ScryGrid = new("ninjaslayer_scry_grid");
    public static string PatchId => "ninjaslayer_scry_grid_highlight";
    public static string Description => "Restore selected card outlines when Scry rebuilds its visible grid.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NCardGrid), "InitGrid", Type.EmptyTypes)];

    public static void Postfix(NCardGrid __instance, List<CardModel> ____highlightedCards)
    {
        if (____highlightedCards.Count == 0 || !__instance.HasMeta(ScryGrid)) return;

        // InitGrid replaces holders without repainting the remembered highlights.
        // HighlightCard would append the same model again and break deselection.
        foreach (var holder in __instance.CurrentlyDisplayedCardHolders)
            if (____highlightedCards.Contains(holder.CardModel))
                holder.CardNode!.CardHighlight.AnimShow();
    }
}
