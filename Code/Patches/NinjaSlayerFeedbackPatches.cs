using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Feedback;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerFeedbackOpenerPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_feedback_f2_route";

    public static string Description => "Route only a local NinjaSlayer player's in-run F2 feedback to the mod author.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NFeedbackScreenOpener), nameof(NFeedbackScreenOpener._Input), [typeof(InputEvent)])];

    public static bool Prefix(NFeedbackScreenOpener __instance, InputEvent inputEvent)
    {
        if (!NinjaSlayerPatchCapabilities.FeedbackEnabled ||
            inputEvent is not InputEventKey { Pressed: not false, Keycode: Key.F2 }
            || !IsLocalNinjaSlayer()
            || NGame.Instance is not { } game
            || game.GetOrCreateFeedbackScreen().Visible
            || NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCapstoneSubmenuStack
            {
                ScreenType: NetScreenType.Feedback
            })
        {
            return true;
        }

        NinjaSlayerFeedbackSession.Begin();
        TaskHelper.RunSafely(OpenFeedbackScreen(__instance));
        return false;
    }

    private static bool IsLocalNinjaSlayer()
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        return runState != null && LocalContext.GetMe(runState)?.Character is INinjaSlayerCharacter;
    }

    private static async Task OpenFeedbackScreen(NFeedbackScreenOpener opener)
    {
        try
        {
            await opener.OpenFeedbackScreen();
        }
        catch
        {
            NinjaSlayerFeedbackSession.Reset();
            throw;
        }
    }
}

public sealed class NinjaSlayerFeedbackOpenPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_feedback_form_labels";
    public static string Description => "Label NinjaSlayer F2 feedback with its actual recipient.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NSendFeedbackScreen), nameof(NSendFeedbackScreen.Open))];

    public static void Postfix(NSendFeedbackScreen __instance)
    {
        if (!NinjaSlayerFeedbackSession.IsActive)
        {
            return;
        }

        __instance.GetNode<NMegaTextEdit>("%DescriptionInput").PlaceholderText =
            Text("NINJA_SLAYER_FEEDBACK_DESCRIPTION_PLACEHOLDER");
        __instance.GetNode<MegaLabel>("%SendButton/Label").SetTextAutoSize(
            Text("NINJA_SLAYER_FEEDBACK_SEND_BUTTON"));
    }

    private static string Text(string key) => new LocString("feedback", key).GetFormattedText();
}

public sealed class NinjaSlayerFeedbackConfirmPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_feedback_confirmation";
    public static string Description => "Require informed confirmation before uploading NinjaSlayer F2 feedback.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NSendFeedbackScreen), "SendButtonSelected", [typeof(NButton)])];

    public static bool Prefix(NSendFeedbackScreen __instance, NButton _)
    {
        if (!NinjaSlayerPatchCapabilities.FeedbackEnabled ||
            !NinjaSlayerFeedbackSession.IsActive || NinjaSlayerFeedbackSession.IsConfirmed)
        {
            return true;
        }

        TaskHelper.RunSafely(ConfirmAndSend(__instance));
        return false;
    }

    private static async Task ConfirmAndSend(NSendFeedbackScreen screen)
    {
        NGenericPopup? popup = NGenericPopup.Create();
        if (popup == null)
        {
            return;
        }

        if (NGame.Instance is not { } game)
        {
            popup.QueueFree();
            return;
        }

        game.AddChildSafely(popup);
        bool confirmed = await popup.WaitForConfirmation(
            Loc("NINJA_SLAYER_FEEDBACK_CONFIRM_BODY"),
            Loc("NINJA_SLAYER_FEEDBACK_CONFIRM_HEADER"),
            Loc("NINJA_SLAYER_FEEDBACK_CONFIRM_CANCEL"),
            Loc("NINJA_SLAYER_FEEDBACK_CONFIRM_SEND"));
        if (!confirmed || !GodotObject.IsInstanceValid(screen) || !screen.Visible)
        {
            return;
        }

        NinjaSlayerFeedbackSession.Confirm();
        GameCompatibility.Feedback.TrySelectSendButton(
            screen,
            screen.GetNode<NButton>("%SendButton"));
    }

    private static LocString Loc(string key) => new("feedback", key);
}

public sealed class NinjaSlayerFeedbackSendPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_feedback_upload";
    public static string Description => "Upload confirmed NinjaSlayer F2 feedback to the mod author's Worker.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NSendFeedbackScreen), "SendFeedback", [typeof(FeedbackData), typeof(Stream), typeof(Stream)])];

    public static bool Prefix(
        FeedbackData data,
        Stream screenshotStream,
        Stream logsMemoryStream,
        ref Task<bool> __result)
    {
        if (!NinjaSlayerPatchCapabilities.FeedbackEnabled ||
            !NinjaSlayerFeedbackSession.IsActive || !NinjaSlayerFeedbackSession.IsConfirmed)
        {
            return true;
        }

        __result = FeedbackStreamOwnership.SendAndCloseAsync(
            () => NinjaSlayerFeedbackClient.SendAsync(data, screenshotStream, logsMemoryStream),
            screenshotStream,
            logsMemoryStream);
        return false;
    }
}

public sealed class NinjaSlayerFeedbackClosePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_feedback_session_cleanup";
    public static string Description => "Clear NinjaSlayer feedback routing when the form closes.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NSendFeedbackScreen), "Close")];

    public static void Postfix(NSendFeedbackScreen __instance)
    {
        if (!NinjaSlayerFeedbackSession.IsActive)
        {
            return;
        }

        NinjaSlayerFeedbackSession.Reset();
        __instance.Relocalize();
    }
}
