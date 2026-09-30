using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class NarakusMight : NinjaSlayerUncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(5)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlame>()];

    public NarakusMight()
        : base(nameof(NarakusMight), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlame>(Owner, PileType.Hand);
    }

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(2);
}
