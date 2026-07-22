using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.Compatibility;

internal static class FinisherLethalTargetContract
{
    public const string ExpectedAssemblyVersion = "0.1.0.0";
    public const string ExpectedModuleMvid = "a49d3537-5a42-4dcd-9877-663e394f2b44";
    public const int ExpectedMetadataToken = 0x06008438;
    public const string ExpectedIlSha256 = "9c1b0e229a97c39866dcebe88c742175b9d41b27b2d507ed4ca31bfee4f61fc6";

    public static bool TryValidate(
        out MethodInfo? target,
        out FinisherLethalTargetFingerprint fingerprint,
        out string reason)
    {
        target = AccessTools.Method(
            typeof(Creature),
            nameof(Creature.LoseHpInternal),
            [typeof(decimal), typeof(ValueProp)]);
        if (target?.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            fingerprint = default;
            reason = "Creature.LoseHpInternal IL is unavailable.";
            return false;
        }

        fingerprint = new FinisherLethalTargetFingerprint(
            target.Module.Assembly.GetName().Version?.ToString() ?? "unknown",
            target.Module.ModuleVersionId,
            target.MetadataToken,
            Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant());
        if (!string.Equals(fingerprint.AssemblyVersion, ExpectedAssemblyVersion, StringComparison.Ordinal)
            || fingerprint.ModuleMvid != Guid.Parse(ExpectedModuleMvid)
            || fingerprint.MetadataToken != ExpectedMetadataToken
            || !string.Equals(fingerprint.IlSha256, ExpectedIlSha256, StringComparison.Ordinal))
        {
            reason = $"Creature.LoseHpInternal fingerprint mismatch ({fingerprint}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

internal readonly record struct FinisherLethalTargetFingerprint(
    string AssemblyVersion,
    Guid ModuleMvid,
    int MetadataToken,
    string IlSha256)
{
    public override string ToString() =>
        $"assembly={AssemblyVersion}, mvid={ModuleMvid:D}, token=0x{MetadataToken:X8}, il={IlSha256}";
}
