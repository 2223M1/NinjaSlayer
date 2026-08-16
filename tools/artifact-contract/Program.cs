using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NinjaSlayer.Loader;

if (args.Length == 0)
{
    throw Usage();
}

string command = args[0];
IReadOnlyDictionary<string, string> options = ParseOptions(args[1..]);
switch (command)
{
    case "validate-assembly":
        ValidateAssembly(options);
        break;
    case "validate-host":
        ValidateHost(options);
        break;
    case "validate-pck":
        ValidatePck(options);
        break;
    case "validate-workshop-bundle":
        ValidateWorkshopBundle(options);
        break;
    case "test-loader-contract":
        TestLoaderContract();
        break;
    default:
        throw Usage();
}

static void ValidateAssembly(IReadOnlyDictionary<string, string> options)
{
    string assemblyPath = ResolveAssembly(options);
    var expectations = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NinjaSlayerHostChannel"] = Required(options, "channel"),
        ["NinjaSlayerGameApiVersion"] = Required(options, "game-api-version"),
        ["NinjaSlayerRitsuLibPackageId"] = Required(options, "ritsulib-package-id"),
        ["NinjaSlayerRitsuLibVersion"] = Required(options, "ritsulib-version")
    };

    ValidateImplementationAssembly(
        assemblyPath,
        expectations,
        Required(options, "forbidden-path-root"));
    Console.WriteLine($"Validated package assembly {assemblyPath}");
}

static void ValidateImplementationAssembly(
    string assemblyPath,
    IReadOnlyDictionary<string, string> expectations,
    string forbiddenPathRoot)
{
    using FileStream stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    ValidateReleaseDebugDirectory(peReader);
    ValidateForbiddenPath(peReader, forbiddenPathRoot);
    MetadataReader reader = peReader.GetMetadataReader();
    var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
    {
        CustomAttribute attribute = reader.GetCustomAttribute(handle);
        if (!IsAssemblyMetadataAttribute(reader, attribute.Constructor))
        {
            continue;
        }
        BlobReader value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1)
        {
            throw new InvalidDataException("AssemblyMetadataAttribute has an invalid custom-attribute prolog.");
        }
        string key = value.ReadSerializedString()
            ?? throw new InvalidDataException("AssemblyMetadataAttribute has a null key.");
        string metadataValue = value.ReadSerializedString() ?? string.Empty;
        if (!metadata.TryAdd(key, metadataValue))
        {
            throw new InvalidDataException($"Package assembly contains duplicate metadata '{key}'.");
        }
    }

    foreach ((string key, string expected) in expectations)
    {
        if (!metadata.TryGetValue(key, out string? actual))
        {
            throw new InvalidDataException($"Package assembly is missing metadata '{key}'.");
        }
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package assembly metadata '{key}' is '{actual}', expected '{expected}'.");
        }
    }

}

