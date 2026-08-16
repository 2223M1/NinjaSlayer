using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Combat.SecondaryResources;

#pragma warning disable CA1725

namespace NinjaSlayer.Cards.RedesignV1;

public abstract class RedesignV1UncommonCard(string id, string art, int cost, CardType type, TargetType target)
    : NinjaSlayerRedesignCardTemplate(new NinjaSlayerCardSpec(id, cost, type, CardRarity.Uncommon, target, true), art);

public sealed class IronShirtRedesignV1 : RedesignV1UncommonCard
{
    public IronShirtRedesignV1() : base(nameof(IronShirtRedesignV1), nameof(DrinkTea), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ChadoHealPower>(c, Owner.Creature, IsUpgraded ? 3 : 2, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class SweepKickRedesignV1 : RedesignV1UncommonCard
{
    public SweepKickRedesignV1() : base(nameof(SweepKickRedesignV1), nameof(ShieldFromNothing), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ChadoBlockPower>(c, Owner.Creature, IsUpgraded ? 7 : 5, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class MurderFistRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public MurderFistRedesignV1() : base(nameof(MurderFistRedesignV1), nameof(SipTea), 1, CardType.Skill, TargetType.Self) => this.SecondaryCosts().Set(NinjaSlayerTeaEnergy.Id, 1);
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => CreatureCmd.Heal(Owner.Creature, IsUpgraded ? 3 : 2);
    protected override void OnUpgrade() { }
}

public sealed class LockOnRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    public LockOnRedesignV1() : base(nameof(LockOnRedesignV1), nameof(Meditation), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ChadoEnergyPower>(c, Owner.Creature, 2, Owner.Creature, this);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class IBlockRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3)];
    public IBlockRedesignV1() : base(nameof(IBlockRedesignV1), nameof(SipTea), 0, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await ScryCmd.Execute(c, Owner, DynamicVars.Cards.IntValue); await SecondaryResourceCmd.Gain(Owner, NinjaSlayerTeaEnergy.Id, 1, source: this); }
    protected override void OnUpgrade() { }
}

public sealed class TornadoFistRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];
    public TornadoFistRedesignV1() : base(nameof(TornadoFistRedesignV1), nameof(Recycle), 0, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, DynamicVars.Cards.IntValue);
        foreach (CardModel card in await CardSelectCmd.FromCombatPile(c, PileType.Discard.GetPile(Owner), Owner, prefs)) { await CardPileCmd.Add(card, PileType.Hand); card.AddKeyword(CardKeyword.Retain); }
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}

public sealed class AssassinationFistRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(6), new DynamicVar("Draw", 2)];
    public AssassinationFistRedesignV1() : base(nameof(AssassinationFistRedesignV1), nameof(ColdBrew), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await ScryCmd.Execute(c, Owner, DynamicVars.Cards.IntValue); await CardPileCmd.Draw(c, DynamicVars["Draw"].IntValue, Owner); }
    protected override void OnUpgrade() { }
}

public sealed class NinjaGreetingRedesignV1 : RedesignV1UncommonCard, IReturnToHandAfterPlay
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3), new DynamicVar("Draw", 1)];
    public NinjaGreetingRedesignV1() : base(nameof(NinjaGreetingRedesignV1), nameof(ColdBrew), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await ScryCmd.Execute(c, Owner, DynamicVars.Cards.IntValue); await CardPileCmd.Draw(c, DynamicVars["Draw"].IntValue, Owner); }
    protected override void OnUpgrade() { }
}

public sealed class BloodTearsRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3), new DynamicVar("NextDraw", 2)];
    public BloodTearsRedesignV1() : base(nameof(BloodTearsRedesignV1), nameof(ReadyBlade), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await ScryCmd.Execute(c, Owner, DynamicVars.Cards.IntValue);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards)
        {
            card.GiveSingleTurnRetain();
        }
        await PowerCmd.Apply<DrawCardsNextTurnPower>(c, Owner.Creature, DynamicVars["NextDraw"].IntValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Cards.UpgradeValueBy(1); DynamicVars["NextDraw"].UpgradeValueBy(1); }
}

