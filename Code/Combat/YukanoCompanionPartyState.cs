using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;

namespace NinjaSlayer.Code.Combat;

internal static class YukanoCompanionPartyState
{
    public static int GetActiveRelicCount(IRunState runState) =>
        EnumerateRelics(runState).Count(IsActive);

    public static bool IsController(YukanoCompanionRelic relic) =>
        ReferenceEquals(EnumerateRelics(relic.Owner.RunState).FirstOrDefault(IsActive), relic);

    public static Creature? FindCompanion(IRunState runState, bool livingOnly)
    {
        foreach (var player in runState.Players)
        {
            Creature? companion = player.PlayerCombatState?.Pets.FirstOrDefault(pet =>
                pet.Monster is YukanoMonster && (!livingOnly || pet.IsAlive));
            if (companion != null)
            {
                return companion;
            }
        }

        return null;
    }

    private static IEnumerable<YukanoCompanionRelic> EnumerateRelics(IRunState runState) =>
        runState.Players.SelectMany(player => player.Relics.OfType<YukanoCompanionRelic>());

    private static bool IsActive(YukanoCompanionRelic relic) =>
        relic.CombatsLeft > 0 && !relic.IsMelted;
}
