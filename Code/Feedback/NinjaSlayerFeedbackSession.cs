namespace NinjaSlayer.Code.Feedback;

public static class NinjaSlayerFeedbackSession
{
    public static bool IsActive { get; private set; }

    public static bool IsConfirmed { get; private set; }

    public static void Begin()
    {
        IsActive = true;
        IsConfirmed = false;
    }

    public static void Confirm() => IsConfirmed = true;

    public static void Reset()
    {
        IsActive = false;
        IsConfirmed = false;
    }
}
