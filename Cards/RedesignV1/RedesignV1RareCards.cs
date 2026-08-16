using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Combat.SecondaryResources;

#pragma warning disable CA1725

namespace NinjaSlayer.Cards.RedesignV1;

public abstract class RedesignV1RareCard(string id, string art, int cost, CardType type, TargetType target)
    : NinjaSlayerRedesignCardTemplate(new NinjaSlayerCardSpec(id, cost, type, CardRarity.Rare, target, true), art);

public sealed class AlabamaDropRedesignV1 : RedesignV1RareCard
{
    public AlabamaDropRedesignV1() : base(nameof(AlabamaDropRedesignV1), nameof(BeatPeopleChado), 2, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ChadoBaseEnergyPower>(c, Owner.Creature, 1, Owner.Creature, this);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class KillingIntentRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Innate, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1), new DynamicVar("Tea", 1)];
    public KillingIntentRedesignV1() : base(nameof(KillingIntentRedesignV1), nameof(SipTea), 0, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await SecondaryResourceCmd.Gain(Owner, NinjaSlayerTeaEnergy.Id, IsUpgraded ? 2 : 1, source: this); await CardPileCmd.Draw(c, 1, Owner); }
    protected override void OnUpgrade() { }
}

public sealed class NarakuRecoveryRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public NarakuRecoveryRedesignV1() : base(nameof(NarakuRecoveryRedesignV1), nameof(ForgoStrength), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) { foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards.Where(x => x != this)) card.EnergyCost.SetThisTurnOrUntilPlayed(1, true); return Task.CompletedTask; }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class GreatUkeRedesignV1 : RedesignV1RareCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("PerCard", 3)];
    public GreatUkeRedesignV1() : base(nameof(GreatUkeRedesignV1), nameof(OpeningGuard), 2, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => CreatureCmd.GainBlock(Owner.Creature, DynamicVars["PerCard"].IntValue * PileType.Hand.GetPile(Owner).Cards.Count, ValueProp.Move, p);
    protected override void OnUpgrade() => DynamicVars["PerCard"].UpgradeValueBy(1);
}

public sealed class BangBangFistRedesignV1 : RedesignV1RareCard
{
    public BangBangFistRedesignV1() : base(nameof(BangBangFistRedesignV1), nameof(BackBridge), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        List<CardModel> cards = [.. PileType.Draw.GetPile(Owner).Cards, .. PileType.Discard.GetPile(Owner).Cards, .. PileType.Hand.GetPile(Owner).Cards.Where(x => x != this)];
        CardModel? card = (await CardSelectCmd.FromSimpleGrid(c, cards, Owner, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1))).FirstOrDefault();
        card?.AddKeyword(CardKeyword.Retain);
        if (card != null && card.Pile?.Type != PileType.Hand) await CardPileCmd.Add(card, PileType.Hand);
    }
    protected override void OnUpgrade() { }
}

public sealed class BeatPeopleChadoRedesignV1 : RedesignV1RareCard
{
    public BeatPeopleChadoRedesignV1() : base(nameof(BeatPeopleChadoRedesignV1), nameof(BackBridge), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<EndTurnRetainPower>(c, Owner.Creature, 1, Owner.Creature, this);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class BladesComeRedesignV1 : RedesignV1RareCard
{
    protected override bool HasEnergyCostX => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(4, ValueProp.Move), new DynamicVar("ThresholdDamage", 8)];
    public BladesComeRedesignV1() : base(nameof(BladesComeRedesignV1), nameof(HellTornado), 0, CardType.Attack, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        int x = ResolveEnergyXValue(); int damage = x >= 4 ? DynamicVars["ThresholdDamage"].IntValue : DynamicVars.Damage.IntValue;
        for (int i = 0; i < x; i++) await DamageCmd.Attack(damage).FromCard(this, p).TargetingAllOpponents(CombatState!).Execute(c);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(3); DynamicVars["ThresholdDamage"].UpgradeValueBy(3); }
}

public sealed partial class ClankDrinkTeaRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(30, ValueProp.Move)];
    public ClankDrinkTeaRedesignV1() : base(nameof(ClankDrinkTeaRedesignV1), nameof(KarateFinish), 4, CardType.Attack, TargetType.AnyEnemy) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, p).Targeting(p.Target!).Execute(c);
    private decimal ModifyDamageAdditiveCore(MegaCrit.Sts2.Core.Entities.Creatures.Creature? target, decimal amount, ValueProp props, MegaCrit.Sts2.Core.Entities.Creatures.Creature? dealer, CardModel? source)
        => source == this && dealer == Owner.Creature ? (Owner.Creature.GetPower<StrengthPower>()?.Amount ?? 0) * (IsUpgraded ? 7 : 3) : 0;
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(10);
}

public sealed class DrowsyBlackTeaRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(15, ValueProp.Move), new RepeatVar(4)];
    public DrowsyBlackTeaRedesignV1() : base(nameof(DrowsyBlackTeaRedesignV1), nameof(TornadoFist), 5, CardType.Attack, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { for (int i = 0; i < DynamicVars.Repeat.IntValue; i++) await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, p).Targeting(p.Target!).Execute(c); }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(5);
}

public sealed class FootworkRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(13, ValueProp.Move), new DynamicVar("Strength", 4)];
    public FootworkRedesignV1() : base(nameof(FootworkRedesignV1), nameof(AlabamaDrop), 2, CardType.Attack, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, p).Targeting(p.Target!).Execute(c); await PowerCmd.Apply<StrengthPower>(c, Owner.Creature, 4, Owner.Creature, this); await PowerCmd.Apply<VulnerablePower>(c, Owner.Creature, 1, Owner.Creature, this); }
    protected override void OnUpgrade() { }
}

