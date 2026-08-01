using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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

    using FileStream stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
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

    Console.WriteLine($"Validated package assembly {assemblyPath}");
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
    "--game-api-version <version> --ritsulib-package-id <id> --ritsulib-version <version> | " +
    "validate-host --assembly <sts2.dll> --module-mvid <guid>");
