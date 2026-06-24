using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Enchantments;

[RegisterEnchantment]
public sealed class BlackFlameEnchantment : ModEnchantmentTemplate
{
    public override EnchantmentAssetProfile AssetProfile => new(
        IconPath: $"res://NinjaSlayer/images/enchantments/{GetType().Name}.png"
    );

    public override bool HasExtraCardText => true;

    public override bool CanEnchantCardType(CardType cardType)
    {
        return cardType == CardType.Attack;
    }

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay? cardPlay)
    {
        if (cardPlay?.Target != null)
        {
            await PowerCmd.Apply<KaratePower>(choiceContext, cardPlay.Target, 3, Card.Owner.Creature, Card);
        }
    }
}
