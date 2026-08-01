using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Lifecycle;

internal static partial class CardPlayResolutionScope
{
    static partial void WriteWarning(string message) => Entry.Logger.Warn(message);
}
