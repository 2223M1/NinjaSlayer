using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class AncientEntranceEventOptionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_ancient_entrance_event_option";

    public static string Description => "Mark NinjaSlayer's next combat for a special entrance after choosing any Ancient relic option.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(EventModel), "SetEventState", [typeof(LocString), typeof(IEnumerable<EventOption>)])];

    public static void Prefix(EventModel __instance, ref IEnumerable<EventOption> eventOptions)
    {
        if (__instance is not AncientEventModel || __instance.Owner?.Character is not INinjaSlayerCharacter)
        {
            return;
        }

        var owner = __instance.Owner;
        List<EventOption> options = eventOptions.ToList();

        foreach (EventOption option in options)
        {
            if (option.Relic == null)
            {
                continue;
            }

            option.BeforeChosen += _ =>
            {
                NinjaSlayerRunData.MarkPendingAncientEntranceAnimation(owner);
                return Task.CompletedTask;
            };
        }

        eventOptions = options;
    }
}
