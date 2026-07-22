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
    public const int ExpectedMetadataToken = 0x060087F6;
    public const string ExpectedIlSha256 = "23ae995c9d6825f8ea24e7febce122641f2660fc3bea0667cf57c3d34b80c857";

    public static bool TryValidate(
        out MethodInfo? target,
        out PreparedDrawTargetFingerprint fingerprint,
        out string reason)
    {
        target = AccessTools.Method(
            typeof(CardPileCmd),
            "DrawInternal",
            [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]);
        if (target?.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            fingerprint = default;
            reason = "CardPileCmd.DrawInternal IL is unavailable.";
            return false;
        }

        fingerprint = new PreparedDrawTargetFingerprint(
            target.Module.Assembly.GetName().Version?.ToString() ?? "unknown",
            target.Module.ModuleVersionId,
            target.MetadataToken,
            Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant());
        if (!string.Equals(fingerprint.AssemblyVersion, ExpectedAssemblyVersion, StringComparison.Ordinal)
            || fingerprint.ModuleMvid != Guid.Parse(ExpectedModuleMvid)
            || fingerprint.MetadataToken != ExpectedMetadataToken
            || !string.Equals(fingerprint.IlSha256, ExpectedIlSha256, StringComparison.Ordinal))
        {
            reason = $"CardPileCmd.DrawInternal fingerprint mismatch ({fingerprint}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

internal readonly record struct PreparedDrawTargetFingerprint(
    string AssemblyVersion,
    Guid ModuleMvid,
    int MetadataToken,
    string IlSha256)
{
    public override string ToString() =>
        $"assembly={AssemblyVersion}, mvid={ModuleMvid:D}, token=0x{MetadataToken:X8}, il={IlSha256}";
}
