using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class GrapplingHook : NinjaSlayerCommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<WeakPower>(1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromPower<StrengthPower>()];

    public GrapplingHook()
        : base(nameof(GrapplingHook), 1, CardType.Skill, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int strengthLoss = Owner.Creature.GetPowerAmount<KaratePower>();
        await HookRopeAnimation.Play(Owner.Creature, cardPlay.Target!, async () =>
        {
            if (strengthLoss > 0)
            {
                await PowerCmd.Apply<GrapplingHookStrengthDownPower>(
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
        });
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Weak.UpgradeValueBy(1);
    }
}
