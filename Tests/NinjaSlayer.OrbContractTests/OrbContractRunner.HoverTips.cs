using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static void VerifyHoverTipText(CardModel card)
    {
        foreach (var tip in card.HoverTips.OfType<HoverTip>())
            Require(!tip.Description.Contains("NINJA_SLAYER_") && tip.Title?.Contains("NINJA_SLAYER_") != true,
                $"Unresolved tooltip localization for {card.Id} (upgraded: {card.IsUpgraded}).");
    }

    private static void VerifyCharacterText()
    {
        string prefix = ModelDb.Character<NinjaSlayerCharacter>().Id.Entry;
        foreach (string suffix in new[] { "description", "pronounSubject", "pronounObject", "pronounPossessive",
            "possessiveAdjective", "aromaPrinciple", "goldMonologue", "eventDeathPrevention", "cardsModifierTitle",
            "cardsModifierDescription", "banter.alive.endTurnPing", "banter.dead.endTurnPing", "bestiaryQuote" })
        {
            var line = new LocString("characters", prefix + "." + suffix);
            Require(line.Exists() && !string.IsNullOrWhiteSpace(line.GetFormattedText()), $"Missing character text: {line.LocEntryKey}");
        }
        foreach (var (key, variable, suffix) in new[] {
            ("AROMA_OF_CHAOS.pages.MAINTAIN_CONTROL.description", "AromaPrinciple", "aromaPrinciple"),
            ("SUNKEN_TREASURY.pages.SECOND_CHEST.description", "Monologue", "goldMonologue") })
        {
            var page = new LocString("events", key);
            var characterLine = new LocString("characters", prefix + "." + suffix);
            page.Add(variable, characterLine);
            string text = page.GetFormattedText();
            Require(text.Contains(characterLine.GetFormattedText()) && !text.Contains(prefix),
                $"Native event page did not format its character line: {key}");
        }
    }

    private static void VerifyHoverTips(OrbCombat combat)
    {
        foreach (bool upgraded in new[] { false, true })
        {
            var karate = AddCard<KarateStraightRedesignV1>(combat, upgraded: upgraded);
            Require(karate.HoverTips.Any(tip => tip.Id == HoverTipFactory.FromPower<KaratePower>().Id),
                "Karate must expose its native side tooltip on mutable base/upgraded cards.");
            var flame = AddCard<ReturnReturnReturnRedesignV1>(combat, upgraded: upgraded);
            Require(flame.HoverTips.OfType<CardHoverTip>().Any(tip => tip.Card is BlackFlameRedesignV1)
                && flame.HoverTips.Any(tip => tip.Id == HoverTipFactory.FromPower<NarakuLifePower>().Id),
                "Return Return Return must preview Black Flame and explain Naraku Life.");
            var starless = AddCard<GiantShurikenRedesignV1>(combat, upgraded: upgraded);
            Require(!starless.HoverTips.OfType<CardHoverTip>().Single().Card.IsUpgraded,
                "Starless Night must preview an unupgraded token even when the power card is upgraded.");
            var guard = AddCard<KillingIntentRedesignV1>(combat, upgraded: upgraded);
            Require(guard.HoverTips.OfType<CardHoverTip>().Single().Card.IsUpgraded == upgraded,
                "Killing Intent must preview the generated Straight Ki's upgrade.");
        }
        Require(ModelDb.Relic<IrcTerminalRelic>().HoverTips.OfType<CardHoverTip>().Any(tip => tip.Card is BusyLine),
            "IRC Terminal must expose its Busy Line card preview.");
        foreach (RelicModel relic in new RelicModel[] { ModelDb.Relic<ChadoBreathingRelic>(), ModelDb.Relic<DeepChadoBreathingRelic>() })
            Require(relic.HoverTips.OfType<CardHoverTip>().Single().Card.Keywords.Contains(CardKeyword.Retain)
                == (relic is DeepChadoBreathingRelic), "Only the ancient starter relic must preview retained tea.");
        Require(!ModelDb.Card<ChadoEnergyRedesignV1>().Keywords.Contains(CardKeyword.Retain),
            "Relic previews must not mutate the canonical tea model.");
    }
}
