using Godot;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

/// <summary>One shared background; replace only the selected portrait on a settings change.</summary>
[GlobalClass]
public partial class NinjaSlayerSelectBackground : Control
{
    private Node2D? _portrait;

    public override void _Ready()
    {
        ReplacePortrait();
        NinjaSlayerSettings.SelectPortraitChanged += ReplacePortrait;
    }

    public override void _ExitTree()
    {
        NinjaSlayerSettings.SelectPortraitChanged -= ReplacePortrait;
    }

    private void ReplacePortrait()
    {
        string variant = NinjaSlayerSettings.MangaSelectPortraitEnabled ? "manga" : "front";
        Node2D next = ResourceLoader.Load<PackedScene>(
            $"res://NinjaSlayer/art/character_select/portrait_{variant}.tscn").Instantiate<Node2D>();
        if (_portrait is not null)
        {
            RemoveChild(_portrait);
            _portrait.QueueFree();
        }
        _portrait = next;
        AddChild(next);
        // Match vanilla Ironclad: background, portrait, then foreground ash/embers.
        MoveChild(next, 1);
    }
}
