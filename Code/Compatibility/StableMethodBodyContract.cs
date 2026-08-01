using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace NinjaSlayer.Code.Compatibility;

internal readonly record struct MethodBodyFingerprint(
    string AssemblyVersion,
    Guid ModuleMvid,
    int MetadataToken,
    string IlSha256)
{
    public override string ToString() =>
        $"assembly={AssemblyVersion}, mvid={ModuleMvid:D}, token=0x{MetadataToken:X8}, il={IlSha256}";
}

internal static class StableMethodBodyContract
{
    public static bool Matches(
        MethodBodyFingerprint fingerprint,
        GameHostContractProfile profile,
        MethodBodyContract expected) =>
        profile.MatchesHost(fingerprint)
        && fingerprint.MetadataToken == expected.MetadataToken
        && string.Equals(
            fingerprint.IlSha256,
            expected.IlSha256,
            StringComparison.OrdinalIgnoreCase);
}

internal static class MethodBodyFingerprintCapture
{
    public static bool TryCapture(
        MethodInfo? method,
        out MethodBodyFingerprint fingerprint,
        out string reason)
    {
        if (method?.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            fingerprint = default;
            reason = $"{Describe(method)} IL is unavailable.";
            return false;
        }

        fingerprint = new MethodBodyFingerprint(
            method.Module.Assembly.GetName().Version?.ToString() ?? "unknown",
            method.Module.ModuleVersionId,
            method.MetadataToken,
            Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant());
        reason = string.Empty;
        return true;
    }

    public static bool TryCaptureAsyncMoveNext(
        MethodInfo method,
        out MethodBodyFingerprint fingerprint,
        out string reason)
    {
        Type? stateMachine = method
            .GetCustomAttribute<AsyncStateMachineAttribute>()
            ?.StateMachineType;
        MethodInfo? moveNext = stateMachine?.GetMethod(
            nameof(IAsyncStateMachine.MoveNext),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (moveNext is null)
        {
            fingerprint = default;
            reason = $"{Describe(method)} async state-machine MoveNext() is unavailable.";
            return false;
        }

        return TryCapture(moveNext, out fingerprint, out reason);
    }

    private static string Describe(MethodInfo? method) =>
        $"{method?.DeclaringType?.FullName ?? "Unknown"}.{method?.Name ?? "Unknown"}";
}
