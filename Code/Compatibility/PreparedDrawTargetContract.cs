using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace NinjaSlayer.Code.Compatibility;

internal static class PreparedDrawTargetContract
{
    public const string ExpectedAssemblyVersion = "0.1.0.0";
    public const string ExpectedModuleMvid = "a49d3537-5a42-4dcd-9877-663e394f2b44";
    public const int ExpectedWrapperMetadataToken = 0x060087F4;
    public const string ExpectedWrapperIlSha256 = "a73e4a4eadb503a8fe18e35e0115581208d946c40c225e4aef0beb3a7a3f529f";
    public const int ExpectedInternalMetadataToken = 0x060087F6;
    public const string ExpectedInternalIlSha256 = "23ae995c9d6825f8ea24e7febce122641f2660fc3bea0667cf57c3d34b80c857";

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
        if (!PreparedMethodContract.TryCapture(target, out PreparedMethodFingerprint wrapper, out reason))
        {
            fingerprint = default;
            return false;
        }
        if (!PreparedMethodContract.TryCapture(drawInternal, out PreparedMethodFingerprint inner, out reason))
        {
            fingerprint = default;
            return false;
        }

        fingerprint = new PreparedDrawTargetFingerprint(wrapper, inner);
        if (!PreparedMethodContract.Matches(
                wrapper,
                ExpectedAssemblyVersion,
                ExpectedModuleMvid,
                ExpectedWrapperMetadataToken,
                ExpectedWrapperIlSha256))
        {
            reason = $"CardPileCmd.Draw wrapper fingerprint mismatch ({wrapper}).";
            return false;
        }
        if (!PreparedMethodContract.Matches(
                inner,
                ExpectedAssemblyVersion,
                ExpectedModuleMvid,
                ExpectedInternalMetadataToken,
                ExpectedInternalIlSha256))
        {
            reason = $"CardPileCmd.DrawInternal fingerprint mismatch ({inner}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

internal readonly record struct PreparedDrawTargetFingerprint(
    PreparedMethodFingerprint Wrapper,
    PreparedMethodFingerprint Internal)
{
    public override string ToString() => $"wrapper=[{Wrapper}], internal=[{Internal}]";
}

internal readonly record struct PreparedMethodFingerprint(
    string AssemblyVersion,
    Guid ModuleMvid,
    int MetadataToken,
    string IlSha256)
{
    public override string ToString() =>
        $"assembly={AssemblyVersion}, mvid={ModuleMvid:D}, token=0x{MetadataToken:X8}, il={IlSha256}";
}

internal static class PreparedMethodContract
{
    public static bool TryCapture(
        MethodInfo? method,
        out PreparedMethodFingerprint fingerprint,
        out string reason)
    {
        if (method?.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            fingerprint = default;
            reason = $"{method?.DeclaringType?.FullName ?? "Unknown"}.{method?.Name ?? "Unknown"} IL is unavailable.";
            return false;
        }

        fingerprint = new PreparedMethodFingerprint(
            method.Module.Assembly.GetName().Version?.ToString() ?? "unknown",
            method.Module.ModuleVersionId,
            method.MetadataToken,
            Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant());
        reason = string.Empty;
        return true;
    }

    public static bool Matches(
        PreparedMethodFingerprint fingerprint,
        string assemblyVersion,
        string moduleMvid,
        int metadataToken,
        string ilSha256) =>
        string.Equals(fingerprint.AssemblyVersion, assemblyVersion, StringComparison.Ordinal)
        && fingerprint.ModuleMvid == Guid.Parse(moduleMvid)
        && fingerprint.MetadataToken == metadataToken
        && string.Equals(fingerprint.IlSha256, ilSha256, StringComparison.Ordinal);
}
