using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Ancients;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

public static class NinjaSlayerContentAccess
{
    public static bool HasNinjaSlayer(IRunState runState) =>
        runState.Players.Any(player => player.Character is INinjaSlayerCharacter);
}

[RegisterSingleton]
public sealed class NinjaSlayerContentAccessRules : NinjaSlayerSingletonTemplate
{
    public NinjaSlayerContentAccessRules() : base(HookType.Run)
    {
    }

    public override bool ShouldAllowAncient(Player player, AncientEventModel ancient) =>
        ancient is not NancyLee || NinjaSlayerContentAccess.HasNinjaSlayer(player.RunState);
}
