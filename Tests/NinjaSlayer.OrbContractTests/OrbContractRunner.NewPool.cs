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
using NinjaSlayer.Cards.RedesignV1;
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
            foreach (Type type in typeof(Prejudge).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type)))
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
        GD.Print("PASS bilingual base/upgraded card descriptions");
        if (_hasPresentationResources)
            GD.Print("PASS mechanism tips, generated previews and native event text");
    }

    private static async Task VerifyDynamicCardNumbers()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var palm = AddCard<PalmThrustRedesignV1>(combat, upgraded: upgraded);
            var adjustment = AddCard<CombatAdjustmentRedesignV1>(combat, upgraded: upgraded);
            var judge = AddCard<Prejudge>(combat, upgraded: upgraded);
            var storm = AddCard<StormFistRedesignV1>(combat, upgraded: upgraded);
            AddCard<ChadoEnergyRedesignV1>(combat, PileType.Exhaust);

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
            Expect(palm, 8);
            Expect(adjustment, upgraded ? 14 : 10);
            Expect(judge, 6, block: true);
            Expect(storm, upgraded ? 15 : 11);

            await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, -5, combat.Player.Creature, null);
            await PowerCmd.Apply<DexterityPower>(Choice, combat.Player.Creature, -4, combat.Player.Creature, null);
            Expect(palm, 3);
            Expect(adjustment, upgraded ? 9 : 5);
            Expect(judge, 2, block: true);
            Expect(storm, upgraded ? 10 : 6);

            await PowerCmd.Apply<WeakPower>(Choice, combat.Player.Creature, 1, combat.Enemy, null);
            await PowerCmd.Apply<FrailPower>(Choice, combat.Player.Creature, 1, combat.Enemy, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 1, combat.Player.Creature, null);
            Expect(palm, 3);
            Expect(adjustment, upgraded ? 10 : 5);
            Expect(judge, 1, block: true);
            Expect(storm, upgraded ? 11 : 6);
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
        using (var combat = new OrbCombat())
        {
            var judge = AddCard<Prejudge>(combat);
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            var discarded = AddCard<DefendIronclad>(combat, PileType.Draw);
            var nested = AddCard<DefendIronclad>(combat, PileType.Draw);
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
            Require(selection == 2 && combat.Stock == 2 && combat.Enemy.CurrentHp == 982,
                "Scry/Sly must dispatch three discards and then gain two stock exactly once.");
            Require(combat.Player.Creature.Block == 8 && combat.Player.Creature.GetPower<EvokeObserver>()!.Discarded == 3,
                "Prejudge must count its two discards only; discard powers must also see the nested discard.");
        }
        using (var combat = new OrbCombat())
        {
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            var second = AddCard<DefendIronclad>(combat, PileType.Draw);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [sly, second]));
            await PowerCmd.Apply<RecycledBladesPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await ScryCmd.Execute(Choice, combat.Player, 2);
            Require(combat.Stock == 3 && second.Pile?.Type == PileType.Discard && sly.Pile?.Type == PileType.Discard,
                "Scry must dispatch both discards before Sly generates new stock.");
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
            combat.Player.AddRelicInternal(ModelDb.Relic<NinjaSlayer.Relics.MotherUnixRelic>().ToMutable());
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
            Require(combat.Stock == (scenario == "nested" ? 2 : 0), "Nested Sly must finish its effect exactly once.");
        }
        GD.Print("PASS Mother UNIX native turn setup: pre-draw Scry, first/later turn, owner-only, keep/discard/nested Sly, short/empty pile");
    }

    private static async Task VerifyStatusCards()
    {
        using (var combat = new OrbCombat())
        {
            var uke = AddCard<GreatUkeRedesignV1>(combat);
            var tea = AddCard<ChadoEnergyRedesignV1>(combat, PileType.Draw);
            tea.IncreaseEnergy(3);
            var wound = AddCard<Wound>(combat, PileType.Hand);
            var flame = AddCard<BlackFlameRedesignV1>(combat, PileType.Discard);
            await PowerCmd.Apply<ReturnReturnReturnPower>(Choice, combat.Player.Creature, 4, combat.Player.Creature, null);
            int before = combat.Player.PlayerCombatState!.Energy;
            await CardCmd.AutoPlay(Choice, uke, null);
            Require(new CardModel[] { uke, tea, wound, flame }.All(card => card.Pile?.Type == PileType.Exhaust),
                "Great Uke must exhaust Status cards from all three piles and itself.");
            Require(combat.Player.PlayerCombatState.Energy == before + 4
                && combat.Player.Creature.GetPowerAmount<BufferPower>() == 1
                && combat.Player.Creature.GetPowerAmount<StrengthPower>() == 4,
                "Great Uke must play Chado, skip Wound and trigger Black Flame exhaust once.");
        }
        using (var combat = new OrbCombat())
        {
            var recovery = AddCard<BlackFlameRecovery>(combat);
            var first = AddCard<DefendIronclad>(combat);
            var second = AddCard<DefendIronclad>(combat);
            var wound = AddCard<Wound>(combat);
            combat.Player.Creature.SetCurrentHpInternal(30);
            await PowerCmd.Apply<ReturnReturnReturnPower>(Choice, combat.Player.Creature, 4, combat.Player.Creature, null);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [first]));
            await CardCmd.AutoPlay(Choice, recovery, null);
            Require(combat.Player.Creature.CurrentHp == 30 && combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 0,
                "Recovery must not heal or exhaust hand statuses.");
            Require(wound.Pile?.Type == PileType.Hand && second.Pile?.Type == PileType.Hand
                && PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlameRedesignV1>().Count() == 1,
                "Recovery must transform exactly one selected card.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 3,
                "Recovery must grant Naraku Life once per attack played.");
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
            Require(!combat.Player.Creature.HasPower<BlackFlameRecoveryPower>(), "Recovery must expire this turn.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<ChadoEnergyRedesignV1>(combat, PileType.Draw);
            AddCard<Wound>(combat, PileType.Draw);
            AddCard<DefendIronclad>(combat, PileType.Draw);
            await PowerCmd.Apply<StatusDrawPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await CardPileCmd.Draw(Choice, 1, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.Count == 3, "Status Draw must chain across successive drawn statuses.");
        }
        GD.Print("PASS status autoplay/exhaust, recovery transformation and turn expiry and chained Status Draw");
    }

    private static async Task VerifyTeaAndChop()
    {
        using (var combat = new OrbCombat())
        {
            var gather = AddCard<GatherKi>(combat);
            var tea = AddCard<ChadoEnergyRedesignV1>(combat);
            tea.IncreaseEnergy(4);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [tea]));
            await CardCmd.AutoPlay(Choice, gather, null);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 10 && tea.Pile?.Type == PileType.Exhaust,
                "Gather Ki must read the selected Chado's accumulated energy.");
        }
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<SipTea>(combat, upgraded: true), null);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>().Single().DynamicVars.Energy.BaseValue == 1,
                "Upgraded Sip Tea must breathe immediately.");
            for (int turn = 0; turn < 4; turn++) await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>().Single().DynamicVars.Energy.BaseValue == 4
                && !combat.Player.Creature.HasPower<SipTeaPower>(), "Sip Tea must expire after exactly three turn starts.");
        }
        using (var combat = new OrbCombat())
        {
            var storm = AddCard<StormFistRedesignV1>(combat);
            await PlayerCmd.SetEnergy(10, combat.Player);
            Require(storm.CanPlay(), "Storm Fist must be playable without Chado.");
            foreach (var pile in new[] { PileType.Draw, PileType.Hand, PileType.Discard, PileType.Exhaust }) AddCard<ChadoEnergyRedesignV1>(combat, pile);
            Require(storm.CanPlay(), "Three Chado across active piles must enable Storm Fist.");
            await CardCmd.AutoPlay(Choice, storm, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 920 && PileType.Exhaust.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>().Count() == 4,
                "Storm Fist must exhaust before calculating four hits of 4 + 4*4.");
        }
        GD.Print("PASS accumulated Chado, Sip Tea duration, Storm Fist playability/damage");
    }

    private static async Task VerifyTemporaryStats()
    {
        using var combat = new OrbCombat();
        await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
        await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
        await CardCmd.AutoPlay(Choice, AddCard<Wasssssshoi>(combat), null);
        await CreatureCmd.GainBlock(combat.Enemy, 100, ValueProp.Unpowered, null);
        await CardCmd.AutoPlay(Choice, AddCard<StrongShurikenTokenRedesignV1>(combat), combat.Enemy);
        Require(combat.Enemy.Block == 89 && combat.Player.Creature.GetPowerAmount<StrengthPower>() == 3
            && combat.Player.Creature.GetPowerAmount<FocusPower>() == 2, "Strong Shuriken must gain Focus damage and an attack card must not trigger Press the Advantage.");
        await AddStock(combat.Player, 1);
        await CardCmd.Discard(Choice, AddCard<AlabamaDropRedesignV1>(combat));
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 4 && combat.Player.Creature.GetPowerAmount<FocusPower>() == 2,
            "Orb damage caused by discarding an Attack must count once, not as both attack and stock damage.");
        await CreatureCmd.Damage(Choice, combat.Enemy, 1, ValueProp.Unpowered, combat.Player.Creature);
        await CreatureCmd.Damage(Choice, combat.Player.Creature, 1, ValueProp.Unpowered, combat.Player.Creature);
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 4, "Independent unpowered damage and self damage must not grant stats.");
#if NINJASLAYER_CHANNEL_STABLE
        await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
        await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 3 && combat.Player.Creature.GetPowerAmount<FocusPower>() == 2,
            "Wasssssshoi must remove only temporary gains at turn end.");
        GD.Print("PASS Focus-enhanced token, blocked attack, stock damage source separation and temporary-stat cleanup");
    }

    private static async Task VerifyDamageSourceMatrix()
    {
        using var combat = new OrbCombat();
        combat.AddEnemy();
        await CardCmd.AutoPlay(Choice, AddCard<Wasssssshoi>(combat), null);
        await PowerCmd.Apply<BladeSweepPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
        await AddStock(combat.Player, 3);
        await CardCmd.AutoPlay(Choice, AddCard<Dualcast>(combat), null);
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 4
            && combat.Player.Creature.GetPowerAmount<FocusPower>() == 0,
            "Two AOE shots against two targets must grant four stat increments.");
        await PowerCmd.Apply<KaratePower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
        AddCard<BlackFlameRedesignV1>(combat);
        await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
        Require(combat.Player.Creature.GetPowerAmount<StrengthPower>() == 4
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
        foreach (bool attackFirst in new[] { false, true })
        {
            using var combat = new OrbCombat();
            if (attackFirst) await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            await CardCmd.AutoPlay(Choice, AddCard<Endurance>(combat), null);
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == (attackFirst ? 1 : 4)
                && !combat.Player.Creature.HasPower<EndurancePower>(), "Endurance must include attacks played before it and expire this turn.");
        }
        using (var combat = new OrbCombat())
        {
            var chop = AddCard<ChopRedesignV1>(combat, PileType.Discard);
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
            Require(chop.Pile?.Type == PileType.Discard, "Skills must not return Strong Chop.");
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(chop.Pile?.Type == PileType.Hand, "The third Attack must return Strong Chop.");
        }
        using (var combat = new OrbCombat())
        {
            var copy = AddCard<ChadoFurinKazanRedesignV1>(combat, upgraded: true);
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
            var card = AddCard<TonyRetention>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [retained]));
            await CardCmd.AutoPlay(Choice, card, null);
            Require(retained.ShouldRetainThisTurn && combat.Player.Creature.Block == 11, "Macaco must give the selected card Retain.");
            retained.EndOfTurnCleanup();
            Require(retained.ShouldRetainThisTurn, "Macaco must grant permanent combat Retain.");
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
            var tea = AddCard<ChadoEnergyRedesignV1>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [tea]));
            await CardCmd.AutoPlay(Choice, AddCard<ObserveBattlefield>(combat, upgraded: true), combat.Enemy);
            Require(combat.Enemy.GetPowerAmount<WeakPower>() == 3 && !second.HasPower<WeakPower>()
                && combat.Player.Creature.Block == 7 && tea.Pile?.Type == PileType.Exhaust,
                "Sudden Guard must consume tea, grant seven Block and weaken only its selected target.");
        }
        GD.Print("PASS Endurance turn history, Strong Chop attack counter, two-card copying, retention, Shuriken Storm and selected-target Weak");
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
