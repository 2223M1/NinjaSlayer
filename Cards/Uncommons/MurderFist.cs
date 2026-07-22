using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class MurderFist : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(MurderFist), 2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy, true, "ComboFist");


    protected override bool ShouldGlowGoldInternal =>
        CombatState?.HittableEnemies.Any(IsAtOrBelowHalfHp) ?? false;

    // ponytail: reuse combo fist art until this card gets dedicated art.

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(14, ValueProp.Move)
    ];

    public MurderFist() : base(CardSpec) { }

    public override bool TryGetHitPreview(Creature? target, out int hitCount)
    {
        hitCount = target != null && IsAtOrBelowHalfHp(target) ? 2 : 1;
        return true;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        int hitCount = IsAtOrBelowHalfHp(cardPlay.Target) ? 2 : 1;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .WithHitCount(hitCount)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target)
            .ExecuteWithFinisher(choiceContext, this, cardPlay, hitCountOverride: hitCount);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
    }

    private static bool IsAtOrBelowHalfHp(Creature target) =>
        target.CurrentHp <= target.MaxHp / 2;
}