static void ValidateWorkshopBundle(IReadOnlyDictionary<string, string> options)
{
    string directory = Path.GetFullPath(Required(options, "directory"));
    string compatibilityPath = Path.GetFullPath(Required(options, "compatibility"));
    string version = Required(options, "version");
    string ritsuLibVersion = Required(options, "ritsulib-version");
    string forbiddenPathRoot = Required(options, "forbidden-path-root");
    if (!Directory.Exists(directory))
    {
        throw new DirectoryNotFoundException(directory);
    }

    using JsonDocument compatibilityDocument = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
    JsonElement compatibility = compatibilityDocument.RootElement;
    JsonElement channels = compatibility.GetProperty("channels");
    string stableVersion = channels.GetProperty("stable").GetProperty("gameApiVersion").GetString()!;
    var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "NinjaSlayer.dll",
        "NinjaSlayer.json",
        "NinjaSlayer.pck",
        VariantBundleContract.ManifestFileName,
        "SHA256SUMS"
    };
    foreach (string channelName in new[] { "stable", "preview" })
    {
        JsonElement profile = channels.GetProperty(channelName);
        string gameApiVersion = profile.GetProperty("gameApiVersion").GetString()!;
        expectedPaths.Add($"lib/{gameApiVersion}/{VariantBundleContract.CompatTargetMarkerName}");
        expectedPaths.Add($"lib/{gameApiVersion}/{VariantBundleContract.VariantAssemblyName}");

        Guid moduleMvid = Guid.ParseExact(
            profile.GetProperty("hostContract").GetProperty("moduleMvid").GetString()!,
            "D");
        BundleVariant selected = VariantBundleContract.Select(directory, moduleMvid);
        if (selected.Channel != channelName || selected.GameApiVersion != gameApiVersion)
        {
            throw new InvalidDataException($"MVID mapping for {channelName} does not match compatibility.json.");
        }
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NinjaSlayerHostChannel"] = channelName,
            ["NinjaSlayerGameApiVersion"] = gameApiVersion,
            ["NinjaSlayerRitsuLibPackageId"] = profile.GetProperty("ritsuLibPackageId").GetString()!,
            ["NinjaSlayerRitsuLibVersion"] = ritsuLibVersion
        };
        ValidateImplementationAssembly(selected.AssemblyPath, metadata, forbiddenPathRoot);
    }

    string[] actualPaths = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
        .ToArray();
    if (actualPaths.Length != expectedPaths.Count || actualPaths.Any(path => !expectedPaths.Contains(path)))
    {
        throw new InvalidDataException(
            $"Workshop bundle contains missing or unexpected files: {string.Join(", ", actualPaths.Order())}");
    }
    foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Workshop bundle contains a reparse point: {path}");
        }
    }

    string loaderPath = Path.Combine(directory, "NinjaSlayer.dll");
    using (FileStream loaderStream = File.OpenRead(loaderPath))
    using (var loaderReader = new PEReader(loaderStream))
    {
        ValidateReleaseDebugDirectory(loaderReader);
        ValidateForbiddenPath(loaderReader, forbiddenPathRoot);
        string loaderName = loaderReader.GetMetadataReader().GetString(
            loaderReader.GetMetadataReader().GetAssemblyDefinition().Name);
        if (loaderName != "NinjaSlayer.Loader")
        {
            throw new InvalidDataException($"Top-level loader assembly is named {loaderName}.");
        }
    }

    using JsonDocument modManifest = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(directory, "NinjaSlayer.json")));
    JsonElement mod = modManifest.RootElement;
    if (mod.GetProperty("version").GetString() != version ||
        mod.GetProperty("min_game_version").GetString() != stableVersion)
    {
        throw new InvalidDataException("Universal mod manifest version or minimum game version is invalid.");
    }
    JsonElement[] ritsuDependencies = mod.GetProperty("dependencies").EnumerateArray()
        .Where(dependency => dependency.GetProperty("id").GetString() == "STS2-RitsuLib")
        .ToArray();
    if (ritsuDependencies.Length != 1 ||
        ritsuDependencies[0].GetProperty("min_version").GetString() != ritsuLibVersion)
    {
        throw new InvalidDataException("Universal mod manifest has an invalid RitsuLib dependency.");
    }

    ValidatePckFile(Path.Combine(directory, "NinjaSlayer.pck"));
    ValidateChecksums(directory, expectedPaths);
    Console.WriteLine($"Validated universal Workshop bundle {directory}");
}

static void ValidatePck(IReadOnlyDictionary<string, string> options)
{
    string pckPath = Path.GetFullPath(Required(options, "pck"));
    ValidatePckFile(pckPath);
    Console.WriteLine($"Validated platform-neutral PCK {pckPath}");
}

