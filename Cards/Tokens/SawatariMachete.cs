using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(TokenCardPool))]
public sealed class SawatariMachete : NinjaSlayerStandaloneCardTemplate
{
    public SawatariMachete() : base(new NinjaSlayerCardSpec(nameof(SawatariMachete),
        2, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy, false)) { }
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(12, ValueProp.Move)];

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card == this) PlayerMacheteVisuals.Refresh(Owner);
        return Task.CompletedTask;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .Targeting(cardPlay.Target!).WithNoAttackerAnim()
            .AfterAttackerAnim(() => PlayerMacheteVisuals.Throw(this, cardPlay.Target!))
            .WithHitFx(VfxCmd.slashPath, NinjaSlayerCombatVfx.MacheteHitSfx)
            .Execute(choiceContext);
        if (cardPlay.Target?.Monster is SawatariMonster { ActThree: true } sawatari)
            sawatari.ReturnMachete();
    }
    protected override void OnUpgrade() { }
}
