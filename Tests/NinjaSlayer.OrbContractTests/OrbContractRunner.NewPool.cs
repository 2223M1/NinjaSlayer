using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyNewCardInteractions()
    {
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitPrefsDataForTest();
        await VerifyCardPresentation();
        AccessTools.Property(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "NetService").SetValue(
            MegaCrit.Sts2.Core.Runs.RunManager.Instance, new MegaCrit.Sts2.Core.Multiplayer.NetSingleplayerGameService());
        await VerifyScryAndSly();
        await VerifyMotherUnixBeforeDraw();
        await VerifyBalanceV0216();
        await VerifyBalanceV0217();
        await VerifyBoardV115();
        await VerifyStatusCards();
        await VerifyTeaAndChop();
        await VerifyTemporaryStats();
        await VerifyDamageSourceMatrix();
        await VerifySweepDuration();
        await VerifyTurnAndSelectionEffects();
    }

    private static async Task VerifyCardPresentation()
    {
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        string localizationRoot = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../NinjaSlayer/localization"));
        foreach (string language in new[] { "eng", "zhs" })
        {
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language);
            foreach (string table in new[] { "cards", "powers", "card_keywords", "characters", "relics", "static_hover_tips" })
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.GetTable(table).MergeWith(
                    System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                        System.IO.File.ReadAllText(Path.Combine(localizationRoot, language, table + ".json")))!);
            using var combat = new OrbCombat();
            foreach (Type type in typeof(ReadAhead).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type)))
            {
                CardModel card = ModelDb.GetById<CardModel>(ModelDb.GetId(type)).ToMutable();
                card.Owner = combat.Player;
                _ = card.GetDescriptionForPile(PileType.None);
                if (_hasPresentationResources) VerifyHoverTipText(card);
                if (!card.IsUpgradable) continue;
                card.UpgradeInternal();
                _ = card.GetDescriptionForPile(PileType.None);
                if (_hasPresentationResources) VerifyHoverTipText(card);
            }
            if (_hasPresentationResources)
            {
                VerifyCharacterText();
                VerifyHoverTips(combat);
            }
            await VerifyDynamicCardNumbers();
        }
        VerifyArchitectDialogueText();
        GD.Print("PASS bilingual base/upgraded card descriptions");
        if (_hasPresentationResources)
            GD.Print("PASS mechanism tips, generated previews and native event text");
    }

    private static async Task VerifyDynamicCardNumbers()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var palm = AddCard<PalmThrust>(combat, upgraded: upgraded);
            var adjustment = AddCard<MotionAndStillness>(combat, upgraded: upgraded);
            var judge = AddCard<ReadAhead>(combat, upgraded: upgraded);
            var storm = AddCard<StormFist>(combat, upgraded: upgraded);
            AddCard<Chado>(combat, PileType.Exhaust);

            void Expect(CardModel card, int expected, bool block = false)
            {
                card.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, card.DynamicVars);
                string text = System.Text.RegularExpressions.Regex.Replace(
                    card.GetDescriptionForPile(PileType.Hand, combat.Enemy), @"\[[^\]]*\]", "");
                string pattern = block ? @"(?:获得|Gain\s+)(\d+)" : @"(?:造成|Deal\s+)(\d+)";
                var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
                Require(match.Success && int.Parse(match.Groups[1].Value) == expected,
                    $"{card.Id} must display {expected} {(block ? "block" : "damage")}, upgraded={upgraded}: {text}");
            }

            await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            await PowerCmd.Apply<DexterityPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            Expect(palm, 9);
            Expect(adjustment, upgraded ? 14 : 10);
            Expect(judge, 6, block: true);
            Expect(storm, upgraded ? 13 : 10);

            await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, -5, combat.Player.Creature, null);
            await PowerCmd.Apply<DexterityPower>(Choice, combat.Player.Creature, -4, combat.Player.Creature, null);
            Expect(palm, 4);
            Expect(adjustment, upgraded ? 9 : 5);
            Expect(judge, 2, block: true);
            Expect(storm, upgraded ? 8 : 5);

            await PowerCmd.Apply<WeakPower>(Choice, combat.Player.Creature, 1, combat.Enemy, null);
            await PowerCmd.Apply<FrailPower>(Choice, combat.Player.Creature, 1, combat.Enemy, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 1, combat.Player.Creature, null);
            Expect(palm, 4);
            Expect(adjustment, upgraded ? 10 : 5);
            Expect(judge, 1, block: true);
            Expect(storm, upgraded ? 9 : 5);
        }
        GD.Print("PASS native card-number previews: positive/negative Strength and Dexterity, Weak, Vulnerable, Frail, exhaust scaling and upgrades");
    }

    private static T AddCard<T>(OrbCombat combat, PileType pile = PileType.Hand, bool upgraded = false) where T : CardModel
    {
        T card = combat.State.CreateCard<T>(combat.Player);
        if (upgraded) card.UpgradeInternal();
        pile.GetPile(combat.Player).AddInternal(card, -1, silent: true);
        return card;
    }

    private static async Task VerifyScryAndSly()
    {
        foreach (var (drawCount, discardCount, amount) in new[] { (0, 3, 2), (1, 3, 4), (0, 0, 2), (0, 3, 0) })
        {
            using var combat = new OrbCombat();
            var drawCards = Enumerable.Range(0, drawCount).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            var discardCards = Enumerable.Range(0, discardCount).Select(_ => AddCard<DefendIronclad>(combat, PileType.Discard)).ToArray();
            int selections = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                selections++;
                Require(options.SequenceEqual(PileType.Draw.GetPile(combat.Player).Cards.Take(amount)),
                    "Scry must read the actual post-shuffle top cards.");
                Require(options.All(c => (drawCount > 0 ? drawCards : discardCards).Contains(c)),
                    "Scry must only shuffle when the draw pile starts empty.");
                return [];
            }));
            var result = await ScryCmd.Execute(Choice, combat.Player, amount);
            int expected = amount > 0 ? Math.Min(amount, drawCount > 0 ? drawCount : discardCount) : 0;
            Require(result.Viewed == expected && selections == (expected > 0 ? 1 : 0)
                && PileType.Hand.GetPile(combat.Player).IsEmpty
                && PileType.Discard.GetPile(combat.Player).Cards.Count == (amount > 0 && drawCount == 0 ? 0 : discardCount),
                "Empty Scry must shuffle once without drawing; short/nonpositive/fully empty Scry must not refill.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<DefendIronclad>(combat, PileType.Discard);
            await AddStock(combat.Player, 3);
            await PowerCmd.Apply<BladeCyclePower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            int hp = combat.Enemy.CurrentHp;
            int evoked = _evoked;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ =>
            {
                Require(_evoked == evoked + 3 && combat.Enemy.CurrentHp == hp - 18 && combat.Stock == 1,
                    "Scry must await native shuffle attacks and stock loss before selecting.");
                return [];
            }));
            Require((await ScryCmd.Execute(Choice, combat.Player, 2)).Viewed == 1, "Shuffled Scry lost its card.");
        }
        GD.Print("PASS empty-draw Scry: native reshuffle, post-shuffle order/effects, no actual draw, short pile and empty/nonpositive boundaries.");
        foreach (var (selected, exhaust) in new[] { (0, false), (1, false), (3, false), (3, true) })
        {
            using var combat = new OrbCombat();
            var cards = Enumerable.Range(0, 3).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            var draw = PileType.Draw.GetPile(combat.Player);
            int removed = 0;
            int finished = 0;
            int contentsChanged = 0;
            draw.CardRemoved += _ => removed++;
            draw.CardRemoveFinished += () => finished++;
            draw.ContentsChanged += () => contentsChanged++;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => cards.Take(selected).ToArray()));
            await ScryCmd.Execute(Choice, combat.Player, 3, exhaust);
            Require(draw.Cards.Count == 3 - selected && removed == selected && finished == selected
                && contentsChanged == selected,
                $"Scry must notify native draw-pile views once per removed card: selected={selected}, exhaust={exhaust}, removed={removed}, finished={finished}, changed={contentsChanged}.");
        }
        using (var combat = new OrbCombat())
        {
            var judge = AddCard<ReadAhead>(combat);
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            var discarded = AddCard<DefendIronclad>(combat, PileType.Draw);
            var nested = AddCard<DefendIronclad>(combat, PileType.Draw);
            int drawRemoved = 0;
            PileType.Draw.GetPile(combat.Player).CardRemoveFinished += () => drawRemoved++;
            await AddStock(combat.Player, 3);
            int selection = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                selection++;
                if (selection == 1) return [sly, discarded];
                Require(selection == 2 && discarded.Pile?.Type == PileType.Discard,
                    "Sly autoplay must wait for the entire outer discard batch.");
                Require(options.SequenceEqual([nested]), "Nested Scry must inspect the new draw-pile top.");
                return [nested];
            }));
            await CardCmd.AutoPlay(Choice, judge, null);
            Require(selection == 2 && combat.Stock == 3 && combat.Enemy.CurrentHp == 982,
                "Scry/Sly must dispatch three discards and then gain three stock exactly once.");
            Require(drawRemoved == 3, "Nested Scry must update the native draw-pile counter for all three removals.");
            Require(combat.Player.Creature.Block == 8 && combat.Player.Creature.GetPower<EvokeObserver>()!.Discarded == 3,
                "ReadAhead must count its two discards only; discard powers must also see the nested discard.");
        }
        using (var combat = new OrbCombat())
        {
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            var second = AddCard<DefendIronclad>(combat, PileType.Draw);
            int selections = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                selections++;
                Require(options.SequenceEqual(selections == 1 ? [sly, second] : new CardModel[] { second }),
                    "Nested empty-pile Scry must reshuffle the completed outer discard, excluding the playing Sly card.");
                return options.ToArray();
            }));
            await PowerCmd.Apply<WatchfulBladesPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await ScryCmd.Execute(Choice, combat.Player, 2);
            Require(selections == 2 && combat.Stock == 5 && second.Pile?.Type == PileType.Discard && sly.Pile?.Type == PileType.Discard,
                "Both outer and shuffled nested Scry must finish their discards before rewarding stock.");
        }
        using (var combat = new OrbCombat())
        {
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [sly]));
            ScryResult result = await ScryCmd.Execute(Choice, combat.Player, 1, exhaustDiscarded: true);
            Require(result.ExhaustedCards == 1 && combat.Stock == 0 && sly.Pile?.Type == PileType.Exhaust,
                "Exhausting a Scry selection must not activate Sly or discard effects.");
        }
        GD.Print("PASS native hand discard, Scry/Sly batching, nested selection, local discard count and exhaust exclusion");
    }

    private static async Task VerifyMotherUnixBeforeDraw()
    {
        foreach (int turn in new[] { 1, 2 })
        foreach (string scenario in new[] { "keep", "discard", "nested", "short", "empty" })
        {
            using var combat = new OrbCombat();
            combat.Player.AddRelicInternal(ModelDb.Relic<NinjaSlayer.Relics.MotherUNIXAccessKeyRelic>().ToMutable());
            if (turn == 2) combat.Player.PlayerCombatState!.IncrementTurnNumber();
            int count = scenario == "empty" ? 0 : scenario == "short" ? 2 : 10;
            var cards = new List<CardModel>();
            for (int i = 0; i < count; i++)
                cards.Add(scenario == "nested" && i == 0
                    ? AddCard<ShurikenCreation>(combat, PileType.Draw)
                    : AddCard<DefendIronclad>(combat, PileType.Draw));

            int selections = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                selections++;
                Require(PileType.Hand.GetPile(combat.Player).IsEmpty,
                    "Mother UNIX and nested Sly must finish selecting before the normal hand is drawn.");
                if (selections == 1)
                {
                    Require(options.SequenceEqual(cards.Take(3)), "Mother UNIX must inspect the undrawn top three cards.");
                    return scenario is "discard" or "nested" ? cards.Take(2) : [];
                }
                Require(scenario == "nested" && selections == 2 && cards[1].Pile?.Type == PileType.Discard,
                    "Nested Sly must run once, after the entire outer discard batch.");
                Require(options.SequenceEqual(cards.Skip(2).Take(1)), "Nested Scry must see the remaining draw pile.");
                return [cards[2]];
            }));

            var other = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(
                MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 2);
            other.InitializeSeed("mother-unix-other");
            combat.State.AddPlayer(other);
            other.ResetCombatState();
            await Hook.BeforeHandDraw(combat.State, other, Choice);
            Require(selections == 0, "Mother UNIX must not request a choice on another player's turn.");

            var context = new HookPlayerChoiceContext(combat.Player, 1,
                MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType.CombatPlayPhaseOnly);
