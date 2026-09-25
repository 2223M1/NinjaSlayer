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
    // Source-linked patch installation tests have no presentation session.
    // The candidate DLL's actual registry is exercised by product contracts.
    internal static class FinisherSessionRegistry
    {
        internal sealed record Session(MegaCrit.Sts2.Core.Entities.Cards.CardPlay? EventVisualPlay);
        internal static Session? GetActiveSession() => null;
    }

    internal static class NinjaSlayerRapidAnimationCoordinator
    {
        public static void EnsureLifecycle(Creature creature)
        {
        }

        public static void CardGameplaySettled(Creature creature)
        {
        }

        public static void BeginDamageRecovery(Creature creature, float fastSeconds, float standardSeconds)
        {
        }

        public static void CancelAndRestore(Creature creature)
        {
        }
    }
}
