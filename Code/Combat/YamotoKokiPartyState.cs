using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;

namespace NinjaSlayer.Code.Combat;

internal static class YamotoKokiPartyState
{
    public static int GetActiveRelicCount(IRunState runState) =>
        EnumerateRelics(runState).Count(IsActive);

    public static YamotoKokiCuteRelic? GetController(IRunState runState) =>
        EnumerateRelics(runState).FirstOrDefault(IsActive);

    public static bool IsController(YamotoKokiCuteRelic relic) =>
        ReferenceEquals(GetController(relic.Owner.RunState), relic);

    public static bool HasPlayedEntrance(IRunState runState) =>
        EnumerateRelics(runState).Any(relic => relic.HasPlayedEntrance);

    public static bool HasPlayedFarewell(IRunState runState) =>
        EnumerateRelics(runState).Any(relic => relic.HasPlayedFarewell);

    public static Creature? FindLivingCompanion(IRunState runState)
    {
        foreach (var player in runState.Players)
        {
            Creature? companion = player.PlayerCombatState?.Pets.FirstOrDefault(
                pet => pet.Monster is YamotoKokiMonster && pet.IsAlive);
            if (companion != null)
            {
                return companion;
            }
        }

        return null;
    }

    private static IEnumerable<YamotoKokiCuteRelic> EnumerateRelics(IRunState runState) =>
        runState.Players.SelectMany(player => player.Relics.OfType<YamotoKokiCuteRelic>());

    private static bool IsActive(YamotoKokiCuteRelic relic) =>
        YamotoKokiRelicLifetimePolicy.IsActive(relic.CombatsLeft, relic.IsMelted);
}