public sealed class BackBridgeRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    public BackBridgeRedesignV1() : base(nameof(BackBridgeRedesignV1), nameof(ColdBrew), 1, CardType.Power, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await ScryCmd.Execute(c, Owner, DynamicVars.Cards.IntValue); await PowerCmd.Apply<ScryDrawPower>(c, Owner.Creature, 1, Owner.Creature, this); }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(2);
}

public sealed class BladeDanceRedesignV1 : RedesignV1UncommonCard
{
    public BladeDanceRedesignV1() : base(nameof(BladeDanceRedesignV1), nameof(Evolution), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ScryBlockPower>(c, Owner.Creature, IsUpgraded ? 4 : 3, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class BrewTeaRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(7, ValueProp.Move), new DynamicVar("Strength", 1)];
    public BrewTeaRedesignV1() : base(nameof(BrewTeaRedesignV1), nameof(IronShirt), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p); await PowerCmd.Apply<StrengthPower>(c, Owner.Creature, 1, Owner.Creature, this); }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(1);
}

public sealed class ColdBrewRedesignV1 : RedesignV1UncommonCard
{
    public ColdBrewRedesignV1() : base(nameof(ColdBrewRedesignV1), nameof(Momentum), 0, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<NextAttackStrengthPower>(c, Owner.Creature, IsUpgraded ? 5 : 3, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class ContraptionRedesignV1 : RedesignV1UncommonCard
{
    public ContraptionRedesignV1() : base(nameof(ContraptionRedesignV1), nameof(Momentum), 2, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<PerCardStrengthPower>(c, Owner.Creature, 1, Owner.Creature, this);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class DrinkTeaRedesignV1 : RedesignV1UncommonCard
{
    public DrinkTeaRedesignV1() : base(nameof(DrinkTeaRedesignV1), nameof(ForgoStrength), 0, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<CarriedStrengthPower>(c, Owner.Creature, IsUpgraded ? 4 : 2, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class EvadeRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(14, ValueProp.Move), new DynamicVar("Strength", 2)];
    public EvadeRedesignV1() : base(nameof(EvadeRedesignV1), nameof(SweepKick), 2, CardType.Attack, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, p).TargetingAllOpponents(CombatState!).Execute(c); await PowerCmd.Apply<StrengthPower>(c, Owner.Creature, 2, Owner.Creature, this); }
    protected override void OnUpgrade() { }
}

public sealed class EvolutionRedesignV1 : RedesignV1UncommonCard
{
    public EvolutionRedesignV1() : base(nameof(EvolutionRedesignV1), nameof(ShieldFromNothing), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ShuffleBlockPower>(c, Owner.Creature, IsUpgraded ? 12 : 8, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class ForgoStrengthRedesignV1 : RedesignV1UncommonCard, IReturnToHandAfterPlay
{
    public ForgoStrengthRedesignV1() : base(nameof(ForgoStrengthRedesignV1), nameof(ShurikenStock), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<ShurikenStockPower>(c, Owner.Creature, IsUpgraded ? 4 : 3, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class HalfMoonCompassKickRedesignV1 : RedesignV1UncommonCard
{
    public HalfMoonCompassKickRedesignV1() : base(nameof(HalfMoonCompassKickRedesignV1), nameof(ShurikenStock), 2, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        int count = CombatManager.Instance.History.CardPlaysStarted.Count(e =>
            e.HappenedThisTurn(CombatState!) && e.CardPlay.Card.Owner == Owner) - 1;
        return PowerCmd.Apply<ShurikenStockPower>(c, Owner.Creature, Math.Max(0, count), Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class ImpureFlameRedesignV1 : RedesignV1UncommonCard, IReturnToHandAfterPlay
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(4, ValueProp.Move)];
    public ImpureFlameRedesignV1() : base(nameof(ImpureFlameRedesignV1), nameof(OpeningGuard), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p); await PowerCmd.Apply<OneTurnBarricadePower>(c, Owner.Creature, 1, Owner.Creature, this); }
    protected override void OnUpgrade() { }
}

public sealed class IyaIronSlashWaveRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6, ValueProp.Move)];
    public IyaIronSlashWaveRedesignV1() : base(nameof(IyaIronSlashWaveRedesignV1), nameof(OpeningGuard), 2, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PowerCmd.Apply<ThreeTurnBlockPower>(c, Owner.Creature, DynamicVars.Block.IntValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class MasochisticBlissRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(14, ValueProp.Move)];
    public MasochisticBlissRedesignV1() : base(nameof(MasochisticBlissRedesignV1), nameof(MurderFist), 2, CardType.Attack, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        int hits = p.Target!.CurrentHp * 2 <= p.Target.MaxHp ? 2 : 1;
        for (int i = 0; i < hits; i++) await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, p).Targeting(p.Target).Execute(c);
    }
    protected override void OnUpgrade() { }
}

public sealed class MomentumRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public MomentumRedesignV1() : base(nameof(MomentumRedesignV1), nameof(LockOn), 0, CardType.Skill, TargetType.AnyEnemy) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        DamageFocusPower? power = p.Target!.GetPower<DamageFocusPower>();
        return ApplyFocus(c, p.Target!, power);
    }
    private async Task ApplyFocus(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Entities.Creatures.Creature target, DamageFocusPower? power)
    {
        power ??= await PowerCmd.Apply<DamageFocusPower>(choiceContext, target, 1, Owner.Creature, this);
        if (power != null) power.DamageMultiplier = IsUpgraded ? 1.5m : 1.25m;
    }
    protected override void OnUpgrade() { }
}

public sealed class OpeningGuardRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10, ValueProp.Move)];
    public OpeningGuardRedesignV1() : base(nameof(OpeningGuardRedesignV1), nameof(IBlock), 2, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p); await PowerCmd.Apply<IBlockPower>(c, Owner.Creature, 1, Owner.Creature, this); }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class PourTeaRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(14, ValueProp.Move), new CardsVar(1)];
    public PourTeaRedesignV1() : base(nameof(PourTeaRedesignV1), nameof(OpeningGuard), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p) { await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p); await CardPileCmd.Draw(c, 1, Owner); }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5);
}

