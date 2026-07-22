using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(KarateDamageMath.CumulativeDamage(5, 0) == 0, "Zero hits must deal zero Karate damage.");
Check(KarateDamageMath.CumulativeDamage(5, 2) == 9, "Karate must use descending arithmetic damage.");
Check(KarateDamageMath.CumulativeDamage(3, 10) == 6, "Karate triggers cannot exceed the stack count.");

var memo = new BoundedMemoSearch<string, bool>(2, TimeSpan.FromSeconds(1));
Check(memo.Lookup("state-a", out _) == MemoSearchLookup.NewState, "The first state must be visited.");
memo.Store("state-a", true);
Check(memo.Lookup("state-a", out bool cached) == MemoSearchLookup.Cached && cached,
    "An identical forecast state must use the memoized result.");
Check(memo.VisitedStates == 1, "A cache hit must not consume the state budget.");
Check(memo.Lookup("state-b", out _) == MemoSearchLookup.NewState, "The second state must fit the budget.");
Check(memo.Lookup("state-c", out _) == MemoSearchLookup.BudgetExceeded,
    "Forecasting must fail closed after the state budget is exhausted.");

Check(!XAttackAudioContext.SuppressAutomaticSfx, "X attack SFX must start unsuppressed.");
using (XAttackAudioContext.Suppress())
{
    Check(XAttackAudioContext.SuppressAutomaticSfx, "The outer suppression scope must be active.");
    using (XAttackAudioContext.Suppress())
    {
        Check(XAttackAudioContext.SuppressAutomaticSfx, "Nested suppression must remain active.");
    }
    Check(XAttackAudioContext.SuppressAutomaticSfx, "Disposing an inner scope must preserve the outer scope.");
    Check(await Task.Run(() => XAttackAudioContext.SuppressAutomaticSfx),
        "Suppression depth must flow across awaited work.");
}
Check(!XAttackAudioContext.SuppressAutomaticSfx, "The final scope must restore automatic SFX.");

Console.WriteLine("NinjaSlayer pure logic checks passed.");
