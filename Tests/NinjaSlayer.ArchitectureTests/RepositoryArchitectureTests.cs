using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NinjaSlayer.ArchitectureTests;

public sealed class RepositoryArchitectureTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly IReadOnlyList<SourceDocument> Sources = LoadSources();
    private static readonly CSharpCompilation Compilation = CreateCompilation();

    [Fact]
    public void EveryPatchBelongsToExactlyOneTypedCapabilityGroup()
    {
        Dictionary<string, INamedTypeSymbol> patches = DeclaredClasses()
            .Where(item => item.Symbol.AllInterfaces.Any(type => type.Name == "IPatchMethod")
                || item.Declaration.BaseList?.Types.Any(type => type.Type.ToString().EndsWith("IPatchMethod", StringComparison.Ordinal)) == true)
            .ToDictionary(item => item.Symbol.Name, item => item.Symbol, StringComparer.Ordinal);
        Dictionary<string, int> registrations = patches.Keys.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);

        foreach ((ClassDeclarationSyntax declaration, INamedTypeSymbol symbol) in DeclaredClasses()
                     .Where(item => item.Symbol.AllInterfaces.Any(type => type.Name == "IModPatches")
                         || item.Declaration.BaseList?.Types.Any(type => type.Type.ToString().EndsWith("IModPatches", StringComparison.Ordinal)) == true))
        {
            SemanticModel model = Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (InvocationExpressionSyntax invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Name: GenericNameSyntax { Identifier.Text: "RegisterPatch", TypeArgumentList.Arguments.Count: 1 } generic
                    })
                {
                    continue;
                }

                ITypeSymbol? patchType = model.GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type;
                string patchName = patchType?.Name ?? generic.TypeArgumentList.Arguments[0].ToString();
                Assert.True(patches.ContainsKey(patchName),
                    $"Capability group {symbol.Name} registers unknown patch {patchName}.");
                registrations[patchName]++;
            }
        }

        foreach ((string patch, int count) in registrations)
        {
            Assert.True(count == 1, $"Patch {patch} must belong to exactly one capability group; found {count}.");
        }
    }

    [Fact]
    public void EveryTypedCapabilityGroupIsInstalledExactlyOnce()
    {
        string[] groups = DeclaredClasses()
            .Where(item => item.Symbol.AllInterfaces.Any(type => type.Name == "IModPatches")
                || item.Declaration.BaseList?.Types.Any(type =>
                    type.Type.ToString().EndsWith("IModPatches", StringComparison.Ordinal)) == true)
            .Select(item => item.Symbol.Name)
            .ToArray();
        CompilationUnitSyntax entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root;

        foreach (string group in groups)
        {
            int installationCount = entry.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(invocation => invocation.Expression is GenericNameSyntax
                {
                    Identifier.Text: "InstallCapability",
                    TypeArgumentList.Arguments.Count: 1
                } generic && generic.TypeArgumentList.Arguments[0].ToString() == group);
            Assert.Equal(1, installationCount);
        }
    }

    [Fact]
    public void CapabilityIdsAreCentralizedAndUnique()
    {
        ClassDeclarationSyntax ids = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerCapabilityIds");
        VariableDeclaratorSyntax[] declarations = ids.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable => variable.Initializer?.Value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            .ToArray();
        string[] values = declarations
            .Select(variable => variable.Initializer?.Value)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.Token.ValueText)
            .ToArray();

        Assert.NotEmpty(values);
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());

        CompilationUnitSyntax entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root;
        Assert.DoesNotContain(
            entry.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression is GenericNameSyntax { Identifier.Text: "InstallCapability" }
                && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax);

        string[] installedIds = entry.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is GenericNameSyntax { Identifier.Text: "InstallCapability" })
            .Select(invocation => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(member => member.Expression.ToString() == "NinjaSlayerCapabilityIds")
            .Select(member => member.Name.Identifier.Text)
            .ToArray();
        Assert.Equal(
            declarations.Select(declaration => declaration.Identifier.Text).Order(StringComparer.Ordinal),
            installedIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RuntimeCapabilityGatesHaveNoMutableSetters()
    {
        ClassDeclarationSyntax gates = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerPatchCapabilities");

        Assert.All(
            gates.Members.OfType<PropertyDeclarationSyntax>(),
            property => Assert.True(
                property.AccessorList == null
                || property.AccessorList.Accessors.All(accessor =>
                    !accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                    && !accessor.IsKind(SyntaxKind.InitAccessorDeclaration)),
                $"Capability gate {property.Identifier.Text} must be get-only."));
    }

    [Fact]
    public void PreparedSafetyRunsAfterPileHooksAndGameplayCreationIsGated()
    {
        string preparedPatches = Sources
            .Single(source => source.RelativePath == "Code/Patches/PreparedCardPatches.cs")
            .Root
            .ToFullString();
        Assert.Contains("PreparedPileChangeSafetyPatch", preparedPatches, StringComparison.Ordinal);
        Assert.Contains("CompletePileChangeAfter(__result, card, oldPile)", preparedPatches, StringComparison.Ordinal);
        Assert.DoesNotContain("PreparedPileExitPatch", preparedPatches, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(CardPile.RemoveInternal)", preparedPatches, StringComparison.Ordinal);

        MethodDeclarationSyntax canPrepare = Sources
            .Single(source => source.RelativePath == "Code/Commands/PrepareCmd.cs")
            .Root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "CanPrepare");
        Assert.Contains("PreparedGameplayEnabled", canPrepare.ToFullString(), StringComparison.Ordinal);

        string entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root.ToFullString();
        Assert.True(
            entry.IndexOf("InstallCapability<PreparedSafetyPatchGroup>", StringComparison.Ordinal)
            < entry.IndexOf("InstallCapability<PreparedGameplayPatchGroup>", StringComparison.Ordinal));
    }

    [Fact]
    public void ChadoRulesDoNotOwnNodesAndPresentationCoversEveryBindingOrder()
    {
        string rules = Sources
            .Single(source => source.RelativePath == "Content/ChadoCombatRules.cs")
            .Root
            .ToFullString();
        Assert.DoesNotContain("NCard", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateVisuals", rules, StringComparison.Ordinal);

        string patches = Sources
            .Single(source => source.RelativePath == "Code/Patches/ChadoPresentationPatches.cs")
            .Root
            .ToFullString();
        Assert.Contains("nameof(CardModel.InvokeEnergyCostChanged)", patches, StringComparison.Ordinal);
        Assert.Contains("nameof(NCard._Ready)", patches, StringComparison.Ordinal);
        Assert.Contains("PatchTarget.Setter<NCard>(nameof(NCard.Model))", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("set_Model", patches, StringComparison.Ordinal);
    }

    [Fact]
    public void HarmonyInstallationIsCentralizedInCompatibilityInfrastructure()
    {
        string[] allowed =
        [
            Normalize("Code/Compatibility/Patching/ExplicitHarmonyPatchAdapter.cs")
        ];

        foreach (SourceDocument source in Sources)
        {
            bool installsHarmony = source.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Any(creation => creation.Type.ToString() is "Harmony" or "HarmonyLib.Harmony")
                || source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(invocation => invocation.Expression.ToString().Contains(".Patch(", StringComparison.Ordinal));
            if (installsHarmony)
            {
                Assert.Contains(source.RelativePath, allowed);
            }
        }
    }

    [Fact]
    public void ModRegistrationUsesTheCanonicalModId()
    {
        string[] registrationMethods =
        [
            "CreateLogger", "CreatePatcher", "BeginModDataRegistration", "CreateContentPack",
            "RegisterApplicant", "GetRunSavedDataStore"
        ];

        foreach (SourceDocument source in Sources)
        {
            foreach (InvocationExpressionSyntax invocation in source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                    IdentifierNameSyntax identifier => identifier.Identifier.Text,
                    _ => string.Empty
                };
                if (!registrationMethods.Contains(methodName, StringComparer.Ordinal))
                {
                    continue;
                }

                Assert.DoesNotContain(invocation.ArgumentList.Arguments,
                    argument => argument.Expression is LiteralExpressionSyntax literal
                        && literal.IsKind(SyntaxKind.StringLiteralExpression)
                        && literal.Token.ValueText == "NinjaSlayer");
            }
        }
    }

    [Fact]
    public void PatchIdsAreUniqueAndNonEmpty()
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((ClassDeclarationSyntax declaration, INamedTypeSymbol symbol) in DeclaredClasses())
        {
            if (!symbol.AllInterfaces.Any(type => type.Name == "IPatchMethod")
                && declaration.BaseList?.Types.Any(type => type.Type.ToString().EndsWith("IPatchMethod", StringComparison.Ordinal)) != true)
            {
                continue;
            }

            PropertyDeclarationSyntax? property = declaration.Members.OfType<PropertyDeclarationSyntax>()
                .SingleOrDefault(member => member.Identifier.Text == "PatchId");
            string? id = property?.ExpressionBody?.Expression is LiteralExpressionSyntax literal
                ? literal.Token.ValueText
                : null;
            Assert.False(string.IsNullOrWhiteSpace(id), $"Patch {symbol.Name} must declare a literal PatchId.");
            Assert.True(ids.TryAdd(id!, symbol.Name),
                $"Patch id {id} is shared by {ids.GetValueOrDefault(id!)} and {symbol.Name}.");
        }
    }

    [Fact]
    public void LethalProtectionPatchMaintainsTypedFinalizerContract()
    {
        ClassDeclarationSyntax patch = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerFinisherLethalDamagePatch");
        MethodDeclarationSyntax prefix = patch.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Prefix");
        MethodDeclarationSyntax postfix = patch.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Postfix");
        MethodDeclarationSyntax finalizer = patch.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Finalizer");

        ParameterSyntax prefixState = prefix.ParameterList.Parameters.Single(parameter => parameter.Identifier.Text == "__state");
        ParameterSyntax postfixState = postfix.ParameterList.Parameters.Single(parameter => parameter.Identifier.Text == "__state");
        ParameterSyntax finalizerState = finalizer.ParameterList.Parameters.Single(parameter => parameter.Identifier.Text == "__state");
        Assert.Equal("FinisherProtectionToken?", prefixState.Type?.ToString());
        Assert.Equal(prefixState.Type?.ToString(), postfixState.Type?.ToString());
        Assert.Equal(prefixState.Type?.ToString(), finalizerState.Type?.ToString());
        Assert.Contains(postfix.ParameterList.Parameters,
            parameter => parameter.Identifier.Text == "__runOriginal" && parameter.Type?.ToString() == "bool");
        Assert.Equal("Exception?", finalizer.ReturnType.ToString());
        Assert.Contains(finalizer.DescendantNodes().OfType<CatchClauseSyntax>(), _ => true);
        Assert.Contains(finalizer.DescendantNodes().OfType<ReturnStatementSyntax>(),
            statement => statement.Expression?.ToString() == "__exception");
    }

    private static IEnumerable<(ClassDeclarationSyntax Declaration, INamedTypeSymbol Symbol)> DeclaredClasses()
    {
        foreach (SourceDocument source in Sources)
        {
            SemanticModel model = Compilation.GetSemanticModel(source.Tree);
            foreach (ClassDeclarationSyntax declaration in source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
                {
                    yield return (declaration, symbol);
                }
            }
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        MetadataReference[] references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return CSharpCompilation.Create(
            "NinjaSlayer.ArchitectureModel",
            Sources.Select(source => source.Tree),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IReadOnlyList<SourceDocument> LoadSources()
    {
        string[] roots = ["Afflictions", "Ancients", "Cards", "Code", "Content", "Enchantments", "Events", "Powers", "Relics", "Scripts"];
        return roots
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(Root, directory), "*.cs", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                string text = File.ReadAllText(path);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(
                    text,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    path);
                return new SourceDocument(
                    Normalize(Path.GetRelativePath(Root, path)),
                    tree,
                    tree.GetCompilationUnitRoot());
            })
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NinjaSlayer.csproj")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed record SourceDocument(
        string RelativePath,
        SyntaxTree Tree,
        CompilationUnitSyntax Root);
}
