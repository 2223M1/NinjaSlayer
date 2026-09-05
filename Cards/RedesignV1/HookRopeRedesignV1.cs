using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class HookRopeRedesignV1 : RedesignV1CommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<WeakPower>(1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromPower<StrengthPower>()];

    public HookRopeRedesignV1()
        : base(nameof(HookRopeRedesignV1), "NinjaWhip", 1, CardType.Skill, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int strengthLoss = Owner.Creature.GetPowerAmount<KaratePower>();
        if (strengthLoss > 0)
        {
            await PowerCmd.Apply<HookRopeStrengthDownPower>(
                choiceContext,
                cardPlay.Target!,
                strengthLoss,
                Owner.Creature,
                this);
        }
        await PowerCmd.Apply<WeakPower>(
            choiceContext,
            cardPlay.Target!,
            DynamicVars.Weak.BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Weak.UpgradeValueBy(1);
    }
}
