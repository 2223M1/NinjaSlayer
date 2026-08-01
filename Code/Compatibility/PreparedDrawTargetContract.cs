using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace NinjaSlayer.Code.Compatibility;

internal static class PreparedDrawTargetContract
{
    public static bool TryValidate(
        out MethodInfo? target,
        out PreparedDrawTargetFingerprint fingerprint,
        out string reason)
    {
        Type[] signature = [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)];
        target = AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.Draw),
            signature);
        MethodInfo? drawInternal = AccessTools.Method(
            typeof(CardPileCmd),
            "DrawInternal",
            signature);
        if (!MethodBodyFingerprintCapture.TryCapture(
                target,
                out MethodBodyFingerprint publicMethod,
                out reason))
        {
            fingerprint = default;
            return false;
        }

        if (!GameHostContractProfile.TryResolve(publicMethod, out GameHostContractProfile profile))
        {
            fingerprint = default;
            reason = $"Unsupported CardPileCmd.Draw host ({publicMethod}).";
            return false;
        }
        if (!StableMethodBodyContract.Matches(
                publicMethod,
                profile,
                profile.PreparedDraw.PublicMethod))
        {
            fingerprint = default;
            reason = $"CardPileCmd.Draw fingerprint mismatch for {profile.Id} ({publicMethod}).";
            return false;
        }

        MethodInfo implementation;
        MethodBodyFingerprint? internalMethod = null;
        if (profile.PreparedDraw.Layout == PreparedDrawHostLayout.DirectAsync)
        {
            if (drawInternal is not null || profile.PreparedDraw.InternalMethod is not null)
            {
                fingerprint = default;
                reason = $"CardPileCmd.Draw layout mismatch for {profile.Id}: expected direct async Draw.";
                return false;
            }

            implementation = target!;
        }
        else
        {
            if (profile.PreparedDraw.InternalMethod is not { } expectedInternal
                || !MethodBodyFingerprintCapture.TryCapture(
                    drawInternal,
                    out MethodBodyFingerprint capturedInternal,
                    out reason))
            {
                fingerprint = default;
                return false;
            }
            if (!StableMethodBodyContract.Matches(
                    capturedInternal,
                    profile,
                    expectedInternal))
            {
                fingerprint = default;
                reason = $"CardPileCmd.DrawInternal fingerprint mismatch for {profile.Id} ({capturedInternal}).";
                return false;
            }

            implementation = drawInternal!;
            internalMethod = capturedInternal;
        }

        if (!MethodBodyFingerprintCapture.TryCaptureAsyncMoveNext(
                implementation,
                out MethodBodyFingerprint moveNext,
                out reason))
        {
            fingerprint = default;
            return false;
        }
        if (!StableMethodBodyContract.Matches(
                moveNext,
                profile,
                profile.PreparedDraw.AsyncMoveNext))
        {
            fingerprint = default;
            reason = $"CardPileCmd draw MoveNext fingerprint mismatch for {profile.Id} ({moveNext}).";
            return false;
        }

        fingerprint = new PreparedDrawTargetFingerprint(
            profile.Id,
            profile.PreparedDraw.Layout,
            publicMethod,
            internalMethod,
            moveNext);
        reason = string.Empty;
        return true;
    }
}

internal readonly record struct PreparedDrawTargetFingerprint(
    string HostProfile,
    PreparedDrawHostLayout Layout,
    MethodBodyFingerprint PublicMethod,
    MethodBodyFingerprint? InternalMethod,
    MethodBodyFingerprint AsyncMoveNext)
{
    public override string ToString() =>
        $"host={HostProfile}, layout={Layout}, public=[{PublicMethod}], "
        + $"internal=[{InternalMethod?.ToString() ?? "none"}], moveNext=[{AsyncMoveNext}]";
}
