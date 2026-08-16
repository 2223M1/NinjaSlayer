using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NinjaSlayer.HostContractCapture;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            RunSelfTests();
            Console.WriteLine("Host contract capture self-tests passed.");
            return 0;
        }

        CaptureOptions options;
        try
        {
            options = CaptureOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        string outputDirectory = Path.Combine(
            options.RepositoryRoot,
            "build",
            "compatibility",
            options.Channel);
        Directory.CreateDirectory(outputDirectory);
        string candidatePath = Path.Combine(outputDirectory, "host-contract.candidate.json");
        string reportPath = Path.Combine(outputDirectory, "layout-report.md");

        try
        {
            string manifestPath = Path.Combine(options.RepositoryRoot, "eng", "compatibility.json");
            string originalManifest = File.ReadAllText(manifestPath, Encoding.UTF8);
            JsonObject manifest = ParseObject(originalManifest, manifestPath);
            JsonObject channel = manifest["channels"]?[options.Channel]?.AsObject()
                ?? throw new InvalidDataException($"Compatibility channel '{options.Channel}' is unavailable.");
            JsonObject current = channel["hostContract"]?.AsObject()
                ?? throw new InvalidDataException($"Compatibility channel '{options.Channel}' has no hostContract.");
            HashSet<string> compileFeatures = channel["compileFeatures"]?.AsArray()
                .Select(node => node?.GetValue<string>() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.Ordinal)
                ?? throw new InvalidDataException($"Compatibility channel '{options.Channel}' has no compileFeatures.");

            string hostDirectory = ResolveHostDirectory(options.GameDirectory);
            JsonObject candidate;
            var context = new HostAssemblyLoadContext(hostDirectory);
            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(hostDirectory, "sts2.dll"));
                candidate = CaptureContract(assembly, current, compileFeatures);
            }
            finally
            {
                context.Unload();
            }

            File.WriteAllText(
                candidatePath,
                candidate.ToJsonString(JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));
            string report = BuildReport(options.Channel, current, candidate);
            File.WriteAllText(reportPath, report, new UTF8Encoding(false));

            if (options.Apply)
            {
                channel["hostContract"] = candidate.DeepClone();
                string updatedManifest = manifest.ToJsonString(JsonOptions) + Environment.NewLine;
                try
                {
                    WriteAtomically(manifestPath, updatedManifest);
                    RunCompatibilitySync(options.RepositoryRoot);
                }
                catch
                {
                    WriteAtomically(manifestPath, originalManifest);
                    RunCompatibilitySync(options.RepositoryRoot);
                    throw;
                }
            }

            Console.WriteLine($"Candidate: {candidatePath}");
            Console.WriteLine($"Report: {reportPath}");
            Console.WriteLine(options.Apply ? "Compatibility manifest updated." : "Compatibility manifest was not modified.");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                reportPath,
                $"# Host contract capture failed\n\n{exception}\n",
                new UTF8Encoding(false));
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static JsonObject CaptureContract(
        Assembly assembly,
        JsonObject current,
        IReadOnlySet<string> compileFeatures)
    {
        string buildVariant = current["buildVariant"]?.GetValue<string>()
            ?? throw new InvalidDataException("Current hostContract has no buildVariant.");
        MethodInfo lethalDamage = ResolveMethod(
            assembly,
            new TargetSpec(
                "lethal-damage",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "LoseHpInternal",
                ["System.Decimal", "MegaCrit.Sts2.Core.ValueProps.ValueProp"]));

        string[] drawParameters =
        [
            "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
            "System.Decimal",
            "MegaCrit.Sts2.Core.Entities.Players.Player",
            "System.Boolean"
        ];
        MethodInfo draw = ResolveMethod(
            assembly,
            new TargetSpec("prepared-draw", "MegaCrit.Sts2.Core.Commands.CardPileCmd", "Draw", drawParameters));
        MethodInfo? drawInternal = TryResolveMethod(
            assembly,
            new TargetSpec(
                "prepared-draw-internal",
                "MegaCrit.Sts2.Core.Commands.CardPileCmd",
                "DrawInternal",
                drawParameters));
        string drawLayout = drawInternal is null ? "DirectAsync" : "WrapperWithAsyncInternal";
        MethodInfo drawImplementation = drawInternal ?? draw;
        MethodInfo drawMoveNext = ResolveMoveNext(drawImplementation, "prepared draw");

        MethodInfo queueAdd = ResolveMethod(
            assembly,
            new TargetSpec(
                "prepared-queue-add",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPile",
                "AddInternal",
                ["MegaCrit.Sts2.Core.Models.CardModel", "System.Int32", "System.Boolean"]));
        MethodInfo queueRemove = ResolveMethod(
            assembly,
            new TargetSpec(
                "prepared-queue-remove",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPile",
                "RemoveInternal",
                ["MegaCrit.Sts2.Core.Models.CardModel", "System.Boolean"]));

        var sensitive = new JsonArray();
        foreach (TargetSpec target in GetSensitiveTargets(compileFeatures))
        {
            MethodInfo method = ResolveMethod(assembly, target);
            MethodInfo capturedMethod = target.CaptureAsyncMoveNext
                ? ResolveMoveNext(method, target.Id)
                : method;
            JsonObject entry = Fingerprint(capturedMethod);
            entry.Insert(0, "id", target.Id);
            entry.Insert(1, "signature", Describe(method));
            entry.Insert(2, "capture", target.CaptureAsyncMoveNext ? "AsyncMoveNext" : "Method");
            if (target.CaptureAsyncMoveNext)
            {
                entry.Insert(3, "stateMachineType", capturedMethod.DeclaringType?.FullName ?? string.Empty);
            }
            sensitive.Add(entry);
        }

        return new JsonObject
        {
            ["buildVariant"] = buildVariant,
            ["assemblyVersion"] = assembly.GetName().Version?.ToString()
                ?? throw new InvalidDataException("sts2.dll has no assembly version."),
            ["moduleMvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D"),
            ["lethalDamage"] = Fingerprint(lethalDamage),
            ["preparedDraw"] = new JsonObject
            {
                ["layout"] = drawLayout,
                ["publicMethod"] = Fingerprint(draw),
                ["internalMethod"] = drawInternal is null ? null : Fingerprint(drawInternal),
                ["asyncMoveNext"] = Fingerprint(drawMoveNext)
            },
            ["preparedQueueAdd"] = Fingerprint(queueAdd),
            ["preparedQueueRemove"] = Fingerprint(queueRemove),
            ["sensitiveMethods"] = sensitive
        };
    }

    private static IReadOnlyList<TargetSpec> GetSensitiveTargets(IReadOnlySet<string> compileFeatures)
    {
        var creatureDamageParameters = new List<string>
        {
            "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
            "System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature>",
            "System.Decimal",
            "MegaCrit.Sts2.Core.ValueProps.ValueProp",
            "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
            "MegaCrit.Sts2.Core.Models.CardModel"
        };
        if (!compileFeatures.Contains("legacyDamageApi"))
        {
            creatureDamageParameters.Add("MegaCrit.Sts2.Core.Entities.Cards.CardPlay");
        }

        return
        [
            new("boss.start-death-animation", "MegaCrit.Sts2.Core.Nodes.Combat.NCreature", "StartDeathAnim", ["System.Boolean"]),
            new("boss.update-music-track", "MegaCrit.Sts2.Core.Nodes.Audio.NRunMusicController", "UpdateTrack", []),
            new("boss.create-single-death-vfx", "MegaCrit.Sts2.Core.Nodes.Vfx.NMonsterDeathVfx", "Create",
                ["MegaCrit.Sts2.Core.Nodes.Combat.NCreature", "System.Threading.CancellationToken"]),
            new("boss.create-grouped-death-vfx", "MegaCrit.Sts2.Core.Nodes.Vfx.NMonsterDeathVfx", "Create",
                ["System.Collections.Generic.List<MegaCrit.Sts2.Core.Nodes.Combat.NCreature>"]),
            new("boss.play-death-vfx", "MegaCrit.Sts2.Core.Nodes.Vfx.NMonsterDeathVfx", "PlayVfx", []),
            new("asset-loading.add-to-cache", "MegaCrit.Sts2.Core.Assets.AssetLoadingSession", "AddToCache", null),
            new("asset-loading.finalize", "MegaCrit.Sts2.Core.Assets.AssetLoadingSession", "FinalizeLoading", []),
            new("asset-loading.process-queue", "MegaCrit.Sts2.Core.Assets.AssetLoadingSession", "ProcessLoadingQueue", []),
            new("preload.load-run-assets", "MegaCrit.Sts2.Core.Assets.PreloadManager", "LoadRunAssets",
                ["System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Models.CharacterModel>"], true),
            new("preload.load-act-assets", "MegaCrit.Sts2.Core.Assets.PreloadManager", "LoadActAssets",
                ["MegaCrit.Sts2.Core.Models.ActModel"], true),
            new("preload.load-room-assets", "MegaCrit.Sts2.Core.Assets.PreloadManager", "LoadRoomAssets",
                ["System.String", "System.Collections.Generic.IEnumerable<System.String>"], true),
            new("feedback.send-button-selected", "MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NSendFeedbackScreen",
                "SendButtonSelected", ["MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton"]),
            new("orobas.generate-initial-options", "MegaCrit.Sts2.Core.Models.Events.Orobas", "GenerateInitialOptions", []),
            new("prepared.shuffle-ftue", "MegaCrit.Sts2.Core.Commands.CardPileCmd", "ShuffleFtueCheck", null),
            new("rapid-card.on-play-wrapper", "MegaCrit.Sts2.Core.Models.CardModel", "OnPlayWrapper",
                [
                    "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                    "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                    "System.Boolean",
                    "MegaCrit.Sts2.Core.Entities.Cards.ResourceInfo",
                    "System.Boolean"
                ], true),
            new("rapid-card.add-during-manual-play", "MegaCrit.Sts2.Core.Commands.CardPileCmd",
                "AddDuringManualCardPlay", null, true),
            new("rapid-card.power-fly", "MegaCrit.Sts2.Core.Models.CardModel", "PlayPowerCardFlyVfx", null, true),
            new("rapid-card.multi-play", "MegaCrit.Sts2.Core.Nodes.Cards.NCard", "AnimMultiCardPlay", null, true),
            new("reporter-pass.set-event-finished", "MegaCrit.Sts2.Core.Models.EventModel", "SetEventFinished",
                ["MegaCrit.Sts2.Core.Localization.LocString"]),
            new("transition.ancient-heal-vfx", "MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout",
                "PlayHealVfxAfterFadeIn", ["MegaCrit.Sts2.Core.Entities.Players.Player", "System.Decimal"]),
            new("tornado.creature-damage", "MegaCrit.Sts2.Core.Commands.CreatureCmd", "Damage",
                creatureDamageParameters.ToArray(), true),
            new("tornado.power-apply", "MegaCrit.Sts2.Core.Commands.PowerCmd", "Apply",
                [
                    "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                    "MegaCrit.Sts2.Core.Models.PowerModel",
                    "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                    "System.Decimal",
                    "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                    "MegaCrit.Sts2.Core.Models.CardModel",
                    "System.Boolean"
                ], true),
            new("tornado.power-modify-amount", "MegaCrit.Sts2.Core.Commands.PowerCmd", "ModifyAmount",
                [
                    "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                    "MegaCrit.Sts2.Core.Models.PowerModel",
                    "System.Decimal",
                    "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                    "MegaCrit.Sts2.Core.Models.CardModel",
                    "System.Boolean"
                ], true)
        ];
    }

    private static MethodInfo ResolveMethod(Assembly assembly, TargetSpec target) =>
        TryResolveMethod(assembly, target)
        ?? throw new MissingMethodException($"Unable to resolve {target.Id}: {target.DeclaringType}.{target.MethodName}.");

    private static MethodInfo? TryResolveMethod(Assembly assembly, TargetSpec target)
    {
        Type type = assembly.GetType(target.DeclaringType, throwOnError: false, ignoreCase: false)
            ?? throw new TypeLoadException($"Unable to resolve {target.Id} type {target.DeclaringType}.");
        MethodInfo[] candidates = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, target.MethodName, StringComparison.Ordinal))
            .Where(method => target.ParameterTypes is null || ParametersMatch(method, target.ParameterTypes))
            .ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new AmbiguousMatchException(
                $"{target.Id} resolved {candidates.Length} overloads of {target.DeclaringType}.{target.MethodName}.")
        };
    }

    private static bool ParametersMatch(MethodInfo method, IReadOnlyList<string> expected)
    {
        ParameterInfo[] actual = method.GetParameters();
        return actual.Length == expected.Count
            && actual.Select(parameter => FormatType(parameter.ParameterType))
                .SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }
        string definition = type.GetGenericTypeDefinition().FullName
            ?? type.GetGenericTypeDefinition().Name;
        int tick = definition.IndexOf('`');
        if (tick >= 0)
        {
            definition = definition[..tick];
        }
        return $"{definition}<{string.Join(",", type.GetGenericArguments().Select(FormatType))}>";
    }

    private static MethodInfo ResolveMoveNext(MethodInfo method, string id)
    {
        Type? stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return stateMachine?.GetMethod(
                nameof(IAsyncStateMachine.MoveNext),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
            ?? throw new MissingMethodException($"{id} has no async state-machine MoveNext().");
    }

    private static JsonObject Fingerprint(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidDataException($"{Describe(method)} has no readable IL body.");
        return new JsonObject
        {
            ["metadataToken"] = $"0x{method.MetadataToken:X8}",
            ["ilSha256"] = Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant()
        };
    }

    private static string Describe(MethodInfo method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}("
        + string.Join(",", method.GetParameters().Select(parameter => FormatType(parameter.ParameterType)))
        + ")";

    private static string BuildReport(string channel, JsonObject current, JsonObject candidate)
    {
        string classification = ClassifyChange(current, candidate);
        var report = new StringBuilder()
            .AppendLine($"# {channel} host contract")
            .AppendLine()
            .AppendLine($"- Classification: `{classification}`")
            .AppendLine($"- Current MVID: `{current["moduleMvid"]}`")
            .AppendLine($"- Candidate MVID: `{candidate["moduleMvid"]}`")
            .AppendLine($"- Current draw layout: `{current["preparedDraw"]?["layout"]}`")
            .AppendLine($"- Candidate draw layout: `{candidate["preparedDraw"]?["layout"]}`")
            .AppendLine()
            .AppendLine("| Target | Current token | Candidate token | IL changed |")
            .AppendLine("|---|---:|---:|---|");
        IReadOnlyDictionary<string, JsonObject> oldMethods = FlattenMethods(current);
        IReadOnlyDictionary<string, JsonObject> newMethods = FlattenMethods(candidate);
        foreach (string id in oldMethods.Keys.Union(newMethods.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            oldMethods.TryGetValue(id, out JsonObject? oldMethod);
            newMethods.TryGetValue(id, out JsonObject? newMethod);
            string oldToken = oldMethod?["metadataToken"]?.GetValue<string>() ?? "missing";
            string newToken = newMethod?["metadataToken"]?.GetValue<string>() ?? "missing";
            string oldIl = oldMethod?["ilSha256"]?.GetValue<string>() ?? string.Empty;
            string newIl = newMethod?["ilSha256"]?.GetValue<string>() ?? string.Empty;
            report.AppendLine($"| `{id}` | `{oldToken}` | `{newToken}` | `{!string.Equals(oldIl, newIl, StringComparison.OrdinalIgnoreCase)}` |");
        }
        return report.ToString();
    }

    private static string ClassifyChange(JsonObject current, JsonObject candidate)
    {
        if (JsonNode.DeepEquals(current, candidate))
        {
            return "no-change";
        }
        if (!string.Equals(
                current["preparedDraw"]?["layout"]?.GetValue<string>(),
                candidate["preparedDraw"]?["layout"]?.GetValue<string>(),
                StringComparison.Ordinal))
        {
            return "async-layout-change";
        }
        JsonObject withoutMvid = candidate.DeepClone().AsObject();
        withoutMvid["moduleMvid"] = current["moduleMvid"]?.DeepClone();
        if (JsonNode.DeepEquals(current, withoutMvid))
        {
            return "mvid-only";
        }
        return "token-or-il-change";
    }

    private static void RunSelfTests()
    {
        JsonObject baseline = CreateSelfTestContract();
        RequireClassification("no-change", baseline, baseline.DeepClone().AsObject());

        JsonObject mvidOnly = baseline.DeepClone().AsObject();
        mvidOnly["moduleMvid"] = "11111111-1111-1111-1111-111111111111";
        RequireClassification("mvid-only", baseline, mvidOnly);

        JsonObject ilChange = baseline.DeepClone().AsObject();
        ilChange["lethalDamage"]!["ilSha256"] = new string('b', 64);
        RequireClassification("token-or-il-change", baseline, ilChange);

        JsonObject layoutChange = baseline.DeepClone().AsObject();
        layoutChange["preparedDraw"]!["layout"] = "WrapperWithAsyncInternal";
        RequireClassification("async-layout-change", baseline, layoutChange);

        MethodInfo? missing = TryResolveMethod(
            typeof(Program).Assembly,
            new TargetSpec(
                "self-test-missing",
                typeof(Program).FullName!,
                "MethodThatMustNotExist",
                []));
        if (missing is not null)
        {
            throw new InvalidOperationException("Missing-target self-test unexpectedly resolved a method.");
        }
    }

    private static JsonObject CreateSelfTestContract()
    {
        JsonObject Method(string token, char hash) => new()
        {
            ["metadataToken"] = token,
            ["ilSha256"] = new string(hash, 64)
        };
        return new JsonObject
        {
            ["buildVariant"] = "self-test",
            ["assemblyVersion"] = "1.0.0.0",
            ["moduleMvid"] = "00000000-0000-0000-0000-000000000000",
            ["lethalDamage"] = Method("0x06000001", 'a'),
            ["preparedDraw"] = new JsonObject
            {
                ["layout"] = "DirectAsync",
                ["publicMethod"] = Method("0x06000002", 'a'),
                ["internalMethod"] = null,
                ["asyncMoveNext"] = Method("0x06000003", 'a')
            },
            ["preparedQueueAdd"] = Method("0x06000004", 'a'),
            ["preparedQueueRemove"] = Method("0x06000005", 'a'),
            ["sensitiveMethods"] = new JsonArray()
        };
    }

    private static void RequireClassification(string expected, JsonObject current, JsonObject candidate)
    {
        string actual = ClassifyChange(current, candidate);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected self-test classification '{expected}', found '{actual}'.");
        }
    }

    private static IReadOnlyDictionary<string, JsonObject> FlattenMethods(JsonObject contract)
    {
        var methods = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        Add("lethalDamage", contract["lethalDamage"]);
        Add("preparedDraw.publicMethod", contract["preparedDraw"]?["publicMethod"]);
        Add("preparedDraw.internalMethod", contract["preparedDraw"]?["internalMethod"]);
        Add("preparedDraw.asyncMoveNext", contract["preparedDraw"]?["asyncMoveNext"]);
        Add("preparedQueueAdd", contract["preparedQueueAdd"]);
        Add("preparedQueueRemove", contract["preparedQueueRemove"]);
        if (contract["sensitiveMethods"] is JsonArray sensitive)
        {
            foreach (JsonNode? node in sensitive)
            {
                JsonObject method = node?.AsObject()
                    ?? throw new InvalidDataException("sensitiveMethods contains a non-object entry.");
                Add($"sensitive.{method["id"]?.GetValue<string>()}", method);
            }
        }
        return methods;

        void Add(string id, JsonNode? node)
        {
            if (node is JsonObject method)
            {
                methods[id] = method;
            }
        }
    }

    private static JsonObject ParseObject(string content, string path) =>
        JsonNode.Parse(content)?.AsObject()
        ?? throw new InvalidDataException($"JSON document is not an object: {path}");

    private static string ResolveHostDirectory(string requestedPath)
    {
        string fullPath = Path.GetFullPath(requestedPath);
        if (File.Exists(Path.Combine(fullPath, "sts2.dll")))
        {
            return fullPath;
        }
        string[] candidates = Directory.EnumerateFiles(fullPath, "sts2.dll", SearchOption.AllDirectories)
            .Take(2)
            .ToArray();
        return candidates.Length switch
        {
            1 => Path.GetDirectoryName(candidates[0])!,
            0 => throw new FileNotFoundException($"No sts2.dll was found under {fullPath}."),
            _ => throw new InvalidOperationException($"Multiple sts2.dll files were found under {fullPath}; pass the data directory.")
        };
    }

    private static void WriteAtomically(string path, string content)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static void RunCompatibilitySync(string repositoryRoot)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "tools", "sync-compatibility.mjs"));
        process.StartInfo.ArgumentList.Add("--write");
        process.Start();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Compatibility generator exited with code {process.ExitCode}.");
        }
    }

    private sealed class HostAssemblyLoadContext(string directory)
        : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            string candidate = Path.Combine(directory, $"{name}.dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }

    private sealed record TargetSpec(
        string Id,
        string DeclaringType,
        string MethodName,
        string[]? ParameterTypes,
        bool CaptureAsyncMoveNext = false);

    private sealed record CaptureOptions(
        string GameDirectory,
        string Channel,
        string RepositoryRoot,
        bool Apply)
    {
        public static CaptureOptions Parse(string[] args)
        {
            if (args.Length < 2)
            {
                throw new InvalidOperationException(
                    "Usage: Capture-GameHostContract <game-dir> <stable|preview> [--apply] [--repository-root <path>]");
            }
            string channel = args[1];
            if (channel is not ("stable" or "preview"))
            {
                throw new InvalidOperationException("Channel must be stable or preview.");
            }
            bool apply = false;
            string repositoryRoot = Directory.GetCurrentDirectory();
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--apply":
                        apply = true;
                        break;
                    case "--repository-root" when index + 1 < args.Length:
                        repositoryRoot = args[++index];
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown argument '{args[index]}'.");
                }
            }
            return new CaptureOptions(
                Path.GetFullPath(args[0]),
                channel,
                Path.GetFullPath(repositoryRoot),
                apply);
        }
    }
}
