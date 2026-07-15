using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;

namespace NinjaSlayer.Powers;

public sealed class DelayedSelfStunPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        await PowerCmd.Remove(this);
        await PowerCmd.Apply<RingingPower>(
            choiceContext,
            Owner,
            1,
            Applier ?? Owner,
            null);
    }
}