static void ValidatePckFile(string pckPath)
{
    const uint PackHeaderMagic = 0x43504447;
    const uint PackDirectoryEncrypted = 1u << 0;
    const uint PackRelativeFileBase = 1u << 1;
    const uint PackSparseBundle = 1u << 2;
    const uint PackKnownFlags = PackDirectoryEncrypted | PackRelativeFileBase | PackSparseBundle;
    const uint PackFileEncrypted = 1u << 0;
    const uint PackFileRemoval = 1u << 1;
    const int FileEntryTailSize = sizeof(ulong) + sizeof(ulong) + 16 + sizeof(uint);

    if (!File.Exists(pckPath))
    {
        throw new FileNotFoundException("PCK does not exist.", pckPath);
    }

    try
    {
        using FileStream stream = File.OpenRead(pckPath);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        if (reader.ReadUInt32() != PackHeaderMagic)
        {
            throw new InvalidDataException("PCK has an invalid Godot pack header.");
        }

        uint version = reader.ReadUInt32();
        if (version is not 2 and not 3)
        {
            throw new InvalidDataException($"PCK format version {version} is unsupported.");
        }

        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        uint packFlags = reader.ReadUInt32();
        if ((packFlags & ~PackKnownFlags) != 0)
        {
            throw new InvalidDataException($"PCK contains unknown pack flags 0x{packFlags:X8}.");
        }
        if ((packFlags & PackDirectoryEncrypted) != 0)
        {
            throw new InvalidDataException("PCK directory encryption is not supported by the release validator.");
        }
        if ((packFlags & PackSparseBundle) != 0)
        {
            throw new InvalidDataException("Sparse PCK bundles are not valid standalone release artifacts.");
        }

        ulong fileBase = reader.ReadUInt64();
        if (fileBase > (ulong)stream.Length)
        {
            throw new InvalidDataException("PCK file base lies outside the artifact.");
        }

        if (version == 3)
        {
            ulong directoryOffset = reader.ReadUInt64();
            if (directoryOffset > (ulong)stream.Length - sizeof(uint))
            {
                throw new InvalidDataException("PCK directory offset lies outside the artifact.");
            }
            stream.Position = checked((long)directoryOffset);
        }
        else
        {
            byte[] reserved = reader.ReadBytes(16 * sizeof(uint));
            if (reserved.Length != 16 * sizeof(uint))
            {
                throw new InvalidDataException("PCK v2 header is truncated.");
            }
        }

        uint fileCount = reader.ReadUInt32();
        ulong minimumDirectoryBytes = (ulong)fileCount * (sizeof(uint) + FileEntryTailSize);
        if (minimumDirectoryBytes > (ulong)(stream.Length - stream.Position))
        {
            throw new InvalidDataException("PCK file count exceeds the remaining directory data.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strictUtf8 = new UTF8Encoding(false, true);
        for (uint index = 0; index < fileCount; index++)
        {
            uint pathByteLength = reader.ReadUInt32();
            long remaining = stream.Length - stream.Position;
            if (pathByteLength > int.MaxValue
                || (ulong)pathByteLength + FileEntryTailSize > (ulong)remaining)
            {
                throw new InvalidDataException($"PCK entry {index} has an invalid path length.");
            }

            byte[] pathBytes = reader.ReadBytes((int)pathByteLength);
            int textLength = pathBytes.Length;
            while (textLength > 0 && pathBytes[textLength - 1] == 0)
            {
                textLength--;
            }
            if (textLength == 0 || pathBytes.AsSpan(0, textLength).Contains((byte)0))
            {
                throw new InvalidDataException($"PCK entry {index} has an invalid NUL-padded path.");
            }

            string path = NormalizePckPath(strictUtf8.GetString(pathBytes, 0, textLength));
            if (!paths.Add(path))
            {
                throw new InvalidDataException($"PCK contains a duplicate path: {path}");
            }
            ValidatePckPath(path);

            ulong fileOffset = reader.ReadUInt64();
            ulong fileSize = reader.ReadUInt64();
            if (fileOffset > ulong.MaxValue - fileBase)
            {
                throw new InvalidDataException($"PCK entry '{path}' has an overflowing data offset.");
            }
            ulong absoluteOffset = fileBase + fileOffset;
            if (absoluteOffset > (ulong)stream.Length
                || fileSize > (ulong)stream.Length - absoluteOffset)
            {
                throw new InvalidDataException($"PCK entry '{path}' data lies outside the artifact.");
            }

            if (reader.ReadBytes(16).Length != 16)
            {
                throw new InvalidDataException($"PCK entry '{path}' has a truncated MD5 field.");
            }
            uint fileFlags = reader.ReadUInt32();
            if ((fileFlags & PackFileEncrypted) != 0)
            {
                throw new InvalidDataException($"PCK entry '{path}' is encrypted.");
            }
            if ((fileFlags & PackFileRemoval) != 0 || (fileFlags & ~(PackFileEncrypted | PackFileRemoval)) != 0)
            {
                throw new InvalidDataException($"PCK entry '{path}' uses unsupported file flags 0x{fileFlags:X8}.");
            }
        }
    }
    catch (EndOfStreamException exception)
    {
        throw new InvalidDataException("PCK header or directory is truncated.", exception);
    }
    catch (DecoderFallbackException exception)
    {
        throw new InvalidDataException("PCK contains a non-UTF-8 path.", exception);
    }
}

static string NormalizePckPath(string rawPath)
{
    string path = rawPath.Replace('\\', '/');
    if (path.StartsWith("res://", StringComparison.Ordinal))
    {
        path = path[6..];
    }
    if (path.Length == 0
        || path.StartsWith('/')
        || path.Contains(':')
        || Path.IsPathFullyQualified(path))
    {
        throw new InvalidDataException($"PCK contains an unsafe path: {rawPath}");
    }

    string[] segments = path.Split('/');
    if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
    {
        throw new InvalidDataException($"PCK contains an unsafe path: {rawPath}");
    }
    return string.Join('/', segments);
}

static void ValidatePckPath(string path)
{
    string firstSegment = path.Split('/')[0];
    if (firstSegment.Equals("output", StringComparison.OrdinalIgnoreCase)
        || firstSegment.Equals(".sts2build", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException($"PCK contains a local work directory: {path}");
    }

    if (path.Equals("addons/spine", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("addons/spine/", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException($"PCK contains the build-only Spine extension: {path}");
    }

    string[] segments = path.Split('/');
    bool nativeLibrary = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || segments.Any(segment => segment.EndsWith(".framework", StringComparison.OrdinalIgnoreCase));
    if (nativeLibrary)
    {
        throw new InvalidDataException($"PCK contains a native platform library: {path}");
    }
}

static void ValidateChecksums(string directory, HashSet<string> expectedPaths)
{
    string[] lines = File.ReadAllLines(Path.Combine(directory, "SHA256SUMS"))
        .Where(line => line.Length > 0)
        .ToArray();
    var expectedChecksums = new HashSet<string>(expectedPaths, StringComparer.Ordinal);
    expectedChecksums.Remove("SHA256SUMS");
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (string line in lines)
    {
        if (line.Length < 67 || line[64..66] != " *")
        {
            throw new InvalidDataException($"Invalid SHA256SUMS entry: {line}");
        }
        string hash = line[..64];
        string path = line[66..];
        if (!hash.All(Uri.IsHexDigit) || !expectedChecksums.Contains(path) || !seen.Add(path))
        {
            throw new InvalidDataException($"Invalid SHA256SUMS entry: {line}");
        }
        using FileStream stream = File.OpenRead(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)));
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA256SUMS does not match {path}.");
        }
    }
    if (!seen.SetEquals(expectedChecksums))
    {
        throw new InvalidDataException("SHA256SUMS does not cover the exact Workshop bundle file set.");
    }
}

static void TestLoaderContract()
{
    string root = Path.Combine(Path.GetTempPath(), $"ninjaslayer-loader-test-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(root);
        Guid stableMvid = Guid.NewGuid();
        Guid previewMvid = Guid.NewGuid();
        string stableHash = WriteVariant(root, "0.1.0", "stable");
        string previewHash = WriteVariant(root, "0.2.0", "preview");
        WriteVariantManifest(root, stableMvid, previewMvid, stableHash, previewHash);

        BundleVariant stable = VariantBundleContract.Select(root, stableMvid);
        BundleVariant preview = VariantBundleContract.Select(root, previewMvid);
        if (stable.Channel != "stable" || preview.Channel != "preview")
        {
            throw new InvalidOperationException("Loader contract selected the wrong channel.");
        }
        ExpectInvalid(() => VariantBundleContract.Select(root, Guid.NewGuid()), "unknown MVID");

        File.AppendAllText(preview.AssemblyPath, "tampered");
        ExpectInvalid(() => VariantBundleContract.Select(root, previewMvid), "hash mismatch");
        previewHash = WriteVariant(root, "0.2.0", "preview");
        WriteVariantManifest(root, stableMvid, previewMvid, stableHash, previewHash);
        File.Delete(Path.Combine(
            root,
            "lib",
            "0.2.0",
            VariantBundleContract.CompatTargetMarkerName));
        ExpectInvalid(() => VariantBundleContract.Select(root, previewMvid), "missing marker");
        previewHash = WriteVariant(root, "0.2.0", "preview");
        WriteVariantManifest(root, stableMvid, previewMvid, stableHash, previewHash, "../outside");
        ExpectInvalid(() => VariantBundleContract.Select(root, stableMvid), "directory escape");
        Console.WriteLine("Loader variant contract tests passed.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}

static string WriteVariant(string root, string version, string content)
{
    string directory = Path.Combine(root, "lib", version);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, VariantBundleContract.CompatTargetMarkerName), version);
    string assemblyPath = Path.Combine(directory, VariantBundleContract.VariantAssemblyName);
    File.WriteAllText(assemblyPath, content);
    return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant();
}

static void WriteVariantManifest(
    string root,
    Guid stableMvid,
    Guid previewMvid,
    string stableHash,
    string previewHash,
    string? previewDirectory = null)
{
    var manifest = new
    {
        schemaVersion = 1,
        variants = new object[]
        {
            new
            {
                channel = "stable",
                gameApiVersion = "0.1.0",
                moduleMvid = stableMvid.ToString("D"),
                directory = "lib/0.1.0",
                assembly = "NinjaSlayer.dll",
                sha256 = stableHash
            },
            new
            {
                channel = "preview",
                gameApiVersion = "0.2.0",
                moduleMvid = previewMvid.ToString("D"),
                directory = previewDirectory ?? "lib/0.2.0",
                assembly = "NinjaSlayer.dll",
                sha256 = previewHash
            }
        }
    };
    File.WriteAllText(
        Path.Combine(root, VariantBundleContract.ManifestFileName),
        JsonSerializer.Serialize(manifest));
}

static void ExpectInvalid(Action action, string scenario)
{
    try
    {
        action();
    }
    catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
    {
        return;
    }
    throw new InvalidOperationException($"Loader contract accepted {scenario}.");
}

static void ValidateReleaseDebugDirectory(PEReader peReader)
{
    foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
    {
        switch (entry.Type)
        {
            case DebugDirectoryEntryType.CodeView:
                CodeViewDebugDirectoryData codeView = peReader.ReadCodeViewDebugDirectoryData(entry);
                throw new InvalidDataException(
                    $"Release assembly contains a CodeView/PDB path: {codeView.Path}");
            case DebugDirectoryEntryType.EmbeddedPortablePdb:
            case DebugDirectoryEntryType.PdbChecksum:
                throw new InvalidDataException(
                    $"Release assembly contains forbidden PDB debug data ({entry.Type}).");
        }
    }
}

static void ValidateForbiddenPath(PEReader peReader, string forbiddenPathRoot)
{
    string root = Path.GetFullPath(forbiddenPathRoot).TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);
    byte[] image = peReader.GetEntireImage().GetContent().ToArray();
    foreach (string path in new[] { root, root.Replace('\\', '/'), root.Replace('/', '\\') }
        .Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (ContainsBytes(image, Encoding.UTF8.GetBytes(path), ignoreAsciiCase: true)
            || ContainsBytes(image, Encoding.Unicode.GetBytes(path), ignoreAsciiCase: true))
        {
            throw new InvalidDataException(
                $"Release assembly contains its absolute build root: {root}");
        }
    }
}

static bool ContainsBytes(byte[] source, byte[] value, bool ignoreAsciiCase)
{
    if (value.Length == 0 || value.Length > source.Length)
    {
        return false;
    }
    for (int index = 0; index <= source.Length - value.Length; index++)
    {
        int offset = 0;
        while (offset < value.Length
               && BytesEqual(source[index + offset], value[offset], ignoreAsciiCase))
        {
            offset++;
        }
        if (offset == value.Length)
        {
            return true;
        }
    }
    return false;
}

static bool BytesEqual(byte left, byte right, bool ignoreAsciiCase)
{
    if (left == right || !ignoreAsciiCase)
    {
        return left == right;
    }
    static byte ToUpperAscii(byte value) => value is >= (byte)'a' and <= (byte)'z'
        ? (byte)(value - 32)
        : value;
    return ToUpperAscii(left) == ToUpperAscii(right);
}

static void ValidateHost(IReadOnlyDictionary<string, string> options)
{
    string assemblyPath = ResolveAssembly(options);
    string expectedMvid = Required(options, "module-mvid");
    if (!Guid.TryParseExact(expectedMvid, "D", out Guid expected))
    {
        throw new InvalidOperationException("--module-mvid must be a D-format GUID.");
    }

    using FileStream stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    MetadataReader reader = peReader.GetMetadataReader();
    Guid actual = reader.GetGuid(reader.GetModuleDefinition().Mvid);
    if (actual != expected)
    {
        throw new InvalidDataException(
            $"Host assembly MVID is {actual:D}, expected {expected:D} for the selected channel.");
    }

    Console.WriteLine($"Validated host assembly {assemblyPath}");
}

static string ResolveAssembly(IReadOnlyDictionary<string, string> options)
{
    string assemblyPath = Path.GetFullPath(Required(options, "assembly"));
    if (!File.Exists(assemblyPath))
    {
        throw new FileNotFoundException("Assembly does not exist.", assemblyPath);
    }
    return assemblyPath;
}

static IReadOnlyDictionary<string, string> ParseOptions(string[] values)
{
    var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 0; index < values.Length; index += 2)
    {
        if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid argument near '{values[index]}'.");
        }
        string name = values[index][2..];
        if (!parsed.TryAdd(name, values[index + 1]))
        {
            throw new InvalidOperationException($"Duplicate argument '--{name}'.");
        }
    }
    return parsed;
}

