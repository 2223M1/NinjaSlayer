using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
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
