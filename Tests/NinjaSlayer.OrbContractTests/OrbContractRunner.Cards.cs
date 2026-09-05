using Godot;
using HarmonyLib;
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
            !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type)) == 86,
            "The product assembly must contain only the 86 current card models.");
        await VerifyGreatUkeThreshold();
        await VerifyChadoGeneration();
        await VerifyNarakuForms();
        VerifyNarakuEventEligibility();
    }

    private static async Task VerifyGreatUkeThreshold()
    {
        foreach (decimal damage in new[] { 10m, 10.5m, 11m })
        {
            using var combat = new OrbCombat();
            var power = await PowerCmd.Apply<GreatUkeRedesignPower>(Choice,
                combat.Player.Creature, 2, combat.Player.Creature, null);
            Require(power is not null, "Great Uke was not applied.");
            decimal result = power!.ModifyHpLostAfterOstyLate(combat.Player.Creature,
                damage, ValueProp.Unpowered, combat.Enemy, null);
            Require(result == (damage > 10m ? 0 : damage), $"Great Uke incorrectly resolved {damage} damage.");
            if (result != damage) await power.AfterModifyingHpLostAfterOsty();
            Require(power.Amount == (damage > 10m ? 1 : 2), "Great Uke consumed an incorrect number of charges.");
        }
        GD.Print("PASS Great Uke at 10, 10.5 and 11 damage");
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
            typeof(NarakuFormRedesignV1), typeof(ReturnReturnReturnRedesignV1), typeof(OneBodyOneSoul)];
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
