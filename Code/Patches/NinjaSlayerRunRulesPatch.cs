using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerRunRulesCharacterPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_redesign_character_new_run";
    public static string Description => "Replace newly created Ninja Slayer players with the staged redesign character.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(Player), nameof(Player.CreateForNewRun),
            [typeof(CharacterModel), typeof(UnlockState), typeof(ulong)])
    ];

    public static void Prefix(ref CharacterModel character, ulong netId) =>
        NinjaSlayerRunRulesRuntime.TryReplaceCharacter(ref character, netId);
}

public sealed class NinjaSlayerSingleplayerRunRulesCharacterPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_redesign_character_new_singleplayer_run";
    public static string Description => "Select the staged redesign character before a new single-player run is created.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NGame), nameof(NGame.StartNewSingleplayerRun),
        [
            typeof(CharacterModel),
            typeof(bool),
            typeof(IReadOnlyList<ActModel>),
            typeof(IReadOnlyList<ModifierModel>),
            typeof(string),
            typeof(GameMode),
            typeof(int),
            typeof(DateTimeOffset?)
        ])
    ];

    public static void Prefix(ref CharacterModel character) =>
        NinjaSlayerRunRulesRuntime.ReplaceSingleplayerCharacter(ref character);
}
