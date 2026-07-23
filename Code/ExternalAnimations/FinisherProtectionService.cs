using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Patches;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherProtectionService
{
    internal static void TryProtectLethalDamage(
        Creature target,
        ref decimal amount,
        out FinisherProtectionToken? token)
    {
        token = null;
        if (NinjaSlayerPatchCapabilities.FinisherEnabled)
        {
            FinisherSessionRegistry.GetActiveSession()?.TryProtectLethalDamage(target, ref amount, out token);
        }
    }

    internal static void ConfirmProtectedDamageResult(
        DamageResult? result,
        bool originalRan,
        FinisherProtectionToken? token)
    {
        if (token == null)
        {
            return;
        }

        try
        {
            if (result != null)
            {
                token.Ledger.Confirm(token, result, originalRan);
            }
        }
        catch (Exception ex)
        {
            FinisherLog.Error($"Could not confirm NinjaSlayer finisher lethal protection: {ex}");
        }
        finally
        {
            if (token.IsConfirmed
                && FinisherSessionRegistry.GetActiveSession() is { } session
                && session.SessionId == token.SessionId
                && session.CombatEpoch == token.CombatEpoch)
            {
                session.NotifyProtectedDamageConfirmed();
            }
        }
    }

    internal static void FinalizeLethalProtection(FinisherProtectionToken? token)
    {
        token?.Ledger.FinalizeProtection(token);
    }

    internal static bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage)
    {
        if (FinisherSessionRegistry.GetActiveSession() is { } session)
        {
            return session.TryTakeDamageDisplayOverride(result, out displayDamage);
        }

        displayDamage = 0;
        return false;
    }
}