static string Required(IReadOnlyDictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing '--{name}'.");
    }
    return value;
}

static bool IsAssemblyMetadataAttribute(MetadataReader reader, EntityHandle constructor)
{
    EntityHandle declaringType = constructor.Kind switch
    {
        HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
        HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
        _ => default
    };
    return declaringType.Kind switch
    {
        HandleKind.TypeReference => IsAssemblyMetadataTypeReference(
            reader,
            reader.GetTypeReference((TypeReferenceHandle)declaringType)),
        HandleKind.TypeDefinition => IsAssemblyMetadataTypeDefinition(
            reader,
            reader.GetTypeDefinition((TypeDefinitionHandle)declaringType)),
        _ => false
    };
}

static bool IsAssemblyMetadataTypeReference(MetadataReader reader, TypeReference type) =>
    reader.StringComparer.Equals(type.Namespace, "System.Reflection")
    && reader.StringComparer.Equals(type.Name, "AssemblyMetadataAttribute");

static bool IsAssemblyMetadataTypeDefinition(MetadataReader reader, TypeDefinition type) =>
    reader.StringComparer.Equals(type.Namespace, "System.Reflection")
    && reader.StringComparer.Equals(type.Name, "AssemblyMetadataAttribute");

static InvalidOperationException Usage() => new(
    "Usage: validate-assembly --assembly <file> --channel <stable|preview> " +
    "--game-api-version <version> --ritsulib-package-id <id> --ritsulib-version <version> " +
    "--forbidden-path-root <directory> | " +
    "validate-host --assembly <sts2.dll> --module-mvid <guid> | " +
    "validate-pck --pck <file> | " +
    "validate-workshop-bundle --directory <dir> --compatibility <json> --version <version> " +
    "--ritsulib-version <version> --forbidden-path-root <directory> | test-loader-contract");
