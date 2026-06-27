using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerDeathAnimPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_death_animation";

    public static string Description => "Play FMOD death SFX and spin-axis fall for non-Spine NinjaSlayer.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)]),
        new(typeof(NCreature), "AnimDie", [typeof(bool), typeof(CancellationToken)])
    ];

    public static void Postfix(NCreature __instance, ref float __result)
    {
        if (!IsNinjaSlayerNonSpine(__instance))
        {
            return;
        }

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(__instance.Entity).Death);
        __result = DeathAnimation.DurationSeconds;
    }

    public static async Task Prefix(NCreature __instance, bool shouldRemove, CancellationToken cancelToken)
    {
        if (!IsNinjaSlayerNonSpine(__instance))
        {
            return;
        }

        await DeathAnimation.Play(__instance.Entity);
    }

    private static bool IsNinjaSlayerNonSpine(NCreature creatureNode)
    {
        return creatureNode.Entity.Player?.Character is NinjaSlayerCharacter
            && !creatureNode.HasSpineAnimation;
    }
}
