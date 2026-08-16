using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class BorrowedDexterityPower : ModTemporaryAppliedPowerTemplate<BorrowedDexterityRedesignV1, DexterityPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(OpeningGuardPower));
}

[RegisterPower]
public sealed class RetainedForcePower : ModTemporaryAppliedPowerTemplate<RetainedForceRedesignV1, StrengthPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(EveryHitTemporaryStrengthTemporaryPower));
}

public sealed class FlowingGuardPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(OpeningGuardPower));

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature == Owner)
        {
            await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null);
        }
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}