public sealed class PursuitStrikeRedesignV1 : RedesignV1UncommonCard, IReturnToHandAfterPlay
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3)];
    public PursuitStrikeRedesignV1() : base(nameof(PursuitStrikeRedesignV1), nameof(ReadyBlade), 1, CardType.Skill, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => CardPileCmd.Draw(c, DynamicVars.Cards.IntValue, Owner);
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}

public sealed class ReadyBladeRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];
    public ReadyBladeRedesignV1() : base(nameof(ReadyBladeRedesignV1), nameof(ForgoStrength), 0, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PlayerCmd.GainEnergy(DynamicVars.Energy.IntValue, Owner);
        CardModel? card = (await CardSelectCmd.FromHandForDiscard(c, Owner, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), x => x != this, this)).FirstOrDefault();
        if (card != null) await CardCmd.Discard(c, card);
    }
    protected override void OnUpgrade() => DynamicVars.Energy.UpgradeValueBy(1);
}

public sealed class RiffleRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];
    public RiffleRedesignV1() : base(nameof(RiffleRedesignV1), nameof(BackBridge), 0, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        var selected = await CardSelectCmd.FromHand(c, Owner, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 2), x => x != this, this);
        foreach (CardModel card in selected.Reverse()) await CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Top);
        await PlayerCmd.GainEnergy(DynamicVars.Energy.IntValue, Owner);
    }
    protected override void OnUpgrade() => DynamicVars.Energy.UpgradeValueBy(1);
}

public sealed class RubHandsRedesignV1 : RedesignV1UncommonCard
{
    public RubHandsRedesignV1() : base(nameof(RubHandsRedesignV1), nameof(NinjaGreeting), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<EveryThirdAttackPower>(c, Owner.Creature, IsUpgraded ? 3 : 2, Owner.Creature, this);
    protected override void OnUpgrade() { }
}

public sealed class ShieldFromNothingRedesignV1 : RedesignV1UncommonCard
{
    public ShieldFromNothingRedesignV1() : base(nameof(ShieldFromNothingRedesignV1), nameof(ShieldFromNothing), 1, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext c, CardPlay p) => PowerCmd.Apply<IntentOpeningPower>(c, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, this);
    protected override void OnUpgrade() { }
}
