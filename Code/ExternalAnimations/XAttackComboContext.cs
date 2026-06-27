namespace NinjaSlayer.Code.ExternalAnimations;

public static class XAttackComboContext
{
    public static bool Active { get; private set; }

    public static int CurrentHitIndex { get; set; }

    public static int TotalHits { get; private set; }

    public static void Begin(int totalHits)
    {
        Active = true;
        TotalHits = totalHits;
        CurrentHitIndex = 0;
    }

    public static void End()
    {
        Active = false;
        TotalHits = 0;
        CurrentHitIndex = 0;
    }
}
