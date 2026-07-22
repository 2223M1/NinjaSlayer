using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Events;

public static class NinjaSlayerHook
{
    private static async Task Dispatch<T>(PlayerChoiceContext ctx, Player player, Func<T, Task> invoke)
        where T : class
    {
        ICombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        foreach (T model in combatState.IterateHookListeners().OfType<T>())
        {
            var abstractModel = (AbstractModel)(object)model;
            ctx.PushModel(abstractModel);
            try
            {
                await invoke(model);
            }
            finally
            {
                ctx.PopModel(abstractModel);
            }
        }
    }

    public static Task OnScryed(PlayerChoiceContext ctx, Player player, int viewedAmount, int discardedAmount)
    {
        return Dispatch<IOnScryed>(ctx, player, m => m.OnScryed(ctx, player, viewedAmount, discardedAmount));
    }
}
