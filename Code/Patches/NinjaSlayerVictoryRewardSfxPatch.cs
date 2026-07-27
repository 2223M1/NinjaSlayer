using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerVictoryRewardSfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_victory_reward_sfx_suppression";
    public static string Description =>
        "Suppress the vanilla reward victory cue while Ninja Soul is used by a NinjaSlayer party.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NDebugAudioManager),
            nameof(NDebugAudioManager.Play),
            [typeof(string), typeof(float), typeof(PitchVariance)])
    ];

    public static bool Prefix(string streamName, ref int __result)
    {
        bool partyContainsNinjaSlayer = NCombatRoom.Instance?.CreatureNodes.Any(
            node => node.Entity.Player?.Character is INinjaSlayerCharacter) == true;
        if (!NinjaSlayerVictorySfxPolicy.ShouldSuppress(streamName, partyContainsNinjaSlayer))
        {
            return true;
        }

        // NRewardsScreen ignores this one-shot handle.
        __result = -1;
        return false;
    }
}
