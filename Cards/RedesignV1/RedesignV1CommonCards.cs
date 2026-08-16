using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public abstract class RedesignV1CommonCard(
    string id,
    string art,
    int cost,
    CardType type,
    TargetType target) : NinjaSlayerRedesignCardTemplate(
        new NinjaSlayerCardSpec(id, cost, type, CardRarity.Common, target, true), art);

public sealed class RepeatSweepRedesignV1 : RedesignV1CommonCard, IReturnToHandAfterPlay
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(4, ValueProp.Move), new CardsVar(1)];
    public RepeatSweepRedesignV1() : base(nameof(RepeatSweepRedesignV1), nameof(TeaHitsPeople), 1, CardType.Skill, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}

public sealed class RetainGuardRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move)];
    public RetainGuardRedesignV1() : base(nameof(RetainGuardRedesignV1), nameof(DiscardDefense), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        CardModel? selected = (await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card != this,
            this)).FirstOrDefault();
        selected?.AddKeyword(CardKeyword.Retain);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class ObserverGuardRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(8, ValueProp.Move), new CardsVar(2)];
    public ObserverGuardRedesignV1() : base(nameof(ObserverGuardRedesignV1), nameof(LuckyStrike), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
    }
    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(2); DynamicVars.Cards.UpgradeValueBy(2); }
}

public sealed class BorrowedDexterityRedesignV1 : RedesignV1CommonCard
{
    public BorrowedDexterityRedesignV1() : base(nameof(BorrowedDexterityRedesignV1), nameof(Evade), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int strength = Owner.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
        return PowerCmd.Apply<BorrowedDexterityPower>(choiceContext, Owner.Creature, strength, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class ThrowKunaiRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(11, ValueProp.Move), new CardsVar(2)];
    public ThrowKunaiRedesignV1() : base(nameof(ThrowKunaiRedesignV1), nameof(ThrowKunai), 1, CardType.Attack, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
        CardModel? selected = (await CardSelectCmd.FromHandForDiscard(choiceContext, Owner, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, this)).FirstOrDefault();
        if (selected != null) await CardCmd.Discard(choiceContext, selected);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(5); DynamicVars.Cards.UpgradeValueBy(1); }
}

public sealed class ChopRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(5, ValueProp.Move), new KarateVar(3)];
    public ChopRedesignV1() : base(nameof(ChopRedesignV1), nameof(Chop), 0, CardType.Attack, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<KaratePower>(choiceContext, cardPlay.Target!, DynamicVars.Karate().BaseValue, Owner.Creature, this);
        await MoveChopToDrawTopForLegacyHost();
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2); DynamicVars.Karate().UpgradeValueBy(1); }
}

public sealed class RetainedForceRedesignV1 : RedesignV1CommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Strength", 8)];
    public RetainedForceRedesignV1() : base(nameof(RetainedForceRedesignV1), nameof(ForgoStrength), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => PowerCmd.Apply<RetainedForcePower>(choiceContext, Owner.Creature, DynamicVars["Strength"].BaseValue, Owner.Creature, this);
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

public sealed class ReflexGuardRedesignV1 : RedesignV1CommonCard, IReturnToHandAfterPlay
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(3, ValueProp.Move), new DynamicVar("Strength", 1)];
    public ReflexGuardRedesignV1() : base(nameof(ReflexGuardRedesignV1), nameof(IBlock), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, DynamicVars["Strength"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(2);
}

public sealed class ShurikenVolleyRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7, ValueProp.Move), new DynamicVar("Stock", 1)];
    public ShurikenVolleyRedesignV1() : base(nameof(ShurikenVolleyRedesignV1), nameof(ShurikenSpread), 1, CardType.Attack, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<ShurikenStockPower>(choiceContext, Owner.Creature, DynamicVars["Stock"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}

public sealed class FlowingGuardRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(4, ValueProp.Move), new DynamicVar("PerCardBlock", 2)];
    public FlowingGuardRedesignV1() : base(nameof(FlowingGuardRedesignV1), nameof(OpeningGuard), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<FlowingGuardPower>(choiceContext, Owner.Creature, DynamicVars["PerCardBlock"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(2); DynamicVars["PerCardBlock"].UpgradeValueBy(1); }
}
