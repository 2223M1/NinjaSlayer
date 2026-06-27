using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Content;

public static class NinjaSlayerActions
{
    public static async Task EnterNaraku(PlayerChoiceContext choiceContext, Player player, decimal life = 12)
    {
        if (player.Creature.HasPower<OneBodyOneSoulPower>())
        {
            await PlayerCmd.GainEnergy(1, player);
            await PowerCmd.Apply<NarakuLifePower>(choiceContext, player.Creature, 6, player.Creature, null);
            return;
        }

        await PowerCmd.Apply<NarakuPower>(choiceContext, player.Creature, 1, player.Creature, null);
        await PowerCmd.Apply<NarakuLifePower>(choiceContext, player.Creature, life, player.Creature, null);
    }

    public static async Task ExitNaraku(Creature creature)
    {
        await PowerCmd.Remove<NarakuPower>(creature);
        await PowerCmd.Remove<NarakuLifePower>(creature);
    }

    public static async Task AddGeneratedCard<T>(Player owner, PileType pile, CardPilePosition position = CardPilePosition.Bottom)
        where T : CardModel
    {
        ICombatState combatState = owner.Creature.CombatState ?? throw new InvalidOperationException("Generated cards require an active combat state.");
        CardPileAddResult result = await CardPileCmd.AddGeneratedCardToCombat(combatState.CreateCard<T>(owner), pile, owner, position);
        PreviewGeneratedPileAdd(pile, result);
    }

    public static async Task AddGeneratedCards<T>(Player owner, int count, PileType pile, CardPilePosition position = CardPilePosition.Bottom)
        where T : CardModel
    {
        if (count <= 0 || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        ICombatState combatState = owner.Creature.CombatState ?? throw new InvalidOperationException("Generated cards require an active combat state.");
        List<CardModel> cards = new();
        for (int i = 0; i < count; i++)
        {
            cards.Add(combatState.CreateCard<T>(owner));
        }

        IReadOnlyList<CardPileAddResult> results = await CardPileCmd.AddGeneratedCardsToCombat(cards, pile, owner, position);
        PreviewGeneratedPileAdd(pile, results);
    }

    public static async Task AddGeneratedShuriken(PlayerChoiceContext choiceContext, Player owner, int count, PileType pile, bool upgraded = false, CardPilePosition position = CardPilePosition.Bottom)
    {
        if (count <= 0 || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        ICombatState combatState = owner.Creature.CombatState ?? throw new InvalidOperationException("Generated cards require an active combat state.");
        List<CardModel> cards = new();
        if (owner.Creature.HasPower<StarlessNightPower>())
        {
            for (int i = 0; i < count; i++)
            {
                CardModel shuriken = combatState.CreateCard<ShurikenCard>(owner);
                if (upgraded)
                {
                    CardCmd.Upgrade(shuriken);
                }

                cards.Add(shuriken);
            }

            IReadOnlyList<CardPileAddResult> shurikenResults = await CardPileCmd.AddGeneratedCardsToCombat(cards, pile, owner, position);
            PreviewGeneratedPileAdd(pile, shurikenResults);

            CardModel giantShuriken = combatState.CreateCard<GiantShurikenCard>(owner);
            if (await ExhaustAllShuriken(choiceContext, owner))
            {
                CardCmd.Upgrade(giantShuriken);
            }

            await CardPileCmd.AddGeneratedCardToCombat(giantShuriken, PileType.Hand, owner);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            CardModel card = combatState.CreateCard<ShurikenCard>(owner);
            if (upgraded)
            {
                CardCmd.Upgrade(card);
            }

            cards.Add(card);
        }

        IReadOnlyList<CardPileAddResult> results = await CardPileCmd.AddGeneratedCardsToCombat(cards, pile, owner, position);
        PreviewGeneratedPileAdd(pile, results);
    }

    private static void PreviewGeneratedPileAdd(PileType pile, CardPileAddResult result)
    {
        if (ShouldPreviewGeneratedPileAdd(pile))
        {
            CardCmd.PreviewCardPileAdd(result);
        }
    }

    private static void PreviewGeneratedPileAdd(PileType pile, IReadOnlyList<CardPileAddResult> results)
    {
        if (ShouldPreviewGeneratedPileAdd(pile) && results.Count > 0)
        {
            CardCmd.PreviewCardPileAdd(results);
        }
    }

    private static bool ShouldPreviewGeneratedPileAdd(PileType pile) => pile is PileType.Draw or PileType.Discard;

    private static async Task<bool> ExhaustAllShuriken(PlayerChoiceContext choiceContext, Player owner)
    {
        bool exhaustedUpgradedShuriken = false;
        foreach (CardModel shuriken in owner.PlayerCombatState?.AllCards
            .Where(c => c.Pile?.Type != PileType.Exhaust && c.Tags.Contains(NinjaSlayerCardTags.Shuriken))
            .ToList() ?? [])
        {
            if (shuriken.Pile?.Type != PileType.Exhaust)
            {
                exhaustedUpgradedShuriken |= shuriken.IsUpgraded;
                await CardCmd.Exhaust(choiceContext, shuriken);
            }
        }

        return exhaustedUpgradedShuriken;
    }

    public static async Task TriggerKarate(PlayerChoiceContext choiceContext, Creature? dealer, Creature target, CardModel? cardSource)
    {
        if (dealer == null || dealer.Side == target.Side)
        {
            return;
        }

        KaratePower? karate = target.GetPower<KaratePower>();
        if (karate == null || karate.Amount <= 0)
        {
            return;
        }

        int extraDamage = karate.Amount;
        await CreatureCmd.Damage(choiceContext, target, extraDamage, ValueProp.Unpowered, dealer, null);
        if (!target.IsDead)
        {
            await PowerCmd.ModifyAmount(choiceContext, karate, -1, dealer, cardSource);
        }
    }

    public static async Task ClearAllKarate(PlayerChoiceContext choiceContext, Player player)
    {
        ICombatState combatState = player.Creature.CombatState ?? throw new InvalidOperationException("Karate can only be cleared during combat.");
        foreach (Creature enemy in combatState.Enemies)
        {
            await PowerCmd.Remove<KaratePower>(enemy);
        }
    }
}
