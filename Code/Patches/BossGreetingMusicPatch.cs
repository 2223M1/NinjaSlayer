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

    public static bool Prefix(string customMusic) =>
        !BossGreetingCinematic.TryDeferBossBgm(customMusic);
}
