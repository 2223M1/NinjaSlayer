using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NinjaSlayer.Loader;

internal static partial class VariantBundleContract
{
    internal const string ManifestFileName = "ninjaslayer-variants.manifest";
    internal const string VariantAssemblyName = "NinjaSlayer.dll";
    internal const string CompatTargetMarkerName = "compat-target.txt";

    internal static BundleVariant Select(string loaderDirectory, string hostGameApiVersion)
    {
        string root = Path.GetFullPath(loaderDirectory);
        string libRoot = Path.Combine(root, "lib");
        string manifestPath = Path.Combine(root, ManifestFileName);
        BundleManifest manifest = JsonSerializer.Deserialize<BundleManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid variant manifest: {manifestPath}");
        if (manifest.SchemaVersion != 2 || manifest.Variants is not { Count: 2 })
        {
            throw new InvalidDataException("Variant manifest must use schema 2 and contain stable and preview. Update the complete NinjaSlayer package.");
        }

        var channels = new HashSet<string>(StringComparer.Ordinal);
        var versions = new HashSet<string>(StringComparer.Ordinal);
        BundleVariant? selected = null;
        foreach (BundleVariantEntry entry in manifest.Variants)
        {
            BundleVariant candidate = ValidateEntry(root, libRoot, entry);
            if (!channels.Add(candidate.Channel) ||
                !versions.Add(candidate.GameApiVersion))
            {
                throw new InvalidDataException("Variant manifest contains a duplicate channel or version.");
            }
            if (candidate.GameApiVersion == hostGameApiVersion)
            {
                selected = candidate;
            }
        }

        if (!channels.SetEquals(["stable", "preview"]))
        {
            throw new InvalidDataException("Variant manifest must contain stable and preview exactly once.");
        }
        return selected ?? throw new InvalidDataException(
            $"NinjaSlayer does not support STS2 version '{hostGameApiVersion}'. Supported versions: {string.Join(", ", versions)}.");
    }

    private static BundleVariant ValidateEntry(string root, string libRoot, BundleVariantEntry entry)
    {
        string channel = entry.Channel ?? string.Empty;
        string gameApiVersion = entry.GameApiVersion ?? string.Empty;
        string directory = entry.Directory ?? string.Empty;
        string assembly = entry.Assembly ?? string.Empty;
        string sha256 = entry.Sha256 ?? string.Empty;
        if (channel is not ("stable" or "preview") ||
            !VersionCore().IsMatch(gameApiVersion) ||
            directory != $"lib/{gameApiVersion}" ||
            assembly != VariantAssemblyName ||
            !Sha256().IsMatch(sha256))
        {
            throw new InvalidDataException($"Invalid {channel} variant manifest entry.");
        }

        string variantDirectory = Path.GetFullPath(
            Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar)));
        string relativeToLib = Path.GetRelativePath(libRoot, variantDirectory);
        if (Path.IsPathRooted(relativeToLib) || relativeToLib == ".." ||
            relativeToLib.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Variant directory escapes lib: {directory}");
        }

        string markerPath = Path.Combine(variantDirectory, CompatTargetMarkerName);
        if (File.ReadAllText(markerPath).Trim() != gameApiVersion)
        {
            throw new InvalidDataException($"Variant marker does not match {gameApiVersion}.");
        }
        string assemblyPath = Path.Combine(variantDirectory, VariantAssemblyName);
        using FileStream stream = File.OpenRead(assemblyPath);
        string actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Variant SHA-256 mismatch: {assemblyPath}");
        }
        return new BundleVariant(channel, gameApiVersion, assemblyPath);
    }

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionCore();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256();

    private sealed class BundleManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("variants")]
        public List<BundleVariantEntry>? Variants { get; init; }
    }

    private sealed class BundleVariantEntry
    {
        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("gameApiVersion")]
        public string? GameApiVersion { get; init; }

        [JsonPropertyName("directory")]
        public string? Directory { get; init; }

        [JsonPropertyName("assembly")]
        public string? Assembly { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }
    }
}

internal sealed record BundleVariant(
    string Channel,
    string GameApiVersion,
    string AssemblyPath);
