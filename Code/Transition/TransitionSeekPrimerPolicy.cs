namespace NinjaSlayer.Code.Transition;

internal static class TransitionSeekPrimerPolicy
{
    public static bool CanEnableFrameCorrection(TimeSpan validatedSeek) =>
        validatedSeek >= TimeSpan.Zero
        && validatedSeek.TotalSeconds <= TransitionFrameDropClock.FrameDurationSeconds;
}