public sealed class HellTornadoRedesignV1 : RedesignV1RareCard
{
    public HellTornadoRedesignV1() : base(nameof(HellTornadoRedesignV1), nameof(OmnidirectionalThrow), 4, CardType.Power, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await PowerCmd.Apply<ShurikenStockPower>(c, Owner.Creature, 4, Owner.Creature, this); await PowerCmd.Apply<ShurikenDamagePower>(c, Owner.Creature, 3, Owner.Creature, this); }
    protected override void OnUpgrade() { }
}

public sealed class InjectionRedesignV1 : RedesignV1RareCard
{
    public InjectionRedesignV1() : base(nameof(InjectionRedesignV1), nameof(OmnidirectionalThrow), 1, CardType.Power, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { int stock = IsUpgraded ? 5 : 4; await PowerCmd.Apply<ShurikenStockPower>(c, Owner.Creature, stock, Owner.Creature, this); await PowerCmd.Apply<ShurikenRegenerationPower>(c, Owner.Creature, IsUpgraded ? 4 : 3, Owner.Creature, this); }
    protected override void OnUpgrade() { }
}

public sealed class KarateFinishRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(4), new DynamicVar("NextDraw", 2)];
    public KarateFinishRedesignV1() : base(nameof(KarateFinishRedesignV1), nameof(ZazenDrink), 0, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await CardPileCmd.ShuffleIfNecessary(c, Owner); await ScryCmd.Execute(c, Owner, DynamicVars.Cards.IntValue); await PowerCmd.Apply<DrawCardsNextTurnPower>(c, Owner.Creature, DynamicVars["NextDraw"].IntValue, Owner.Creature, this); }
    protected override void OnUpgrade() { DynamicVars.Cards.UpgradeValueBy(2); DynamicVars["NextDraw"].UpgradeValueBy(1); }
}

public sealed class KarateRollingStoneRedesignV1 : RedesignV1RareCard
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(25, ValueProp.Move)];
    public KarateRollingStoneRedesignV1() : base(nameof(KarateRollingStoneRedesignV1), nameof(KillingIntent), 4, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p); await PowerCmd.Apply<RetaliatoryIntentPower>(c, Owner.Creature, 1, Owner.Creature, this); }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(10);
}

public sealed class KarateWallRedesignV1 : RedesignV1RareCard
{
    public override bool GainsBlock => true;

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Life", 10),
        new BlockVar(10, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromPower<NarakuLifePower>(),
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
    ];

    public KarateWallRedesignV1()
        : base(nameof(KarateWallRedesignV1), nameof(NarakuRecovery), 2, CardType.Skill, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await NinjaSlayerActions.EnterNaraku(choiceContext, Owner, DynamicVars["Life"].BaseValue);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["Life"].UpgradeValueBy(2);
        DynamicVars.Block.UpgradeValueBy(2);
    }
}

public sealed class OmnidirectionalThrowRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public OmnidirectionalThrowRedesignV1() : base(nameof(OmnidirectionalThrowRedesignV1), nameof(GreatUke), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<NullifyHitsPower>(c, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class RecycleRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Threshold", 30)];
    public RecycleRedesignV1() : base(nameof(RecycleRedesignV1), nameof(AssassinationFist), 1, CardType.Skill, TargetType.AnyEnemy) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => p.Target!.CurrentHp < DynamicVars["Threshold"].IntValue ? CreatureCmd.Kill(p.Target, true) : Task.CompletedTask;
    protected override void OnUpgrade() => DynamicVars["Threshold"].UpgradeValueBy(10);
}

public sealed class RedBlackFlameRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Innate, CardKeyword.Ethereal, CardKeyword.Exhaust];
    public RedBlackFlameRedesignV1() : base(nameof(RedBlackFlameRedesignV1), nameof(StunStrike), 3, CardType.Skill, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.Stun(p.Target!);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards)
        {
            card.GiveSingleTurnRetain();
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class RedoubleRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(3), new CardsVar(2), new DynamicVar("HpLoss", 3)];
    public RedoubleRedesignV1() : base(nameof(RedoubleRedesignV1), nameof(BloodTears), 0, CardType.Power, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await GameCompatibility.Damage.Deal(
            c,
            [Owner.Creature],
            DynamicVars["HpLoss"].BaseValue,
            ValueProp.Unblockable | ValueProp.Unpowered,
            Owner.Creature,
            this,
            p);
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
        await CardPileCmd.Draw(c, DynamicVars.Cards.IntValue, Owner);
        await PowerCmd.Apply<BloodTearsRedesignPower>(
            c,
            Owner.Creature,
            DynamicVars["HpLoss"].BaseValue,
            Owner.Creature,
            this);
    }
    protected override void OnUpgrade() { }
}

public sealed class SenchaStormRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public SenchaStormRedesignV1() : base(nameof(SenchaStormRedesignV1), nameof(Redouble), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        CardModel? card = (await CardSelectCmd.FromHand(c, Owner, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), x => x != this && x.Type != CardType.Power, this)).FirstOrDefault();
        if (card == null) return;
        RedesignRepeatState.Add(card);
        int cost = card.EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.Local);
        if (cost < 1) card.EnergyCost.SetThisTurnOrUntilPlayed(1);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
