using MegaCrit.Sts2.Core.Entities.Players;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

public abstract class NinjaSlayerSingletonTemplate : HookedSingletonModel
{
    protected NinjaSlayerSingletonTemplate(HookType hookType) : base(hookType)
    {
    }

    protected static bool IsNinjaSlayer(Player? player) => player?.Character is INinjaSlayerCharacter;
}

public abstract class NinjaSlayerCombatSingletonTemplate : NinjaSlayerSingletonTemplate
{
    protected NinjaSlayerCombatSingletonTemplate() : base(HookType.Combat)
    {
    }
}
