using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.Standard;
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

    private static void VerifyArchitectDialogueText()
    {
        string root = Path.GetFullPath(Path.Combine(Godot.ProjectSettings.GlobalizePath("res://"), "../../NinjaSlayer/localization"));
        foreach (string language in new[] { "eng", "zhs", "jpn" })
        {
            LocManager.Instance.SetLanguage(language);
            LocManager.Instance.GetTable("ancients").MergeWith(
                System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(Path.Combine(root, language, "ancients.json")))!);
            var architect = (MegaCrit.Sts2.Core.Models.Events.TheArchitect)ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.TheArchitect>().ToMutable();
            var set = architect.DialogueSet;
            var id = ModelDb.Character<NinjaSlayerCharacter>().Id;
            Require(set.CharacterDialogues[id.Entry].Count == 4, "Architect must register exactly four native dialogue sequences.");
            for (int visits = 0; visits < 9; visits++)
            {
                var valid = set.GetValidDialogues(id, visits, visits, false).ToArray();
                Require(valid.Length > 0, "Architect must have dialogue for initial and repeated visits.");
                foreach (var dialogue in valid)
                {
                    Require(dialogue.Lines.Count == 2, "Each Architect sequence must retain two lines.");
                    foreach (var line in dialogue.Lines)
                        Require(line.LineText.Exists() && !line.LineText.GetFormattedText().Contains("THE_ARCHITECT"),
                            $"Unresolved Architect line: {language}, visit {visits}, {line.LineText.LocEntryKey}");
                }
            }
        }
        LocManager.Instance.SetLanguage("zhs");
        Godot.GD.Print("PASS Architect dialogue: three languages, four sequences, first/later/repeat visits, no unresolved keys.");
    }

    private static void VerifyHoverTips(OrbCombat combat)
    {
        foreach (bool upgraded in new[] { false, true })
        {
            var karate = AddCard<StraightPunch>(combat, upgraded: upgraded);
            Require(karate.HoverTips.Any(tip => tip.Id == HoverTipFactory.FromPower<KaratePower>().Id),
                "Karate must expose its native side tooltip on mutable base/upgraded cards.");
            var flame = AddCard<DevourFlame>(combat, upgraded: upgraded);
            Require(flame.HoverTips.Any(tip => tip.Id == HoverTipFactory.FromPower<NarakuLifePower>().Id),
                "Devour Flame must explain its Naraku Life reward.");
            var starless = AddCard<StarlessNight>(combat, upgraded: upgraded);
            Require(!starless.HoverTips.OfType<CardHoverTip>().Single().Card.IsUpgraded,
                "Starless Night must preview an unupgraded token even when the power card is upgraded.");
            var guard = AddCard<KillingIntent>(combat, upgraded: upgraded);
            Require(!guard.HoverTips.OfType<CardHoverTip>().Single().Card.IsUpgraded,
                "Killing Intent must preview the generated Straight Ki's upgrade.");
        }
        Require(ModelDb.Relic<IrcTerminalRelic>().HoverTips.OfType<CardHoverTip>().Any(tip => tip.Card is BusyLine),
            "IRC Terminal must expose its Busy Line card preview.");
        foreach (RelicModel relic in new RelicModel[] { ModelDb.Relic<ChadoBreathingRelic>(), ModelDb.Relic<ChadoBreathingMasteryRelic>() })
            Require(relic.HoverTips.OfType<CardHoverTip>().Single().Card.Keywords.Contains(CardKeyword.Retain)
                == false, "Starter relics grant temporary retention without changing tea keywords.");
        Require(!ModelDb.Card<Chado>().Keywords.Contains(CardKeyword.Retain),
            "Relic previews must not mutate the canonical tea model.");
    }
}
