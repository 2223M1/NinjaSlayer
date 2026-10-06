using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib.Settings;

namespace NinjaSlayer.Code.Feedback;

/// <summary>A mod-owned form, using the same independent shell components as mod settings.</summary>
public partial class NinjaSlayerFeedbackScreen : Control, IScreenContext
{
    private static readonly string[] Categories = ["bug", "balance", "feedback"];
    private static Task<bool>? _opening;
    private byte[] _screenshotBytes = [];
    private NMegaTextEdit _description = null!;
    private OptionButton _category = null!;
    private ModSettingsSidebarButton _send = null!;
    private MegaRichTextLabel _status = null!;
    private Control? _previousFocus;
    private bool _sending;
    private bool _sent;

    public static NinjaSlayerFeedbackScreen? Instance { get; private set; }
    public Control DefaultFocusedControl => _description;

    public static Task<bool> OpenAsync()
    {
        if (_opening is { IsCompleted: false }) return _opening;
        if (Instance is { Visible: true }) return Task.FromResult(true);
        return _opening = OpenFormAsync();
    }

    private static async Task<bool> OpenFormAsync()
    {
        if (NGame.Instance is not { } game || NModalContainer.Instance is not { OpenModal: null } modal
            || game.FeedbackScreen is { Visible: true }) return false;
        using Image screenshot = game.GetViewport().GetTexture().GetImage();
        float scale = Math.Min(1f, Math.Min(1280f / screenshot.GetWidth(), 720f / screenshot.GetHeight()));
        if (scale < 1f)
            screenshot.Resize(Math.Max(1, (int)(screenshot.GetWidth() * scale)),
                Math.Max(1, (int)(screenshot.GetHeight() * scale)), Image.Interpolation.Bilinear);
        byte[] bytes = screenshot.SavePngToBuffer();
        await game.AwaitProcessFrame();
        if (!GodotObject.IsInstanceValid(game) || !game.IsInsideTree()
            || !GodotObject.IsInstanceValid(modal) || modal.OpenModal is not null) return false;
        game.GetInspectCardScreen().Close();
        game.GetInspectRelicScreen().Close();
        var screen = new NinjaSlayerFeedbackScreen
        {
            Name = nameof(NinjaSlayerFeedbackScreen), _screenshotBytes = bytes,
            _previousFocus = game.GetViewport().GuiGetFocusOwner()
        };
        modal.Add(screen);
        return true;
    }

    public override void _Ready()
    {
        Instance = this;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(960, 600) };
        panel.AddThemeStyleboxOverride("panel", ModSettingsUiFactory.CreateSurfaceStyle());
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        panel.AddChild(margin);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 16);
        margin.AddChild(rows);
        rows.AddChild(ModSettingsUiFactory.CreateHeaderLabel(Text("TITLE"), 32, HorizontalAlignment.Left));
        rows.AddChild(ModSettingsUiFactory.CreateInlineDescription(Text("PRIVACY")));
        var categoryRow = new HBoxContainer();
        categoryRow.AddThemeConstantOverride("separation", 20);
        categoryRow.AddChild(ModSettingsUiFactory.CreateHeaderLabel(NativeText("FEEDBACK_CATEGORY_LABEL"), 24, HorizontalAlignment.Left));
        _category = new OptionButton { Name = "CategoryInput", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string category in Categories) _category.AddItem(NativeText("FEEDBACK_CATEGORY." + category));
        _category.Select(2);
        _category.AddThemeFontOverride("font", rows.GetChild<Control>(0).GetThemeFont("normal_font"));
        _category.AddThemeFontSizeOverride("font_size", 24);
        categoryRow.AddChild(_category);
        rows.AddChild(categoryRow);
        _description = new NMegaTextEdit
        {
            Name = "DescriptionInput", PlaceholderText = Text("DESCRIPTION_PLACEHOLDER"),
            CustomMinimumSize = new Vector2(0, 240), SizeFlagsVertical = SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary, FocusMode = FocusModeEnum.All
        };
        _description.AddThemeFontSizeOverride("font_size", 24);
        _description.AddThemeStyleboxOverride("normal", ModSettingsUiFactory.CreateEntryFieldFrameStyle(false));
        _description.AddThemeStyleboxOverride("focus", ModSettingsUiFactory.CreateEntryFieldFrameStyle(true));
        _description.TextChanged += () => _send.Disabled = _sending || _sent
            || string.IsNullOrWhiteSpace(_description.Text) || _description.Text.Length > 8000;
        rows.AddChild(_description);
        _status = ModSettingsUiFactory.CreateInlineDescription("");
        _status.Name = "SendStatus";
        rows.AddChild(_status);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 16);
        actions.AddChild(ModSettingsUiFactory.CreateSidebarButton(Text("CLOSE"), Close));
        _send = ModSettingsUiFactory.CreateSidebarButton(Text("SEND"), () => TaskHelper.RunSafely(SubmitAsync()));
        _send.Name = "SendButton";
        _send.Disabled = true;
        actions.AddChild(_send);
        rows.AddChild(actions);
        NHotkeyManager.Instance!.AddBlockingScreen(this);
        _description.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    public async Task<bool> SubmitAsync()
    {
        if (_sending || _sent || string.IsNullOrWhiteSpace(_description.Text) || _description.Text.Length > 8000)
            return false;
        _sending = true;
        _send.Disabled = true;
        _description.Editable = false;
        _category.Disabled = true;
        _status.Text = NativeText("FEEDBACK_SENDING_LABEL");
        var release = ReleaseInfoManager.Instance.ReleaseInfo;
        var data = new FeedbackData
        {
            description = _description.Text, category = Categories[_category.Selected],
            gameVersion = release?.Version ?? "unknown", commit = release?.Commit ?? GitHelper.ShortCommitId ?? "unknown",
            platformBranch = PlatformUtil.GetPlatformBranch().ToName(),
            isModded = ModManager.IsRunningModded() || ModManager.HasHarmonyPatches(),
            isFullConsole = SaveManager.Instance.SettingsSave.FullConsole, lang = LocManager.Instance.Language
        };
        using var screenshot = new MemoryStream(_screenshotBytes);
        using var logs = new MemoryStream();
        GetLogsConsoleCmd.ZipFeedbackLogs(logs, SaveManager.Instance.CurrentProfileId);
        logs.Position = 0;
        bool success = await NinjaSlayerFeedbackClient.SendAsync(data, screenshot, logs);
        // A closed form may finish uploading; its result must never modify a later form.
        if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion())
        {
            _sending = false;
            _sent = success;
            _send.Disabled = success;
            _description.Editable = !success;
            _category.Disabled = success;
            _status.Text = NativeText(success ? "FEEDBACK_SEND_SUCCESS_LABEL" : "FEEDBACK_SEND_FAILED_LABEL");
        }
        return success;
    }

    public void Close()
    {
        Visible = false;
        if (ReferenceEquals(NModalContainer.Instance?.OpenModal, this)) NModalContainer.Instance.Clear();
    }

    public override void _ExitTree()
    {
        NHotkeyManager.Instance?.RemoveBlockingScreen(this);
        if (ReferenceEquals(Instance, this)) Instance = null;
        if (GodotObject.IsInstanceValid(_previousFocus) && _previousFocus!.IsInsideTree()) _previousFocus.GrabFocus();
        else ActiveScreenContext.Instance.FocusOnDefaultControl();
    }

    private static string Text(string suffix) => new LocString("settings_ui", "NINJA_SLAYER_FEEDBACK_" + suffix).GetFormattedText();
    private static string NativeText(string key) => new LocString("settings_ui", key).GetFormattedText();
}