#if NINJASLAYER_CHANNEL_STABLE
            object[] args = [combat.Player, context];
#else
            object[] args = [AccessTools.Field(typeof(CombatManager), "_turnState").GetValue(CombatManager.Instance)!, combat.Player, context];
#endif
            await (Task)AccessTools.Method(typeof(CombatManager), "SetupPlayerTurn").Invoke(CombatManager.Instance, args)!;
            int removed = scenario == "nested" ? 3 : scenario == "discard" ? 2 : 0;
            Require(PileType.Hand.GetPile(combat.Player).Cards.SequenceEqual(cards.Skip(removed).Take(5)),
                $"Turn {turn}, {scenario}: native hand draw must use the post-Scry pile.");
            Require(selections == (scenario == "empty" ? 0 : scenario == "nested" ? 2 : 1),
                "Mother UNIX must not Scry again after the hand draw.");
            Require(combat.Stock == (scenario == "nested" ? 3 : 0), "Nested Sly must finish its effect exactly once.");
        }
        GD.Print("PASS Mother UNIX native turn setup: pre-draw Scry, first/later turn, owner-only, keep/discard/nested Sly, short/empty pile");
    }

    private static async Task VerifyStatusCards()
    {
        using (var combat = new OrbCombat())
        {
            var uke = AddCard<GreatUkemi>(combat);
            var tea = AddCard<Chado>(combat, PileType.Draw);
            tea.IncreaseEnergy(3);
            var wound = AddCard<Wound>(combat, PileType.Hand);
            var flame = AddCard<BlackFlame>(combat, PileType.Discard);
            await PowerCmd.Apply<DevourFlamePower>(Choice, combat.Player.Creature, 4, combat.Player.Creature, null);
            int before = combat.Player.PlayerCombatState!.Energy;
            await CardCmd.AutoPlay(Choice, uke, null);
            Require(new CardModel[] { uke, tea, wound, flame }.All(card => card.Pile?.Type == PileType.Exhaust),
                "Great Uke must exhaust Status cards from all three piles and itself.");
            Require(combat.Player.PlayerCombatState.Energy == before + 4
                && combat.Player.Creature.GetPowerAmount<BufferPower>() == 1
                && combat.Player.Creature.GetPowerAmount<StrengthPower>() == 0,
                "Great Uke must play Chado, skip Wound and exhaust Black Flame without the retired Strength reward.");
        }
        using (var combat = new OrbCombat())
        {
            var recovery = AddCard<Rekindle>(combat);
            var first = AddCard<DefendIronclad>(combat);
            var second = AddCard<DefendIronclad>(combat);
            var wound = AddCard<Wound>(combat);
            combat.Player.Creature.SetCurrentHpInternal(30);
            await PowerCmd.Apply<DevourFlamePower>(Choice, combat.Player.Creature, 4, combat.Player.Creature, null);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [first]));
            await CardCmd.AutoPlay(Choice, recovery, null);
            Require(combat.Player.Creature.CurrentHp == 30 && combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 4,
                "Transforming into Black Flame must trigger Devour Flame once without healing or a Rekindle self-reward.");
            Require(wound.Pile?.Type == PileType.Hand && second.Pile?.Type == PileType.Hand
                && PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1,
                "Recovery must transform exactly one selected card.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 4,
                "Recovery must ignore itself and attacks.");
            await CardCmd.AutoPlay(Choice, second, null);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 7,
                "Recovery must grant Naraku Life for a subsequent Skill.");
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
            Require(!combat.Player.Creature.HasPower<RekindlePower>(), "Recovery must expire this turn.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<Chado>(combat, PileType.Draw);
            AddCard<Wound>(combat, PileType.Draw);
            AddCard<DefendIronclad>(combat, PileType.Draw);
            await PowerCmd.Apply<ResiliencePower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await CardPileCmd.Draw(Choice, 1, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.Count == 1, "Resilience must not trigger when an existing Status is drawn.");
        }
        GD.Print("PASS status autoplay/exhaust, recovery transformation, turn expiry and Resilience draw isolation");
    }

    private static async Task VerifyTeaAndChop()
    {
        using (var combat = new OrbCombat())
        {
            var gather = AddCard<GatherKi>(combat);
            var tea = AddCard<Chado>(combat);
            tea.IncreaseEnergy(4);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [tea]));
            await CardCmd.AutoPlay(Choice, gather, null);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 10 && tea.Pile?.Type == PileType.Exhaust,
                "Gather Ki must read the selected Chado's accumulated energy.");
        }
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<Sip>(combat, upgraded: true), null);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<Chado>().Single().DynamicVars.Energy.BaseValue == 1,
                "Upgraded Sip Tea must breathe immediately.");
            for (int turn = 0; turn < 4; turn++) await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<Chado>().Single().DynamicVars.Energy.BaseValue == 4
                && !combat.Player.Creature.HasPower<SipPower>(), "Sip Tea must expire after exactly three turn starts.");
        }
        using (var combat = new OrbCombat())
        {
            var storm = AddCard<StormFist>(combat);
            await PlayerCmd.SetEnergy(10, combat.Player);
            Require(storm.CanPlay(), "Storm Fist must be playable without Chado.");
            foreach (var pile in new[] { PileType.Draw, PileType.Hand, PileType.Discard, PileType.Exhaust }) AddCard<Chado>(combat, pile);
            Require(storm.CanPlay(), "Three Chado across active piles must enable Storm Fist.");
            await CardCmd.AutoPlay(Choice, storm, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 972 && PileType.Exhaust.GetPile(combat.Player).Cards.OfType<Chado>().Count() == 1,
                "Storm Fist leaves active tea untouched and deals four hits of 4 + 3.");
        }
        GD.Print("PASS accumulated Chado, Sip Tea duration, Storm Fist playability/damage");
    }

    private static async Task VerifyTemporaryStats()
    {
        using var combat = new OrbCombat();
        await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
        await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
        await CardCmd.AutoPlay(Choice, AddCard<Onslaught>(combat), null);
        await CreatureCmd.GainBlock(combat.Enemy, 100, ValueProp.Unpowered, null);
        await CardCmd.AutoPlay(Choice, AddCard<StrongShuriken>(combat), combat.Enemy);
        Require(combat.Enemy.Block == 89 && combat.Player.Creature.GetPowerAmount<StrengthPower>() == 3
            && combat.Player.Creature.GetPowerAmount<FocusPower>() == 2, "Strong Shuriken must gain Focus damage and an attack card must not trigger Press the Advantage.");
        await AddStock(combat.Player, 1);
        await CardCmd.Discard(Choice, AddCard<AlabamaDrop>(combat));
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 5 && combat.Player.Creature.GetPowerAmount<FocusPower>() == 2,
            "Orb damage caused by discarding an Attack must count once, not as both attack and stock damage.");
        await CreatureCmd.Damage(Choice, combat.Enemy, 1, ValueProp.Unpowered, combat.Player.Creature);
        await CreatureCmd.Damage(Choice, combat.Player.Creature, 1, ValueProp.Unpowered, combat.Player.Creature);
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 5, "Independent unpowered damage and self damage must not grant stats.");
#if NINJASLAYER_CHANNEL_STABLE
        await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
        await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 3 && combat.Player.Creature.GetPowerAmount<FocusPower>() == 2,
            "Onslaught must remove only temporary gains at turn end.");
        GD.Print("PASS Focus-enhanced token, blocked attack, stock damage source separation and temporary-stat cleanup");
    }

    private static async Task VerifyDamageSourceMatrix()
    {
        using var combat = new OrbCombat();
        combat.AddEnemy();
        await CardCmd.AutoPlay(Choice, AddCard<Onslaught>(combat), null);
        await PowerCmd.Apply<BladeSweepPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
        await AddStock(combat.Player, 3);
        await CardCmd.AutoPlay(Choice, AddCard<Dualcast>(combat), null);
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 24
            && combat.Player.Creature.GetPowerAmount<FocusPower>() == 0,
            "Three stock evoked twice against two targets must grant twenty-four temporary Strength.");
        await PowerCmd.Apply<KaratePower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
        AddCard<BlackFlame>(combat);
        await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 24
            && combat.Player.Creature.GetPowerAmount<FocusPower>() == 0,
            "An attack with Karate and Black Flame must not grant orb-only Strength.");
        GD.Print("PASS multi-target multi-evoke stat counts and actual Karate/Black Flame source exclusion");
    }

    private static async Task VerifySweepDuration()
    {
        using var combat = new OrbCombat();
        var second = combat.AddEnemy();
        await AddStock(combat.Player, 3);
        await PowerCmd.Apply<BladeSweepPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
        await CardCmd.Discard(Choice, new[] { combat.Card(), combat.Card() });
        Require(combat.Enemy.CurrentHp == 988 && second.CurrentHp == 988 && combat.Player.Creature.HasPower<BladeSweepPower>(),
            "Blade Sweep must affect every shot this turn.");
#if NINJASLAYER_CHANNEL_STABLE
        await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
        await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
        await CardCmd.Discard(Choice, combat.Card());
        Require(combat.Enemy.CurrentHp + second.CurrentHp == 1970 && !combat.Player.Creature.HasPower<BladeSweepPower>(),
            "Blade Sweep must expire at turn end and restore single-target shots.");
        GD.Print("PASS Blade Sweep repeated shots and turn-end expiry");
    }

    private static async Task VerifyTurnAndSelectionEffects()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var untouched = combat.AddEnemy();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<StrengthPower>(Choice, owner, 3, owner, null);
            await PowerCmd.Apply<VigorPower>(Choice, owner, 7, owner, null);
            var card = AddCard<Endurance>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - 3 * (upgraded ? 15 : 14) && untouched.CurrentHp == 1000
                && owner.GetPowerAmount<KaratePower>() == (upgraded ? 5 : 4) && !owner.HasPower<VigorPower>()
                && !owner.HasPower<EndurancePower>(), "Endurance must resolve three selected-target hits before granting Karate, with native Strength/Vigor and no delayed power.");
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [owner]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [owner]);
#endif
            Require(owner.GetPowerAmount<KaratePower>() == (upgraded ? 5 : 4), "Endurance must not grant a second turn-end reward.");
        }
        using (var combat = new OrbCombat())
        {
            var chop = AddCard<HellChop>(combat, PileType.Discard);
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
            Require(chop.Pile?.Type == PileType.Discard, "Skills must not return Strong Chop.");
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(chop.Pile?.Type == PileType.Hand, "The third Attack must return Strong Chop.");
        }
        using (var combat = new OrbCombat())
        {
            var copy = AddCard<FurinKazan>(combat, upgraded: true);
            var first = AddCard<StrikeIronclad>(combat, upgraded: true);
            var second = AddCard<DefendIronclad>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [first, second]));
            await CardCmd.AutoPlay(Choice, copy, null);
            var clones = PileType.Draw.GetPile(combat.Player).Cards;
            Require(clones.Count == 2 && clones[0].Id == first.Id && clones[0].IsUpgraded
                && clones[1].Id == second.Id && first.Pile?.Type == PileType.Hand && second.Pile?.Type == PileType.Hand,
                "Upgraded Chado Furin Kazan must place two independent copies on draw top in selection order.");
        }
        using (var combat = new OrbCombat())
        {
            var retained = AddCard<DefendIronclad>(combat);
            var card = AddCard<Assess>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => []));
            await CardCmd.AutoPlay(Choice, card, null);
            Require(!retained.ShouldRetainThisTurn && combat.Player.Creature.Block == 4, "Assess grants block without Retain.");
            retained.EndOfTurnCleanup();
            Require(!retained.ShouldRetainThisTurn, "Assess must not add Retain after cleanup.");
        }
        using (var combat = new OrbCombat())
        {
            var storm = AddCard<ShurikenStorm>(combat, upgraded: true);
            AddCard<DefendIronclad>(combat);
            AddCard<DefendIronclad>(combat);
            await CardCmd.AutoPlay(Choice, storm, null);
            Require(combat.Stock == 3 && combat.Enemy.CurrentHp == 988 && storm.Pile?.Type == PileType.Exhaust,
                "Shuriken Storm must grant initial stock, dispatch every hand discard, then replenish by discarded count.");
        }
        using (var combat = new OrbCombat())
        {
            var second = combat.AddEnemy();
            var tea = AddCard<Chado>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [tea]));
            await CardCmd.AutoPlay(Choice, AddCard<TomoeThrow>(combat, upgraded: true), combat.Enemy);
            Require(combat.Enemy.GetPowerAmount<WeakPower>() == 3 && !second.HasPower<WeakPower>()
                && combat.Player.Creature.Block == 11 && tea.Pile?.Type == PileType.Exhaust,
                "Sudden Guard must consume tea, grant eleven Block and weaken only its selected target.");
        }
        GD.Print("PASS Endurance three hits, post-attack Karate and no delayed reward, Strong Chop attack counter, two-card copying, retention, Shuriken Storm and selected-target Weak");
    }

    private sealed class SelectCards(Func<CardModel[], IEnumerable<CardModel>> select) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            CardModel[] available = options.ToArray();
            CardModel[] selected = select(available).ToArray();
            Require(selected.Length >= minSelect && selected.Length <= maxSelect && selected.All(available.Contains), "Invalid scripted card choice.");
            return Task.FromResult<IEnumerable<CardModel>>(selected);
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            throw new InvalidOperationException("No reward choice is expected in card contracts.");
    }
}
