using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Potions;

[RegisterPotion(typeof(NinjaSlayerPotionPool))]
public sealed class ZbrAmpoulePotion : ModPotionTemplate
{
    public override PotionRarity Rarity => PotionRarity.Uncommon;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.AnyPlayer;

    public override PotionAssetProfile AssetProfile => new(
        ImagePath: $"res://NinjaSlayer/images/potions/{GetType().Name}.png",
        OutlinePath: $"res://NinjaSlayer/images/potions/{GetType().Name}_outline.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<StrengthPower>(2),
        new DynamicVar("NarakuLife", 12)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<StrengthPower>()
    ];

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
    {
        AssertValidForTargetedPotion(target);
        NCombatRoom.Instance?.PlaySplashVfx(target, new Color("D32020FF"));
        await PowerCmd.Apply<StrengthPower>(choiceContext, target!, DynamicVars.Strength.BaseValue, Owner.Creature, null);
        if (target!.Player != null)
        {
            await NinjaSlayerActions.EnterNaraku(choiceContext, target.Player, DynamicVars["NarakuLife"].BaseValue);
        }
    }
}
