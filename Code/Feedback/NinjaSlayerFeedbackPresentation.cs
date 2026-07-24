using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Feedback;

/// <summary>
/// Tints the shared feedback form so an in-run NinjaSlayer F2 session is visually distinct from MegaCrit feedback.
/// </summary>
internal static class NinjaSlayerFeedbackPresentation
{
    // Match NinjaSlayerCardPool.DeckEntryCardColor / character reds.
    private static readonly Color InputBackground = new("9D1F1FFF");
    private static readonly Color InputFocusBackground = NinjaSlayerCharacterStats.NameColor;
    private static readonly Color CategoryBackground = NinjaSlayerCharacterStats.EnergyLabelOutlineColor;

    private static readonly StringName NormalStyle = "normal";
    private static readonly StringName FocusStyle = "focus";
    private static readonly StringName PanelStyle = "panel";

    private static readonly object SyncRoot = new();
    private static ulong _screenInstanceId;
    private static StyleSnapshot? _snapshot;

    public static void Apply(NSendFeedbackScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        NMegaTextEdit description = screen.GetNode<NMegaTextEdit>("%DescriptionInput");
        Control category = screen.GetNode<Control>("%CategoryDropdown");
        Panel currentHighlight = category.GetNode<Panel>("CurrentOption/Highlight");
        Panel dropdownHighlight = category.GetNode<Panel>("DropdownContainer/Highlight");

        StyleBox? descriptionNormal = description.GetThemeStylebox(NormalStyle);
        StyleBox? descriptionFocus = description.GetThemeStylebox(FocusStyle);
        StyleBox? currentStyle = currentHighlight.GetThemeStylebox(PanelStyle);
        StyleBox? dropdownStyle = dropdownHighlight.GetThemeStylebox(PanelStyle);

        lock (SyncRoot)
        {
            _screenInstanceId = screen.GetInstanceId();
            _snapshot = new StyleSnapshot(descriptionNormal, descriptionFocus, currentStyle, dropdownStyle);
        }

        description.AddThemeStyleboxOverride(NormalStyle, CloneFlat(descriptionNormal, InputBackground));
        description.AddThemeStyleboxOverride(FocusStyle, CloneFlat(descriptionFocus, InputFocusBackground));
        currentHighlight.AddThemeStyleboxOverride(PanelStyle, CloneFlat(currentStyle, CategoryBackground));
        dropdownHighlight.AddThemeStyleboxOverride(PanelStyle, CloneFlat(dropdownStyle, CategoryBackground));
    }

    public static void Restore(NSendFeedbackScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        StyleSnapshot? snapshot;
        lock (SyncRoot)
        {
            if (_snapshot is null || _screenInstanceId != screen.GetInstanceId())
            {
                return;
            }

            snapshot = _snapshot;
            _snapshot = null;
            _screenInstanceId = 0;
        }

        NMegaTextEdit description = screen.GetNode<NMegaTextEdit>("%DescriptionInput");
        Control category = screen.GetNode<Control>("%CategoryDropdown");
        Panel currentHighlight = category.GetNode<Panel>("CurrentOption/Highlight");
        Panel dropdownHighlight = category.GetNode<Panel>("DropdownContainer/Highlight");

        RestoreStyle(description, NormalStyle, snapshot.DescriptionNormal);
        RestoreStyle(description, FocusStyle, snapshot.DescriptionFocus);
        RestoreStyle(currentHighlight, PanelStyle, snapshot.CurrentOptionHighlight);
        RestoreStyle(dropdownHighlight, PanelStyle, snapshot.DropdownHighlight);
    }

    private static StyleBoxFlat CloneFlat(StyleBox? source, Color background)
    {
        StyleBoxFlat flat = source is StyleBoxFlat existing
            ? (StyleBoxFlat)existing.Duplicate()
            : new StyleBoxFlat
            {
                ContentMarginLeft = 16f,
                ContentMarginTop = 16f,
                ContentMarginRight = 16f,
                ContentMarginBottom = 16f,
                CornerRadiusTopLeft = 16,
                CornerRadiusTopRight = 16,
                CornerRadiusBottomRight = 16,
                CornerRadiusBottomLeft = 16
            };
        flat.BgColor = background;
        return flat;
    }

    private static void RestoreStyle(Control control, StringName name, StyleBox? style)
    {
        if (style is null)
        {
            control.RemoveThemeStyleboxOverride(name);
            return;
        }

        control.AddThemeStyleboxOverride(name, style);
    }

    private sealed record StyleSnapshot(
        StyleBox? DescriptionNormal,
        StyleBox? DescriptionFocus,
        StyleBox? CurrentOptionHighlight,
        StyleBox? DropdownHighlight);
}
