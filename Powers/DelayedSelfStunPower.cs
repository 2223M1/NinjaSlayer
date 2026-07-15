using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using Godot;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class DelayedSelfStunPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        ShowStunnedVfx();
        await PowerCmd.Remove(this);
        PlayerCmd.EndTurn(player, canBackOut: false);
    }

    private void ShowStunnedVfx()
    {
        NStunnedVfx? vfx = NStunnedVfx.Create(Owner);
        Node? container = Owner.GetVfxContainer();
        if (vfx is null || container is null)
        {
            return;
        }

        Callable.From(() => container.AddChildSafely(vfx)).CallDeferred();
    }
}
