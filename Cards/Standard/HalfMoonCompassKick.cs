using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;

namespace NinjaSlayer.Cards.Standard;

public sealed class HalfMoonCompassKick : NinjaSlayerUncommonCard
{
    private int _extraHitsThisCombat;
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<Chado>().Any();
    protected override bool ShouldGlowGoldInternal => CombatState != null && IsPlayable;
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromCardWithCardHoverTips<Chado>();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(10, ValueProp.Move), new RepeatVar(1)];

    public HalfMoonCompassKick()
        : base(nameof(HalfMoonCompassKick), 0, CardType.Attack, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel? tea = (await CardSelectCmd.FromHand(choiceContext, Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card is Chado, this)).FirstOrDefault();
        if (tea == null) return;
        await CardCmd.Exhaust(choiceContext, tea);
        if (tea.Pile?.Type != PileType.Exhaust) return;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .WithHitCount(DynamicVars.Repeat.IntValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        _extraHitsThisCombat++;
        DynamicVars.Repeat.BaseValue++;
    }

    protected override void AfterDowngraded() => DynamicVars.Repeat.BaseValue += _extraHitsThisCombat;
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}
