namespace NinjaSlayer.Code.Combat;

internal enum SawatariEventPhase
{
    FirstCombat,
    Intermission,
    DuelTransition,
    Duel,
    DuelResult,
    Finalizing
}

internal static class SawatariEventRules
{
    public const int BaseHp = 60;
    public const int ToughHp = 66;
    public const int AttackDamage = 2;
    public const int AttackHits = 4;
    public const int DuelStrength = 2;
    public const float SingleEnemyY = 200f;

    public static float ResolveSingleEnemyX(float boundsWidth, float encounterScaling) =>
        Math.Max((960f / encounterScaling - boundsWidth) * 0.5f, 150f)
        + boundsWidth * 0.5f;

    public static float ResolveMonsterReplacementChance(
        float eventOdds,
        int validEventCount,
        float monsterOdds)
    {
        if (eventOdds <= 0f || validEventCount <= 0 || monsterOdds <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(eventOdds / validEventCount / monsterOdds, 0f, 1f);
    }

    public static bool ShouldBeginIntermission(
        bool defeatedCreatureIsEnemy,
        bool hasOtherLivingEnemy) =>
        defeatedCreatureIsEnemy && !hasOtherLivingEnemy;
}

internal sealed class SawatariEventPhaseGate
{
    private readonly object _sync = new();

    public SawatariEventPhase Current { get; private set; } = SawatariEventPhase.FirstCombat;

    public bool TryMove(SawatariEventPhase from, SawatariEventPhase to)
    {
        lock (_sync)
        {
            if (Current != from)
            {
                return false;
            }

            Current = to;
            return true;
        }
    }

    public void FinalizeEvent()
    {
        lock (_sync)
        {
            Current = SawatariEventPhase.Finalizing;
        }
    }
}
