using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerDeathAnimPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_death_animation";

    public static string Description => "Choose NinjaSlayer death feedback from the fatal damage source.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])
    ];

    public static void Prefix(NCreature __instance, out bool __state)
    {
        __state = IsNinjaSlayerNonSpine(__instance)
            && (__instance.DeathAnimationTask == null || __instance.DeathAnimationTask.IsCompleted);
    }

    public static void Postfix(NCreature __instance, ref float __result, bool __state)
    {
        if (!__state)
        {
            return;
        }

        NinjaSlayerDeathKind kind = DeathAnimation.Classify(__instance.Entity);
        string eventPath = kind == NinjaSlayerDeathKind.EnemyKill
            ? NinjaSlayerCombatAudioSet.For(__instance.Entity).Death
            : NinjaSlayerAudio.NinjaSlayerSuicideEvent;
        NinjaSlayerCombatAudioSet.Play(eventPath);
        TaskHelper.RunSafely(DeathAnimation.Play(__instance.Entity, kind));
        __result = DeathAnimation.GetDuration(kind);
    }

    private static bool IsNinjaSlayerNonSpine(NCreature creatureNode)
    {
        return creatureNode.Entity.Player?.Character is INinjaSlayerCharacter
            && !creatureNode.HasSpineAnimation;
    }
}
