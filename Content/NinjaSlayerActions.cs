using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Content;

public static class NinjaSlayerActions
{
    private const int NarakuEntryLife = 12;
    private const int OneBodyOneSoulNarakuLife = 12;

    public static async Task EnsureNarakuForm(PlayerChoiceContext choiceContext, Player player)
    {
        if (player.Creature.HasPower<OneBodyOneSoulPower>())
        {
            await PlayerCmd.GainEnergy(2, player);
            await CardPileCmd.Draw(choiceContext, 2, player);
            await PowerCmd.Apply<NarakuLifePower>(choiceContext, player.Creature, OneBodyOneSoulNarakuLife, player.Creature, null);
            return;
        }

        if (player.Creature.HasPower<NarakuPower>())
        {
            return;
        }

        await PowerCmd.Apply<NarakuPower>(choiceContext, player.Creature, 1, player.Creature, null);
        await PowerCmd.Apply<NarakuLifePower>(choiceContext, player.Creature, NarakuEntryLife, player.Creature, null);
    }

    public static async Task EnterNaraku(PlayerChoiceContext choiceContext, Player player, decimal life = 12)
    {
        await EnsureNarakuForm(choiceContext, player);

        if (player.Creature.HasPower<OneBodyOneSoulPower>() || life <= 0)
        {
            return;
        }

        await PowerCmd.Apply<NarakuLifePower>(choiceContext, player.Creature, life, player.Creature, null);
    }

    public static async Task ExitNaraku(Creature creature)
    {
        await PowerCmd.Remove<NarakuPower>(creature);
        await PowerCmd.Remove<NarakuLifePower>(creature);
    }

    // Tea-count scaling: how many Chado cards are currently held. Used both for gameplay branches
    // and as a static multiplier for CalculatedDamageVar/CalculatedBlockVar (must stay static).
    public static int ChadoInHandCount(Player player) =>
        PileType.Hand.GetPile(player).Cards.Count(c => c is ChadoCard);

    public static decimal ChadoInHandMultiplier(CardModel card, Creature? _) =>
        PileType.Hand.GetPile(card.Owner).Cards.Count(c => c is ChadoCard);

    public static decimal ChadoInExhaustPileMultiplier(CardModel card, Creature? _) =>
        PileType.Exhaust.GetPile(card.Owner).Cards.Count(c => c is ChadoCard);

    public static async Task AddGeneratedCard<T>(Player owner, PileType pile, CardPilePosition position = CardPilePosition.Bottom)
        where T : CardModel
    {
        ICombatState combatState = owner.Creature.CombatState ?? throw new InvalidOperationException("Generated cards require an active combat state.");
        CardPileAddResult result = await CardPileCmd.AddGeneratedCardToCombat(combatState.CreateCard<T>(owner), pile, owner, position);
        PreviewGeneratedPileAdd(pile, result);
    }

    public static async Task AddGeneratedShuriken(
        PlayerChoiceContext choiceContext,
        Player owner,
        int count,
        PileType pile,
        bool upgraded = false,
        CardPilePosition position = CardPilePosition.Bottom,
        bool prepare = false)
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

        if (prepare)
        {
            foreach (CardPileAddResult result in results)
            {
                if (!result.success || !await PrepareCmd.Apply(result.cardAdded))
                {
                    throw new InvalidOperationException(
                        "Generated Shuriken was not prepared as requested.");
                }
            }
        }
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

    public static async Task TriggerKarateWave(
        PlayerChoiceContext choiceContext,
        Creature dealer,
        IReadOnlyList<Creature> targets,
        KaratePower karate,
        int extraDamage,
        CardModel? cardSource)
    {
        if (targets.Count == 0 || extraDamage <= 0)
        {
            return;
        }

        using var _ = ScreenShakeSuppressionContext.Suppress();
        await CreatureCmd.Damage(choiceContext, targets, extraDamage, ValueProp.Unpowered, dealer);

        ICombatState? combatState = dealer.CombatState;
        if (combatState != null)
        {
            foreach (Player player in combatState.Players)
            {
                if (player.Creature.GetPower<ShieldFromNothingPower>() is { } shieldFromNothing)
                {
                    await shieldFromNothing.OnKarateTriggered(choiceContext);
                }
            }
        }

        int amountBeforeConsumption = karate.Amount;
        await PowerCmd.ModifyAmount(choiceContext, karate, -1, dealer, cardSource);
        if (CombatManager.Instance.IsEnding && karate.Amount == amountBeforeConsumption)
        {
            // PowerCmd rejects amount changes after the bonus ends combat, but the triggering wave still consumes.
            karate.SetAmount(Math.Max(0, amountBeforeConsumption - 1), silent: true);
        }
    }

    public static async Task<int> ClearAllKarate(PlayerChoiceContext choiceContext, Player player)
    {
        ICombatState combatState = player.Creature.CombatState ?? throw new InvalidOperationException("Karate can only be cleared during combat.");

        int clearedUnits = 0;
        foreach (Creature creature in combatState.Creatures.ToList())
        {
            KaratePower? karate = creature.GetPower<KaratePower>();
            if (karate == null || karate.Amount <= 0)
            {
                continue;
            }

            clearedUnits++;
            await PowerCmd.Remove(karate);
        }

        return clearedUnits;
    }
}
