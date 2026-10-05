using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Events;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class SawatariRoomRevealPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_visible_entrance";
    public static string Description => "Start Sawatari's embedded combat entrance after the native room fade-in.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(RunManager), "FadeIn", [typeof(bool)])];

    public static void Postfix(RunManager __instance, ref Task __result)
    {
        TheMovingJungleEvent[] pending = __instance.EventSynchronizer.Events
            .OfType<TheMovingJungleEvent>().Where(model => model.HasPendingCombatStart).ToArray();
        if (pending.Length != 0) __result = StartAfterReveal(__result, pending);
    }

    private static async Task StartAfterReveal(Task reveal, TheMovingJungleEvent[] pending)
    {
        await reveal;
        foreach (TheMovingJungleEvent model in pending) await model.StartCombatAfterRoomReveal();
    }
}
