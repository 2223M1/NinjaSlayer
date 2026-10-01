using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models;
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

        // Set scene offsets before NConfirmButton._Ready caches its animated positions.
        // Native show/hide, resizing, hotkeys and multiplayer selection remain unchanged.
        var confirm = __result.GetNode<Control>("%Confirm");
        confirm.OffsetTop -= 120f;
        confirm.OffsetBottom -= 120f;
    }
}
