using MegaCrit.Sts2.Core.HoverTips;
using STS2RitsuLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(TokenCardPool))]
public sealed class StrongShurikenTokenRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.FocusPower>()];

    private int _snapshotDamage = -1;

    internal static void RegisterSavedData(string modId) =>
        RitsuLibFramework.GetModelSavedDataStore(modId)
            .RegisterComputed<StrongShurikenTokenRedesignV1, Snapshot>("strong_shuriken_damage",
                card => new Snapshot { Damage = card.SnapshotDamage },
                (card, saved) => { if (saved is not null) card.SnapshotDamage = saved.Damage; },
                () => new Snapshot());

    internal sealed class Snapshot
    {
        public int Damage { get; set; } = -1;
    }

    public int SnapshotDamage
    {
        get => _snapshotDamage;
        set
        {
            AssertMutable();
            _snapshotDamage = value;
            if (value >= 0) DynamicVars.Damage.BaseValue = value + 4 * CurrentUpgradeLevel;
        }
    }

    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(StrongShurikenTokenRedesignV1),
        0,
        CardType.Attack,
        CardRarity.Token,
        TargetType.AnyEnemy,
        false,
        "GiantShurikenCard",
        [NinjaSlayerCardTags.Shuriken]);

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move)];

    public StrongShurikenTokenRedesignV1() : base(Spec) { }

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource
#if !NINJASLAYER_LEGACY_DAMAGE_API
        , CardPlay? cardPlay
#endif
    ) => SnapshotDamage < 0 && cardSource == this && props.IsPoweredAttack() ? Owner.Creature.GetPowerAmount<FocusPower>() : 0;

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        ShurikenCombat.BuildAttackCommand(this, cardPlay, DynamicVars.Damage)
            .Execute(choiceContext);

    protected override void AfterDowngraded()
    {
        if (SnapshotDamage >= 0) DynamicVars.Damage.BaseValue = SnapshotDamage;
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4);
}
