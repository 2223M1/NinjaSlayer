using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using STS2RitsuLib;

namespace NinjaSlayer.Scripts
{
    internal static class Entry
    {
        public static readonly Logger Logger =
            RitsuLibFramework.CreateLogger("NinjaSlayer.RitsuLibContractTests");
    }
}

namespace NinjaSlayer.Code.ExternalAnimations
{
    internal static class NinjaSlayerRapidAnimationCoordinator
    {
        public static void EnsureLifecycle(Creature creature)
        {
        }

        public static void CardGameplaySettled(Creature creature)
        {
        }

        public static void CancelAndRestore(Creature creature)
        {
        }
    }
}
