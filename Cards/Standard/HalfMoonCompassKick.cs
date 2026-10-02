using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.Standard;

public sealed class HalfMoonCompassKick : NinjaSlayerUncommonCard
{
    private decimal _extraDamageThisTurn;
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<Chado>().Any();
    protected override bool ShouldGlowGoldInternal => CombatState != null && IsPlayable;
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromCardWithCardHoverTips<Chado>();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(10, ValueProp.Move)];

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
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        _extraDamageThisTurn += DynamicVars.Damage.BaseValue;
        DynamicVars.Damage.BaseValue *= 2;
    }

#if NINJASLAYER_CHANNEL_STABLE
    protected override PileType GetResultPileTypeForCardPlay()
    {
        PileType pile = base.GetResultPileTypeForCardPlay();
        return pile == PileType.Discard ? PileType.Hand : pile;
    }
#else
    protected override CardLocation GetResultLocationForCardPlay()
    {
        CardLocation location = base.GetResultLocationForCardPlay();
        if (location.pileType == PileType.Discard) location.pileType = PileType.Hand;
        return location;
    }
#endif

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner.Creature))
        {
            DynamicVars.Damage.BaseValue -= _extraDamageThisTurn;
            _extraDamageThisTurn = 0;
        }
        return Task.CompletedTask;
    }

    protected override void AfterDowngraded() => DynamicVars.Damage.BaseValue += _extraDamageThisTurn;
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}
