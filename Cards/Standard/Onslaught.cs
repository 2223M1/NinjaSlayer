using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Cards.Standard;

public sealed class Onslaught : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>(), HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()];

    public Onslaught() : base(nameof(Onslaught), 1, CardType.Power, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(1)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<OnslaughtPower>(choiceContext, Owner.Creature, DynamicVars["StrengthPower"].BaseValue, Owner.Creature, this);
    protected override void OnUpgrade() => DynamicVars["StrengthPower"].UpgradeValueBy(1);
}
