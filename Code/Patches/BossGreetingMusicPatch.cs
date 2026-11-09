using MegaCrit.Sts2.Core.Nodes.Audio;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class BossGreetingMusicPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_boss_greeting_music_gate";

    public static string Description => "Delay custom boss music until the NinjaSlayer greeting completes.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic), [typeof(string)])];

    public static bool Prefix(NRunMusicController __instance, string customMusic) =>
        !BossGreetingCinematic.TryDeferBossBgm(__instance, customMusic);
}

public sealed class BossGreetingMusicParameterPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_boss_greeting_music_parameters";
    public static string Description => "Apply boss intro parameters to the deferred boss music instance.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusicParameter), [typeof(string), typeof(float)])];

    public static bool Prefix(NRunMusicController __instance, string label, float trackIndex) =>
        !BossGreetingCinematic.TryDeferBossMusicParameter(__instance, label, trackIndex);
}
