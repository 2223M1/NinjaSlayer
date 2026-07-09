using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerJackpotCardPoolPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_jackpot_card_pool";

    public static string Description =>
        "Keep Jackpot from generating NinjaSlayer token/status cards that are registered in the character card pool.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Jackpot), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)])];

    public static bool Prefix(Jackpot __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        if (__instance.Owner?.Character is not NinjaSlayerCharacter)
        {
            return true;
        }

        __result = PlayWithNormalCardPool(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task PlayWithNormalCardPool(Jackpot card, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
            .FromCard(card, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        List<CardModel> candidates = card.Owner.Character.CardPool
            .GetUnlockedCards(card.Owner.UnlockState, card.Owner.RunState.CardMultiplayerConstraint)
            .Where(IsNormalZeroCostCandidate)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        IEnumerable<CardModel> generatedCards = CardFactory.GetForCombat(
            card.Owner,
            candidates,
            card.DynamicVars.Cards.IntValue,
            card.Owner.RunState.Rng.CombatCardGeneration);

        foreach (CardModel generatedCard in generatedCards)
        {
            if (card.IsUpgraded)
            {
                CardCmd.Upgrade(generatedCard);
            }

            await CardPileCmd.AddGeneratedCardToCombat(generatedCard, PileType.Hand, card.Owner);
        }
    }

    private static bool IsNormalZeroCostCandidate(CardModel card)
    {
        CardEnergyCost energyCost = card.EnergyCost;
        if (energyCost == null || energyCost.Canonical != 0 || energyCost.CostsX)
        {
            return false;
        }

        if (!card.ShouldShowInCardLibrary || card.Type == CardType.Status)
        {
            return false;
        }

        return card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
    }
}
