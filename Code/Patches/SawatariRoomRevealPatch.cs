using System.Reflection;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Events;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class SawatariRoomRevealPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_visible_entrance";
    public static string Description => "Start Sawatari's embedded combat entrance after the native room fade-in.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RunManager), "FadeIn", [typeof(bool)]),
        // Continue from the main menu reveals the restored room directly.
        new(typeof(NTransition), nameof(NTransition.FadeIn),
            [typeof(float), typeof(string), typeof(CancellationToken?)]),
        // Quick Restart restores a visible room without calling either fade-in.
        new(typeof(NGame), nameof(NGame.LoadRun), [typeof(RunState), typeof(SerializableRoom)])
    ];

    public static void Postfix(MethodBase __originalMethod, ref Task __result)
    {
        if (__originalMethod.DeclaringType == typeof(NGame))
        {
            __result = StartAfterVisibleLoad(__result);
            return;
        }
        if (!RunManager.Instance.IsInProgress) return;
        TheMovingJungleEvent[] pending = RunManager.Instance.EventSynchronizer.Events
            .OfType<TheMovingJungleEvent>().Where(model => model.HasPendingCombatStart).ToArray();
        if (pending.Length != 0) __result = StartAfterReveal(__result, pending);
    }

    private static async Task StartAfterVisibleLoad(Task load)
    {
        await load;
        if (!RunManager.Instance.IsInProgress || NGame.Instance!.Transition.InTransition) return;
        TheMovingJungleEvent[] pending = RunManager.Instance.EventSynchronizer.Events
            .OfType<TheMovingJungleEvent>().Where(model => model.HasPendingCombatStart).ToArray();
        foreach (TheMovingJungleEvent model in pending) await model.StartCombatAfterRoomReveal();
    }

    private static async Task StartAfterReveal(Task reveal, TheMovingJungleEvent[] pending)
    {
        await reveal;
        foreach (TheMovingJungleEvent model in pending) await model.StartCombatAfterRoomReveal();
    }
}
