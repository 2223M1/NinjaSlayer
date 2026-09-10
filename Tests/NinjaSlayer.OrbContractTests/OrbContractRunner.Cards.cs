using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Orbs;
using NinjaSlayer.Potions;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyCurrentCardInteractions()
    {
        Require(typeof(ShurikenOrb).Assembly.GetTypes().Count(type =>
            !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type)) == 92,
            "The product assembly must contain only the 92 current card models.");
        await VerifyNewCardInteractions();
        await VerifyChadoGeneration();
        await VerifyOpeningChadoRetention();
        await VerifyBlackFlameTurnEnd();
        await VerifyNarakuForms();
        VerifyNarakuEventEligibility();
    }

    private static async Task VerifyChadoGeneration()
    {
        using var combat = new OrbCombat();
        await PowerCmd.Apply<KarateTeaPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
        await ChadoBreathCmd.Apply(combat.Player, 2);
        ChadoEnergyRedesignV1 tea = PileType.Hand.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>().Single();
        Require(tea.DynamicVars.Energy.BaseValue == 2 && combat.Player.Creature.GetPowerAmount<KaratePower>() == 3,
            "First Chado Breathing must create one 2-energy Chado and trigger Karate Tea once.");
        await ChadoBreathCmd.Apply(combat.Player, 2);
        Require(tea.DynamicVars.Energy.BaseValue == 4 && combat.Player.Creature.GetPowerAmount<KaratePower>() == 3,
            "Increasing held Chado must preserve its identity and not trigger generation effects.");
        var retain = await PowerCmd.Apply<ChadoRetainPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
        CardModel strike = combat.Card();
        await retain!.BeforeFlush(Choice, combat.Player);
        Require(tea.ShouldRetainThisTurn && !strike.ShouldRetainThisTurn, "Chado retention affected another card.");
        GD.Print("PASS first/repeated Chado Breathing, generation hooks and Chado-only retention");
    }

    private static async Task VerifyOpeningChadoRetention()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            RelicModel relic = upgraded
                ? ModelDb.Relic<DeepChadoBreathingRelic>().ToMutable()
                : ModelDb.Relic<ChadoBreathingRelic>().ToMutable();
            combat.Player.AddRelicInternal(relic);
            await relic.BeforeHandDraw(combat.Player, Choice, combat.State);
            var opening = PileType.Hand.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>().ToArray();
            Require(opening.Length == (upgraded ? 2 : 1)
                && opening.All(card => card.Keywords.Contains(CardKeyword.Retain) == upgraded
                    && card.DynamicVars.Energy.BaseValue == (upgraded ? 3 : 2)),
                "Starter breathes two with first-turn tea retention; ancient generates two retained tea then breathes two.");
            foreach (var tea in opening)
                await CardPileCmd.Add(tea, PileType.Discard);
            await ChadoBreathCmd.Apply(combat.Player, 2);
            var later = PileType.Hand.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>().Single();
            Require(!later.Keywords.Contains(CardKeyword.Retain), "Later tea must not inherit the opening relic's Retain.");
            await CardPileCmd.Add(opening[0], PileType.Hand);
            Require(opening[0].Keywords.Contains(CardKeyword.Retain) == upgraded, "Pile changes must preserve opening tea's keywords.");
            Require(opening[0].MutableClone() is CardModel copy && copy.Keywords.Contains(CardKeyword.Retain) == upgraded,
                "Native copies must preserve opening tea's keywords.");
            combat.Player.PlayerCombatState!.IncrementTurnNumber();
            int count = PileType.Hand.GetPile(combat.Player).Cards.Count;
            await relic.BeforeHandDraw(combat.Player, Choice, combat.State);
            Require(PileType.Hand.GetPile(combat.Player).Cards.Count == count && later.DynamicVars.Energy.BaseValue == 2,
                "Opening relic effects must not repeat on later turns.");
        }
        GD.Print("PASS starter breath two; ancient retained tea, later ordinary tea, piles and copies");
    }

    private static async Task VerifyBlackFlameTurnEnd()
    {
        using var combat = new OrbCombat();
        await PowerCmd.Apply<ReturnReturnReturnPower>(Choice, combat.Player.Creature, 6, combat.Player.Creature, null);
        var flames = new[] { AddCard<BlackFlameRedesignV1>(combat), AddCard<BlackFlameRedesignV1>(combat) };
        foreach (var flame in flames)
        {
#if NINJASLAYER_CHANNEL_STABLE
            await flame.OnTurnEndInHandWrapper(Choice);
#else
            await (Task)AccessTools.Method(typeof(CombatManager), "ResolveTurnEndCardEffects")
                .Invoke(CombatManager.Instance, [flame, Choice, Task.CompletedTask])!;
#endif
        }
        // The second flame spends four of the first flame's six Naraku Life before granting six more.
        Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 8
            && PileType.Exhaust.GetPile(combat.Player).Cards.Count == 2,
            "Native end-turn must exhaust each Black Flame once and trigger Return Return Return once per card.");
        GD.Print("PASS native Black Flame end-turn and stacked Return Return Return without duplicate exhaust");
    }

    private static async Task VerifyNarakuForms()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        Require(NinjaSlayerFormState.GetPresentation(combat.Player.Creature).Kind == NinjaSlayerFormKind.Normal,
            "A new combat must start in normal form.");
        var potion = ModelDb.Potion<ZbrAmpoulePotion>().ToMutable();
        potion.Owner = combat.Player;
        await (Task)AccessTools.Method(typeof(ZbrAmpoulePotion), "OnUse").Invoke(potion, [Choice, combat.Player.Creature])!;
        Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 12
            && combat.Player.Creature.GetPowerAmount<StrengthPower>() == 0
            && NinjaSlayerFormState.GetPresentation(combat.Player.Creature).Kind == NinjaSlayerFormKind.Normal,
            "ZBR must grant exactly 12 Naraku Life without Strength or transformation.");
        var form = await PowerCmd.Apply<NarakuFormRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
        Require(NinjaSlayerFormState.GetPresentation(combat.Player.Creature).Kind == NinjaSlayerFormKind.Naraku,
            "Naraku Form must select the half-Naraku presentation.");
        CardModel attack = combat.Card();
        Require(form!.TryModifyEnergyCostInCombatLate(attack, 1, out decimal cost) && cost == 0,
            "Naraku Form must make attacks free.");
        await PowerCmd.Remove(form);
        Require(NinjaSlayerFormState.GetPresentation(combat.Player.Creature).Kind == NinjaSlayerFormKind.Normal,
            "Removing Naraku Form must restore normal presentation.");
        var relic = ModelDb.Relic<NarakuWithinRelic>().ToMutable();
        combat.Player.AddRelicInternal(relic);
        await relic.BeforeCombatStart();
        Require(combat.Player.Creature.GetPower<NarakuFormRedesignPower>() is not null
            && NinjaSlayerFormState.GetPresentation(combat.Player.Creature).Kind == NinjaSlayerFormKind.FullyReleasedNaraku,
            "The event relic must grant Naraku Form at combat start with the fully released presentation.");
        GD.Print("PASS ZBR, half Naraku, form removal and fully released event relic");
    }

    private static void VerifyNarakuEventEligibility()
    {
        var player = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
        var run = RunState.CreateForTest([player]);
        var model = ModelDb.Event<NarakuEvent>();
        Require(!model.IsAllowed(run), "The starter deck must not qualify for the Naraku event.");
        Type[] qualifying = [typeof(GuidingFlameRedesignV1), typeof(SatsubatsuRedesignV1), typeof(AbyssStrengthRedesignV1),
            typeof(HardItOutRedesignV1), typeof(RedBlackFlameAttackRedesignV1), typeof(BurnBurnBurnRedesignV1),
            typeof(NarakuFormRedesignV1), typeof(ReturnReturnReturnRedesignV1), typeof(BlackFlameRecovery), typeof(OneBodyOneSoul)];
        foreach (CardModel canonical in ModelDb.CardPool<NinjaSlayerCardPool>().AllCards)
        {
            CardModel card = canonical.ToMutable();
            card.Owner = player;
            player.Deck.AddInternal(card, -1, silent: true);
            Require(model.IsAllowed(run) == qualifying.Contains(card.GetType()), $"Incorrect Naraku event eligibility for {card.Id}.");
            player.Deck.RemoveInternal(card, silent: true);
        }
        GD.Print("PASS Naraku event eligibility across the current card pool");
    }
}
