using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Enchantments;

[RegisterEnchantment]
public sealed class NourishingEnchantment : ModEnchantmentTemplate
{
    public override EnchantmentAssetProfile AssetProfile => new(
        IconPath: "res://NinjaSlayer/images/relics/MaguroSushiRelic.png");

    public override bool HasExtraCardText => true;
    public override bool ShowAmount => true;

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay? cardPlay)
    {
        if (Status != EnchantmentStatus.Normal)
        {
            return;
        }

        Status = EnchantmentStatus.Disabled;
        await CreatureCmd.GainMaxHp(Card.Owner.Creature, Amount);
    }
}
