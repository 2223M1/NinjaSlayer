using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherLog
{
    public static void Error(string message)
    {
        try
        {
            Entry.Logger.Error(message);
        }
        catch
        {
        }
    }

    public static void Info(string message)
    {
        try
        {
            Entry.Logger.Info(message);
        }
        catch
        {
        }
    }

    public static void Warn(string message)
    {
        try
        {
            Entry.Logger.Warn(message);
        }
        catch
        {
        }
    }
}

internal static class FinisherPresentationSettings
{
    public const FinisherPresentationMode Mode = FinisherPresentationMode.Enhanced;
}
