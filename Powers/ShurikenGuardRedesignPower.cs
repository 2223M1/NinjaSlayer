using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ShurikenGuardRedesignPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(IBlockPower));

    internal async Task AfterShurikenDamage(
        IReadOnlyCollection<DamageResult> results)
    {
        int block = results.Sum(result => result.TotalDamage + result.OverkillDamage) * Amount;
        if (block <= 0)
        {
            return;
        }

        Flash();
        await CreatureCmd.GainBlock(Owner, block, ValueProp.Move, null);
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}
