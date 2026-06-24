using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using NinjaSlayer.Relics;

namespace NinjaSlayer.Code.Patches;

[HarmonyPatch(typeof(EventModel), "SetEventState")]
internal static class ReporterPassEventOptionPatch
{
    private const int CardsToUpgrade = 3;
    private const string RecordOptionKey = "NINJA_SLAYER_REPORTER_PASS_RECORD";
    private const string RelicLocPrefix = "NINJA_SLAYER_RELIC_REPORTER_PASS_RELIC";

    private static readonly MethodInfo SetEventFinishedMethod =
        AccessTools.Method(typeof(EventModel), "SetEventFinished", new[] { typeof(LocString) })
        ?? throw new MissingMethodException(nameof(EventModel), "SetEventFinished");

    private static void Prefix(EventModel __instance, ref IEnumerable<EventOption> eventOptions)
    {
        if (__instance.Owner?.GetRelic<ReporterPassRelic>() == null || __instance.IsFinished || eventOptions == null)
        {
            return;
        }

        List<EventOption> options = eventOptions.ToList();
        if (options.Count == 0 || options.Any(option => option.TextKey == RecordOptionKey))
        {
            return;
        }

        options.Add(CreateRecordOption(__instance));
        eventOptions = options;
    }

    private static EventOption CreateRecordOption(EventModel eventModel)
    {
        return new EventOption(
            eventModel,
            () => Record(eventModel),
            new LocString("relics", $"{RelicLocPrefix}.record.title"),
            new LocString("relics", $"{RelicLocPrefix}.record.description"),
            RecordOptionKey,
            Array.Empty<IHoverTip>()
        );
    }

    private static Task Record(EventModel eventModel)
    {
        eventModel.Owner?.GetRelic<ReporterPassRelic>()?.Flash();

        if (eventModel.Owner != null)
        {
            IEnumerable<CardModel> cards = PileType.Deck.GetPile(eventModel.Owner).Cards
                .Where(card => card?.IsUpgradable ?? false)
                .ToList()
                .StableShuffle(eventModel.Rng)
                .Take(CardsToUpgrade);

            foreach (CardModel card in cards)
            {
                CardCmd.Upgrade(card, CardPreviewStyle.EventLayout);
            }
        }

        // ponytail: protected finish API; reflection avoids patching every event subclass.
        SetEventFinishedMethod.Invoke(eventModel, [new LocString("relics", $"{RelicLocPrefix}.record.done")]);
        return Task.CompletedTask;
    }
}
