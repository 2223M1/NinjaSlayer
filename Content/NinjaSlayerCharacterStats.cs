using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCharacterStats
{
    public static readonly Color NameColor = new("D32020FF");
    public static readonly Color EnergyLabelOutlineColor = new("691A1BFF");
    public static readonly Color DialogueColor = new("530909FF");
    public static readonly Color MapDrawingColor = new("D32020FF");
    public static readonly Color RemoteTargetingLineColor = new("E34B3FFF");
    public static readonly Color RemoteTargetingLineOutlineColor = new("691A1BFF");

    public const CharacterGender Gender = CharacterGender.Masculine;
    public const int StartingHp = 72;
    public const int StartingGold = 99;
    public const float AttackAnimDelay = 0.15f;
    public const float CastAnimDelay = 0.2f;
    public const bool RequiresEpochAndTimeline = false;
    public const VfxColor SpeechBubbleColor = VfxColor.Red;

    public static IReadOnlyList<string> ArchitectAttackVfx { get; } = Array.AsReadOnly(
    [
        "vfx/vfx_attack_slash",
        "vfx/vfx_heavy_blunt",
        "vfx/vfx_bloody_impact"
    ]);
}
