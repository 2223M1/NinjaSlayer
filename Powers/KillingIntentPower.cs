using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Patches;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KillingIntentPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("KillingIntentPower");
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(47, ValueProp.Move)];

    private bool _receivedDamage;
    private Creature[] _attackers = [];

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext, Creature target, DamageResult result,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target == Owner && dealer != null && dealer.Side != Owner.Side && props.IsPoweredAttack()
            && (result.UnblockedDamage > 0 || NarakuLifeDamagePatch.AbsorbedBy(result) > 0))
            _receivedDamage = true;
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEndLate(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == Owner.Side)
        {
            if (participants.Contains(Owner))
                _attackers = CombatState.HittableEnemies
                    .Where(enemy => enemy.Monster?.IntendsToAttack == true).ToArray();
            return;
        }

        // Consume this turn's stacks before resolving them, including lethal retaliation.
        int responses = Amount;
        decimal damage = DynamicVars.Damage.BaseValue;
        await PowerCmd.Remove(this);
        if (_receivedDamage || !Owner.IsAlive || CombatManager.Instance.IsOverOrEnding) return;
        for (int i = 0; i < responses && Owner.IsAlive && !CombatManager.Instance.IsOverOrEnding; i++)
        {
            Creature[] targets = _attackers.Where(enemy => enemy.IsAlive && enemy.IsHittable
                && ReferenceEquals(enemy.CombatState, Owner.CombatState)).ToArray();
            if (targets.Length == 0) break;
            await KillingIntentCounter.Execute(choiceContext, Owner, targets, damage);
        }
    }
}
