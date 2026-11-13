using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
#if NINJASLAYER_CHANNEL_STABLE
using StretchPower = NinjaSlayer.Powers.StretchPower;
#else
using StretchPower = MegaCrit.Sts2.Core.Models.Powers.StretchPower;
#endif

namespace NinjaSlayer.Cards.Standard;

public sealed class Assess : NinjaSlayerUncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<StretchPower>(), HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];
    public Assess() : base(nameof(Assess), 1, CardType.Skill, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<StretchPower>(5), new DynamicVar("Scry", 4)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<StretchPower>(choiceContext, Owner.Creature,
            DynamicVars["StretchPower"].BaseValue, Owner.Creature, this);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars["Scry"].IntValue);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["StretchPower"].UpgradeValueBy(2);
        DynamicVars["Scry"].UpgradeValueBy(1);
    }
}
