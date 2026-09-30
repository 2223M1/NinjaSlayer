using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Poise : NinjaSlayerRareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<Chado>(), HoverTipFactory.FromPower<KaratePower>()];

    public Poise()
        : base(nameof(Poise), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<PoisePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(1);
}
