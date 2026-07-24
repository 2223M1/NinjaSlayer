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
    public void SmokeDriverCannotEnterTheShippingAssemblyOrPackage()
    {
        string project = File.ReadAllText(Path.Combine(Root, "NinjaSlayer.csproj"));
        string packaging = File.ReadAllText(Path.Combine(Root, "eng", "NinjaSlayer.Packaging.targets"));
        string controller = File.ReadAllText(Path.Combine(
            Root,
            "tools",
            "smoke-harness",
            "NinjaSlayer.SmokeDriver",
            "SmokeController.cs"));
        string entry = File.ReadAllText(Path.Combine(
            Root,
            "tools",
            "smoke-harness",
            "NinjaSlayer.SmokeDriver",
            "Entry.cs"));

        Assert.Contains("<Compile Remove=\"tools\\smoke-harness\\**\\*.cs\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("NinjaSlayer-SmokeDriver", packaging, StringComparison.Ordinal);
        Assert.Contains("$(NinjaSlayerArtifactName).dll;$(NinjaSlayerArtifactName).json;$(NinjaSlayerArtifactName).pck", packaging, StringComparison.Ordinal);
        Assert.DoesNotContain("SmokeController : Node", controller, StringComparison.Ordinal);
        Assert.Contains("controller.Start();", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeWorkflowUsesTrustedMainAndProtectedCanonicalCandidates()
    {
        string workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "smoke.yml"));

        Assert.Contains("environment: game-smoke", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: main", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.Contains("merge-base --is-ancestor", workflow, StringComparison.Ordinal);
        Assert.Contains("trusted/tools/smoke-harness/Invoke-NinjaSlayerSmoke.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("FirstCombatRestart", workflow, StringComparison.Ordinal);
        Assert.Contains("FullAutoSlay", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeLauncherPreservesSaveNetworkAndReleaseBoundaries()
    {
        string launcher = File.ReadAllText(Path.Combine(Root, "tools", "smoke-harness", "Invoke-NinjaSlayerSmoke.ps1"));
        string release = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release.yml"));

        foreach (string required in new[]
                 {
                     "$env:APPDATA = $appDataDirectory",
                     "$env:LOCALAPPDATA = $localAppDataDirectory",
                     "--force-steam=off",
                     "New-NetFirewallRule",
                     "Invoke-SmokePhase -Phase Fresh -ExpectedExitCode 20",
                     "Invoke-SmokePhase -Phase Resume -ExpectedExitCode 0",
                     "Invoke-SmokePhase -Phase FullAutoSlay -ExpectedExitCode 0",
                     "Stop-SmokeProcesses -Root $isolatedGameRoot"
                 })
        {
            Assert.Contains(required, launcher, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("verify-smoke-attestation.ps1", release, StringComparison.Ordinal);
    }

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
    public void NormalAndDebugCharactersShareOneStartingDeckDefinition()
    {
        string entry = SourceText("Scripts/Entry.cs");

        Assert.Contains("Character<NinjaSlayerCharacter>(ConfigureStartingDeck)", entry, StringComparison.Ordinal);
        Assert.Contains("ConfigureStartingDeck(character);", entry, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(entry, "AddStartingCard<StrikeNinjaSlayer>"));
        Assert.Equal(1, CountOccurrences(entry, "AddStartingCard<DefendNinjaSlayer>"));
        Assert.Equal(1, CountOccurrences(entry, "AddStartingCard<Meditation>"));
        Assert.Equal(1, CountOccurrences(entry, "AddStartingCard<KarateStraight>"));
        Assert.Equal(1, CountOccurrences(entry, "AddStartingRelic<ChadoBreathingRelic>"));
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
        Assert.Contains(
            "CompletePileChangeAfter(__result, combatState, card, oldPile)",
            preparedPatches,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PreparedPileExitPatch", preparedPatches, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(CardPile.RemoveInternal)", preparedPatches, StringComparison.Ordinal);

        MethodDeclarationSyntax canPrepare = Sources
            .Single(source => source.RelativePath == "Code/Commands/PrepareCmd.cs")
            .Root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "CanPrepare");
        Assert.Contains("PreparedGameplayEnabled", canPrepare.ToFullString(), StringComparison.Ordinal);

        string prepareCommand = Sources
            .Single(source => source.RelativePath == "Code/Commands/PrepareCmd.cs")
            .Root
            .ToFullString();
        Assert.Contains("internal static class PrepareCmd", prepareCommand, StringComparison.Ordinal);
        Assert.Contains("Task<PreparedApplyResult>", prepareCommand, StringComparison.Ordinal);
        Assert.Contains("PreparedQueueCompatibility.TryReposition", prepareCommand, StringComparison.Ordinal);
        Assert.Contains("RepairAfterApplyFailure", prepareCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveInternal", prepareCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInternal", prepareCommand, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Sources.Select(source => source.Root.ToFullString()),
            source => source.Contains("PreparedQueueReorderContext", StringComparison.Ordinal));

        string queueCompatibility = Sources
            .Single(source => source.RelativePath == "Code/Compatibility/PreparedQueueCompatibility.cs")
            .Root
            .ToFullString();
        Assert.Contains("ExpectedAddIlSha256", queueCompatibility, StringComparison.Ordinal);
        Assert.Contains("ExpectedRemoveIlSha256", queueCompatibility, StringComparison.Ordinal);
        Assert.Contains("PreparedQueueTransaction.Execute", queueCompatibility, StringComparison.Ordinal);

        string actions = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerActions.cs")
            .Root
            .ToFullString();
        string nextDiscard = Sources
            .Single(source => source.RelativePath == "Powers/NextDiscardPreparedPower.cs")
            .Root
            .ToFullString();
        Assert.Contains("_ = await PrepareCmd.Apply", actions, StringComparison.Ordinal);
        Assert.Contains("_ = await PrepareCmd.Apply", nextDiscard, StringComparison.Ordinal);

        string entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root.ToFullString();
        Assert.True(
            entry.IndexOf("InstallCapability<PreparedSafetyPatchGroup>", StringComparison.Ordinal)
            < entry.IndexOf("InstallCapability<PreparedGameplayPatchGroup>", StringComparison.Ordinal));
    }

    [Fact]
    public void PreparedSafetyUsesContextualCombatStateAccess()
    {
        string service = SourceText("Code/Prepared/PreparedSafetyService.cs");
        string accessor = SourceText("Code/Prepared/CombatStateAccessor.cs");
        string patches = SourceText("Code/Patches/PreparedCardPatches.cs");

        Assert.DoesNotContain("DebugOnlyGetState", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DebugOnlyGetState", patches, StringComparison.Ordinal);
        Assert.Contains("ICombatStateAccessor", service, StringComparison.Ordinal);
        Assert.Contains("class CardCombatStateAccessor", accessor, StringComparison.Ordinal);
        Assert.Contains("PreparedCleanupStatus.Deferred", service, StringComparison.Ordinal);
        Assert.Contains("ICombatState? combatState", patches, StringComparison.Ordinal);
    }

    [Fact]
    public void NextDiscardSourceProtectionUsesASerializedOneShotCardMarker()
    {
        string power = Sources
            .Single(source => source.RelativePath == "Powers/NextDiscardPreparedPower.cs")
            .Root
            .ToFullString();
        Assert.Contains("CardCmd.Afflict<NextDiscardSourceAffliction>", power, StringComparison.Ordinal);
        Assert.Contains("CardCmd.ClearAffliction(card)", power, StringComparison.Ordinal);
        Assert.DoesNotContain("CardPlayResolutionScope", power, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceProtectionState", power, StringComparison.Ordinal);

        string marker = Sources
            .Single(source => source.RelativePath == "Afflictions/NextDiscardSourceAffliction.cs")
            .Root
            .ToFullString();
        Assert.Contains("[RegisterAffliction]", marker, StringComparison.Ordinal);
        Assert.Contains("AfterCardChangedPilesLate", marker, StringComparison.Ordinal);
        Assert.Contains("card.Pile?.Type != PileType.Play", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("AfflictionAssetProfile", marker, StringComparison.Ordinal);
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
    public void NancyFilteringAndLoadedRepairHaveIndependentCompatibilityBoundaries()
    {
        string patches = Sources
            .Single(source => source.RelativePath == "Code/Patches/NancyLeeAvailabilityPatches.cs")
            .Root
            .ToFullString();
        Assert.Contains(
            "HarmonyAfter(NancyCompatibility.RitsuLibContentRegistryHarmonyId)",
            patches,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AccessTools", patches, StringComparison.Ordinal);

        string groups = Sources
            .Single(source => source.RelativePath == "Code/Patches/NinjaSlayerPatchGroups.cs")
            .Root
            .ToFullString();
        Assert.Contains("class NancyCandidateFilterPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("class NancyLoadedRunRepairPatchGroup", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("class NancyCompatibilityPatchGroup", groups, StringComparison.Ordinal);

        string compatibility = Sources
            .Single(source => source.RelativePath == "Code/Compatibility/NancyCompatibility.cs")
            .Root
            .ToFullString();
        Assert.Contains("CapabilityProbe.Required", compatibility, StringComparison.Ordinal);
        Assert.DoesNotContain("CapabilityProbe.Optional", compatibility, StringComparison.Ordinal);

        string entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root.ToFullString();
        Assert.Contains("InstallCapability<NancyCandidateFilterPatchGroup>", entry, StringComparison.Ordinal);
        Assert.Contains("InstallCapability<NancyLoadedRunRepairPatchGroup>", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugCardPoolCoversEveryPotionFutureRewardBucket()
    {
        ClassDeclarationSyntax coverage = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerDebugRewardCoverage");
        int minimumCandidates = coverage.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == "MinimumCandidatesPerBucket")
            .Initializer?.Value is LiteralExpressionSyntax minimumLiteral
            ? (int)minimumLiteral.Token.Value!
            : throw new InvalidOperationException("Potion Future minimum candidate count must be a literal integer.");
        VariableDeclaratorSyntax requiredBuckets = coverage.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == "RequiredBuckets");
        (string Rarity, string Type)[] required = RequiredRewardBuckets(requiredBuckets).ToArray();

        ClassDeclarationSyntax catalog = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerDebugCardCatalog");
        HashSet<string> removed = TypeNames(catalog, "RemovedCards").ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> replacements = ReplacementTypes(catalog);
        string[] selectedTypes = TypeNames(catalog, "BaselineCards")
            .Where(type => !removed.Contains(type))
            .Select(type => replacements.GetValueOrDefault(type, type))
            .Concat(TypeNames(catalog, "AdditionalCards"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, (string Rarity, string Type)> cardSpecs = CardRewardMetadata();

        foreach (string cardType in selectedTypes)
        {
            Assert.True(cardSpecs.ContainsKey(cardType), $"Debug card {cardType} has no readable CardSpec metadata.");
        }
        foreach ((string rarity, string type) in required)
        {
            int count = selectedTypes.Count(cardType => cardSpecs[cardType] == (rarity, type));
            Assert.True(
                count >= minimumCandidates,
                $"The Future of Potions requires {minimumCandidates} {rarity}/{type} cards, but the debug pool has {count}.");
        }

        MethodDeclarationSyntax createCards = catalog.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "CreateCards");
        Assert.Contains(
            createCards.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression.ToString()
                == "NinjaSlayerDebugRewardCoverage.EnsurePotionFutureCoverage");
    }

    [Fact]
    public void FormPresentationPoliciesAreCentralized()
    {
        string overlay = Sources
            .Single(source => source.RelativePath == "Code/Nodes/NarakuVisualOverlay.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerFormState.GetPresentation", overlay, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("res://", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("enum FormVisual", overlay, StringComparison.Ordinal);

        string[] presentationConsumers =
        [
            "Code/ExternalAnimations/NinjaSlayerXAttackSequence.cs",
            "Code/ExternalAnimations/SpinComboAudio.cs",
            "Code/Nodes/NinjaSlayerSpinPivot.cs",
            "Content/NinjaSlayerCombatAudio.cs",
            "Content/NinjaSlayerCombatVisuals.cs",
            "Powers/HellTornadoPower.cs"
        ];
        foreach (string relativePath in presentationConsumers)
        {
            string source = Sources.Single(item => item.RelativePath == relativePath).Root.ToFullString();
            Assert.Contains("NinjaSlayerFormState.GetPresentation", source, StringComparison.Ordinal);
        }

        foreach (SourceDocument source in Sources.Where(source => source.RelativePath != "Content/NinjaSlayerFormState.cs"))
        {
            Assert.DoesNotContain("IsFullyReleasedNaraku(", source.Root.ToFullString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CharacterPresentationResponsibilitiesAreSeparated()
    {
        string character = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerCharacter.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerCharacterStats", character, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAssetProfile.Profile", character, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAnimations.BuildCombatAnimationStateMachine", character, StringComparison.Ordinal);
        Assert.DoesNotContain("res://", character, StringComparison.Ordinal);
        Assert.DoesNotContain("ModVisualCues", character, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterWorldProceduralVisualSetBuilder", character, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualNodeStyle", character, StringComparison.Ordinal);

        string assets = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAssetProfile.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerAnimationCatalog.CombatVisualCues", assets, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerWorldVisualProfile.Profile", assets, StringComparison.Ordinal);

        string animations = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAnimationCatalog.cs")
            .Root
            .ToFullString();
        Assert.Contains("IdleFrameDuration = 1f / 24f", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"idle\", AddIdleFrames)", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"archived_attack\"", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"archived_hit\"", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"archived_blocked_hit\"", animations, StringComparison.Ordinal);
    }

    [Fact]
    public void AlabamaDropUsesTheSharedFixedAxisSpinProjection()
    {
        string alabamaDrop = SourceText("Code/ExternalAnimations/AlabamaDropAnimation.cs");
        string soarSpin = SourceText("Code/ExternalAnimations/SoarSpinAnimation.cs");
        string projection = SourceText("Code/ExternalAnimations/VerticalAxisSpinProjection.cs");

        Assert.Contains("VerticalAxisSpinProjection.CaptureCurrent", alabamaDrop, StringComparison.Ordinal);
        Assert.Contains("PlayFiniteVerticalAxisProjection", alabamaDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerOffsetX", alabamaDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("targetOffsetX", alabamaDrop, StringComparison.Ordinal);
        Assert.Contains("VerticalAxisSpinProjection.CaptureNinjaSlayer", soarSpin, StringComparison.Ordinal);
        Assert.Contains("VerticalSpinMath.ProjectCoordinate", projection, StringComparison.Ordinal);
        Assert.Contains("projectedMarker - _body.Transform.BasisXform(_markerBodyLocal)", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterStatsAndAssetPathsMatchThePreSplitSnapshot()
    {
        CompilationUnitSyntax stats = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerCharacterStats.cs")
            .Root;
        Dictionary<string, string> statValues = stats.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Initializer is not null)
            .ToDictionary(
                variable => variable.Identifier.Text,
                variable => variable.Initializer!.Value.ToString(),
                StringComparer.Ordinal);
        Assert.Equal("72", statValues["StartingHp"]);
        Assert.Equal("99", statValues["StartingGold"]);
        Assert.Equal("0.15f", statValues["AttackAnimDelay"]);
        Assert.Equal("0.2f", statValues["CastAnimDelay"]);
        Assert.Equal("false", statValues["RequiresEpochAndTimeline"]);

        CompilationUnitSyntax assets = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAssetProfile.cs")
            .Root;
        Dictionary<string, string> paths = assets.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Initializer?.Value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            .ToDictionary(
                variable => variable.Identifier.Text,
                variable => ((LiteralExpressionSyntax)variable.Initializer!.Value).Token.ValueText,
                StringComparer.Ordinal);
        Assert.Equal("res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn", paths["VisualsPath"]);
        Assert.Equal("res://NinjaSlayer/scenes/ui/ninja_slayer_energy_counter.tscn", paths["EnergyCounterPath"]);
        Assert.Equal(
            "res://NinjaSlayer/materials/transitions/ninja_slayer_transition_mat.tres",
            paths["CharacterSelectTransitionMaterialPath"]);
        Assert.Equal("res://NinjaSlayer/videos/ninja_slayer_transition.ogv", paths["TransitionVideoPath"]);
    }

    [Fact]
    public void WorldPresentationStylesUseAbsoluteCoordinates()
    {
        CompilationUnitSyntax world = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerWorldVisualProfile.cs")
            .Root;
        Assert.DoesNotContain(
            world.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "WithOffset"
            });

        ClassDeclarationSyntax[] placements = world.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.Text is "Merchant" or "RestSite")
            .ToArray();
        Assert.Equal(2, placements.Length);
        foreach (ClassDeclarationSyntax placement in placements)
        {
            MethodDeclarationSyntax style = placement.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.Text == "BodyStyle");
            InvocationExpressionSyntax[] calls = style.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .ToArray();
            Assert.Single(calls, invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "WithPosition"
            });
            Assert.Single(calls, invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "WithScale"
            });
        }
    }

    [Fact]
    public void PresentationConsumersUseDedicatedCatalogs()
    {
        string animations = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAnimations.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerAnimationCatalog.AttackCueName", animations, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAnimationCatalog.CombatVisualCues", animations, StringComparison.Ordinal);

        string transitionVideo = Sources
            .Single(source => source.RelativePath == "Code/Transition/NinjaSlayerTransitionVideo.cs")
            .Root
            .ToFullString();
        string transitionPaths = Sources
            .Single(source => source.RelativePath == "Code/Transition/NinjaSlayerTransitionPaths.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerAssetProfile.TransitionVideoPath", transitionVideo, StringComparison.Ordinal);
        Assert.Contains(
            "NinjaSlayerAssetProfile.CharacterSelectTransitionMaterialPath",
            transitionPaths,
            StringComparison.Ordinal);

        foreach (SourceDocument source in Sources.Where(source => source.RelativePath != "Content/NinjaSlayerCharacter.cs"))
        {
            string text = source.Root.ToFullString();
            Assert.DoesNotContain("NinjaSlayerCharacter.CombatVisualCues", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NinjaSlayerCharacter.TransitionVideoPath", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NinjaSlayerCharacter.OriginalAnimations", text, StringComparison.Ordinal);
        }
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
    public void RunHistoryTelemetryUsesTheFailClosedLocalIdentityTracker()
    {
        string telemetry = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerBalanceTelemetry.cs")
            .Root
            .ToFullString();

        Assert.Contains("captureFilter: ShouldCaptureRunHistory", telemetry, StringComparison.Ordinal);
        Assert.Contains("IdentityTracker.TryCaptureCompletedRun", telemetry, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerPatchCapabilities.TelemetryIdentityEnabled", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("evt.Run.Players.Any", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotNodeScriptsArePubliclyResolvableFromScriptPathAttributes()
    {
        foreach ((ClassDeclarationSyntax declaration, INamedTypeSymbol symbol) in DeclaredClasses())
        {
            bool isGodotScript = declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                && declaration.BaseList?.Types.Any(type => type.Type.ToString() is
                    "Node" or "Node2D" or "Control" or "Godot.Node" or "Godot.Node2D" or "Godot.Control") == true;
            if (isGodotScript)
            {
                Assert.Equal(Accessibility.Public, symbol.DeclaredAccessibility);
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

    [Fact]
    public void FinisherAttackAdaptationIsSeparatedFromTheCinematicOrchestrator()
    {
        string orchestrator = Sources
            .Single(source => source.RelativePath == "Code/ExternalAnimations/NinjaSlayerFinisherCinematic.cs")
            .Root
            .ToFullString();
        string adapter = Sources
            .Single(source => source.RelativePath == "Code/ExternalAnimations/FinisherAttackCommandAdapter.cs")
            .Root
            .ToFullString();

        Assert.DoesNotContain("class FinisherAttackCommandAdapter", orchestrator, StringComparison.Ordinal);
        Assert.Contains("GameCompatibility.Finisher.TryReadAttackCommand", adapter, StringComparison.Ordinal);
        Assert.Contains("new FinisherAttackSpec", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherRuntimeResponsibilitiesRemainSeparated()
    {
        string orchestrator = SourceText("Code/ExternalAnimations/NinjaSlayerFinisherCinematic.cs");
        string session = SourceText("Code/ExternalAnimations/FinisherSession.cs");
        string registry = SourceText("Code/ExternalAnimations/FinisherSessionRegistry.cs");
        string eligibility = SourceText("Code/ExternalAnimations/FinisherEligibilityService.cs");
        string protection = SourceText("Code/ExternalAnimations/FinisherProtectionService.cs");
        string cleanup = SourceText("Code/ExternalAnimations/FinisherCleanupService.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");

        Assert.DoesNotContain("class FinisherSession", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionRegistrySync", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("static class FinisherForecast", orchestrator, StringComparison.Ordinal);
        Assert.Contains("class FinisherSession", session, StringComparison.Ordinal);
        Assert.Contains("static class FinisherSessionRegistry", registry, StringComparison.Ordinal);
        Assert.Contains("static class FinisherEligibilityService", eligibility, StringComparison.Ordinal);
        Assert.Contains("static class FinisherProtectionService", protection, StringComparison.Ordinal);
        Assert.Contains("static class FinisherCleanupService", cleanup, StringComparison.Ordinal);
        Assert.Contains("static class FinisherForecast", forecast, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherForecastUsesDeterministicStructuredSearchAndFrameCaching()
    {
        string engine = SourceText("Code/Combat/FinisherForecastEngine.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");

        Assert.DoesNotContain("StringBuilder", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<TState, string>", engine, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastSimulation<TState, TStateKey>", engine, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastSearchKey<TStateKey>", engine, StringComparison.Ordinal);
        Assert.Contains("FrameScopedCache<FinisherForecastFrameKey, CachedForecast>", forecast, StringComparison.Ordinal);
        Assert.Contains("Engine.GetProcessFrames()", forecast, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionLifecycleUsesAViewAdapter()
    {
        string session = SourceText("Code/Transition/NinjaSlayerTransitionSession.cs");
        string transitionPatch = SourceText("Code/Patches/NinjaSlayerTransitionPatch.cs");
        string adapter = SourceText("Code/Transition/TransitionViewAdapter.cs");

        Assert.Contains("ITransitionViewAdapter", session, StringComparison.Ordinal);
        Assert.DoesNotContain("SimpleTransition", session, StringComparison.Ordinal);
        Assert.DoesNotContain("GradientTransition", session, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNode", session, StringComparison.Ordinal);
        Assert.DoesNotContain("SimpleTransition", transitionPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("GradientTransition", transitionPatch, StringComparison.Ordinal);
        Assert.Contains("SimpleTransitionPath", adapter, StringComparison.Ordinal);
        Assert.Contains("GradientTransitionPath", adapter, StringComparison.Ordinal);
        Assert.Contains("void Restore(bool forceRelease)", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionSmoothingPreservesMediaTimingAndDefersOnlyItsGcFlush()
    {
        string audio = SourceText("Content/NinjaSlayerAudio.cs");
        string smoothing = SourceText("Code/Transition/NinjaSlayerTransitionLoadSmoothing.cs");
        string session = SourceText("Code/Transition/NinjaSlayerTransitionSession.cs");
        string overlay = SourceText("Code/Nodes/NinjaSlayerTransitionOverlay.cs");

        Assert.Contains("TransitionVisualSeconds = 2f", audio, StringComparison.Ordinal);
        Assert.Contains("EmbarkLoadStartDelaySeconds = 0.2f", audio, StringComparison.Ordinal);
        Assert.Contains("SaveLoadStartDelaySeconds = 0.6f", audio, StringComparison.Ordinal);
        Assert.Contains("GCCollectionMode.Optimized", smoothing, StringComparison.Ordinal);
        Assert.Contains("blocking: false", smoothing, StringComparison.Ordinal);
        Assert.Contains("TransitionGcRequestExecutor.Execute", smoothing, StringComparison.Ordinal);
        Assert.DoesNotContain("EndAnimationAndCollectDeferred", smoothing, StringComparison.Ordinal);
        Assert.Contains("EndAnimationSmoothing", session, StringComparison.Ordinal);
        Assert.Contains("CompleteLoadSmoothing", session, StringComparison.Ordinal);
        Assert.Contains("RecordFrame(delta, videoPosition)", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionDecoderPrewarmReusesTheSilentOfficialPlayerBeforeFormalPlayback()
    {
        string overlay = SourceText("Code/Nodes/NinjaSlayerTransitionOverlay.cs");
        string prewarmer = SourceText("Code/Transition/NinjaSlayerTransitionVideoPrewarmer.cs");
        string preloadPatch = SourceText("Code/Patches/NinjaSlayerTransitionPreloadPatch.cs");

        Assert.Contains("videoPlayer.Volume = 0f", overlay, StringComparison.Ordinal);
        Assert.Contains("videoPlayer.Modulate = Colors.Transparent", overlay, StringComparison.Ordinal);
        Assert.Contains("videoPlayer.Stop()", overlay, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerTransitionOverlay.GetOrCreate(game.Transition)", prewarmer, StringComparison.Ordinal);
        Assert.Contains("StopDecoderPrewarmForPlayback", prewarmer, StringComparison.Ordinal);
        Assert.Contains("typeof(NMainMenu)", preloadPatch, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerTransitionVideoPrewarmer.TryStart();", preloadPatch, StringComparison.Ordinal);
        Assert.Contains("characterModel is INinjaSlayerCharacter", preloadPatch, StringComparison.Ordinal);

        int takeover = overlay.IndexOf(
            "NinjaSlayerTransitionVideoPrewarmer.PrepareForPlayback()",
            StringComparison.Ordinal);
        int formalPlay = overlay.IndexOf("videoPlayer.Play()", StringComparison.Ordinal);
        Assert.True(takeover >= 0 && takeover < formalPlay);
    }

    [Fact]
    public void TransitionLoadSmoothingBoundsOnlyVisibleAssetConcurrency()
    {
        string patch = SourceText("Code/Patches/NinjaSlayerTransitionLoadSmoothingPatch.cs");
        string policy = SourceText("Code/Transition/TransitionLoadConcurrencyPolicy.cs");

        Assert.Contains("ProcessLoadingQueue", patch, StringComparison.Ordinal);
        Assert.Contains("GetConcurrentAssetLoadLimit", patch, StringComparison.Ordinal);
        Assert.Contains("replacements != 1", patch, StringComparison.Ordinal);
        Assert.Contains("VisibleTransitionConcurrentLoadLimit = 8", policy, StringComparison.Ordinal);
        Assert.Contains("VanillaConcurrentLoadLimit = 128", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionAssetPrefetchUsesAnAtomicScopedRetentionCapability()
    {
        string patches = SourceText("Code/Patches/NinjaSlayerTransitionAssetPrefetchPatch.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string session = SourceText("Code/Transition/NinjaSlayerTransitionSession.cs");
        string prefetcher = SourceText("Code/Transition/NinjaSlayerRunAssetPrefetcher.cs");

        Assert.Contains("TransitionAssetPrefetchPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerTransitionAssetRetentionPatch>", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerTransitionMainMenuAssetPrefetchPatch>", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerTransitionEmbarkAssetPrefetchPatch>", groups, StringComparison.Ordinal);
        Assert.Contains("AssetCache.UnloadAssets", patches, StringComparison.Ordinal);
        Assert.Contains("FilterAssetsToUnload", patches, StringComparison.Ordinal);
        Assert.Contains("ClaimForTransition", session, StringComparison.Ordinal);
        Assert.Contains("ReleaseAssetPrefetch", session, StringComparison.Ordinal);
        Assert.Contains("PreloadManager.Cache.CreateSession", prefetcher, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadRunAssets(", prefetcher, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionColdScenePrewarmIsOffscreenAndSharedWithLoadStart()
    {
        string prewarmer = SourceText("Code/Transition/NinjaSlayerTransitionScenePrewarmer.cs");
        string transition = SourceText("Code/Patches/NinjaSlayerTransitionPatch.cs");

        Assert.Contains("NRun.AssetPaths", prewarmer, StringComparison.Ordinal);
        Assert.Contains("NEventRoom.AssetPaths", prewarmer, StringComparison.Ordinal);
        Assert.Contains("NAncientEventLayout.ancientScenePath", prewarmer, StringComparison.Ordinal);
        Assert.Contains("NAncientNameBanner.Create(ModelDb.Event<Neow>())", prewarmer, StringComparison.Ordinal);
        Assert.Contains("TransitionManagedCodePrewarmer.Prepare", prewarmer, StringComparison.Ordinal);
        Assert.Contains("new TransitionScenePrewarmResult", prewarmer, StringComparison.Ordinal);
        Assert.Contains("SubViewport.UpdateMode.Always", prewarmer, StringComparison.Ordinal);
        Assert.Contains("GuiDisableInput = true", prewarmer, StringComparison.Ordinal);
        Assert.DoesNotContain("SubViewportContainer", prewarmer, StringComparison.Ordinal);
        Assert.Contains("AwaitReadyAsync(cancelToken)", transition, StringComparison.Ordinal);
        Assert.Contains("WaitForScenePrewarmAndLoadDelayAsync", transition, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerTransitionRunSceneTracePatch", SourceText("Code/Patches/NinjaSlayerPatchGroups.cs"), StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerTransitionEventSceneTracePatch", SourceText("Code/Patches/NinjaSlayerPatchGroups.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void HighFrequencyContextsUseOwnedScopes()
    {
        string karatePatch = SourceText("Code/Patches/KarateHealthBarPreviewPatch.cs");
        string combo = SourceText("Code/ExternalAnimations/XAttackComboContext.cs");
        string audio = SourceText("Code/ExternalAnimations/XAttackAudioContext.cs");
        string shake = SourceText("Code/Combat/ScreenShakeSuppressionContext.cs");
        string cadence = SourceText("Code/Combat/TornadoFistFinisherCadenceContext.cs");

        Assert.Contains("KaratePreviewScopeRegistry.Replace", karatePatch, StringComparison.Ordinal);
        Assert.Contains("KaratePreviewScopeRegistry.Release", karatePatch, StringComparison.Ordinal);
        Assert.Contains("AsyncLocal<State?>", combo, StringComparison.Ordinal);
        Assert.Contains("AsyncScopeDepth", audio, StringComparison.Ordinal);
        Assert.Contains("AsyncScopeDepth", shake, StringComparison.Ordinal);
        Assert.Contains("AsyncScopeDepth", cadence, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherDeathKickFailureOnlyDisablesPresentation()
    {
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string patches = SourceText("Code/Patches/NinjaSlayerFinisherPatches.cs");
        ClassDeclarationSyntax core = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "FinisherCorePatchGroup");
        ClassDeclarationSyntax presentation = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "FinisherPresentationPatchGroup");
        string compatibility = SourceText("Code/Compatibility/GameCompatibility.Finisher.cs");

        Assert.DoesNotContain("NinjaSlayerFinisherDeathStartPatch", core.ToFullString(), StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerFinisherDeathStartPatch", presentation.ToFullString(), StringComparison.Ordinal);
        Assert.Contains("GetPresentationProbes", compatibility, StringComparison.Ordinal);
        Assert.Contains("NCreature.start-death-animation", compatibility, StringComparison.Ordinal);
        Assert.Contains("FinisherPresentationPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("DeathAnimationTask", patches, StringComparison.Ordinal);
    }

    [Fact]
    public void NinjaSlayerEnemyDeathOwnsTheVanillaTaskAndUsesTheActiveDamageScope()
    {
        string patch = SourceText("Code/Patches/NinjaSlayerDeathAnimPatch.cs");
        string classifier = SourceText("Code/ExternalAnimations/NinjaSlayerDeathClassifier.cs");

        Assert.Contains("__instance.DeathAnimationTask = deathTask", patch, StringComparison.Ordinal);
        Assert.Contains("capture != null && IsValidEnemyDealer(creature, capture.Dealer)", classifier, StringComparison.Ordinal);
        Assert.Contains("previous is { IsCompleted: false }", classifier, StringComparison.Ordinal);
    }

    [Fact]
    public void OutsideCombatAbandonDeathCapturesVisualsBeforeKillAndPlaysAudioAfterward()
    {
        string patches = SourceText("Code/Patches/NinjaSlayerOutsideCombatDeathFeedbackPatch.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");

        Assert.Contains("RunManager.Instance.IsAbandoned", patches, StringComparison.Ordinal);
        Assert.Contains("&& NCombatRoom.Instance?.GetCreatureNode(creature) == null", patches, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerOutsideCombatDeathFeedback.TryMark(creature)", patches, StringComparison.Ordinal);
        Assert.Contains("await original;", patches, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAudio.NinjaSlayerSuicideEvent", patches, StringComparison.Ordinal);
        Assert.Contains("MoveCreaturesToDifferentLayerAndDisableUi", patches, StringComparison.Ordinal);
        Assert.Contains("playSuicideSfx: false", patches, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Creature, Marker>", patches, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerOutsideCombatDeathCapturePatch>", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerOutsideCombatDeathFeedbackPatch>", groups, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectExecutionSynchronizesLogicalDeathHeadFlightAndRagdoll()
    {
        string cinematic = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string deathSession = SourceText("Code/ExternalAnimations/ArchitectDeathPresentationSession.cs");
        string ragdoll = SourceText("Code/ExternalAnimations/ArchitectRagdollDeathAnimation.cs");
        string patch = SourceText("Code/Patches/ArchitectExecutionPatch.cs");

        Assert.Contains("CreatureCmd.Kill(_architectNode.Entity, force: true)", cinematic, StringComparison.Ordinal);
        Assert.Contains("WaitUntilDeathStarts(killTask", cinematic, StringComparison.Ordinal);
        Assert.Contains("HeadFlightSeconds - NinjaSoulLeadSeconds", cinematic, StringComparison.Ordinal);
        Assert.Contains("private const float NinjaSoulLeadSeconds = 1f", cinematic, StringComparison.Ordinal);
        Assert.Contains("public const float DurationSeconds = 1.5f", deathSession, StringComparison.Ordinal);
        Assert.Contains("public const float FallSeconds = 1f", ragdoll, StringComparison.Ordinal);
        Assert.Contains("ResolveLandingCompensation", ragdoll, StringComparison.Ordinal);
        Assert.Contains("__instance.DeathAnimationTask = deathTask", patch, StringComparison.Ordinal);
        Assert.Contains("__result = ArchitectDeathPresentationSession.DurationSeconds", patch, StringComparison.Ordinal);
        Assert.Contains("return false", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectVictoryCleanupSuppressesOnlyTheMarkedNinjaSlayerDeath()
    {
        string cinematic = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string deathPatch = SourceText("Code/Patches/NinjaSlayerDeathAnimPatch.cs");
        string cleanup = SourceText("Code/ExternalAnimations/ArchitectVictoryCleanup.cs");

        int completed = cinematic.IndexOf("_completed = true;", StringComparison.Ordinal);
        int mark = cinematic.IndexOf("ArchitectVictoryCleanup.Mark(_owner)", StringComparison.Ordinal);
        int ready = cinematic.IndexOf("SetLocalPlayerReady()", mark, StringComparison.Ordinal);
        Assert.True(completed >= 0 && mark > completed && ready > mark);
        Assert.DoesNotContain("ArchitectVictoryCleanup.Clear", cinematic, StringComparison.Ordinal);
        Assert.Contains("ArchitectVictoryCleanup.TryConsume(__instance.Entity)", deathPatch, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Creature, Marker>", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void GameCompatibilityIsSplitByCapability()
    {
        string facade = SourceText("Code/Compatibility/GameCompatibility.cs");
        string[] capabilityFiles =
        [
            "GameCompatibility.Finisher.cs",
            "GameCompatibility.Prepared.cs",
            "GameCompatibility.Transition.cs",
            "GameCompatibility.Typography.cs",
            "GameCompatibility.Feedback.cs",
            "GameCompatibility.KarateHealthBar.cs",
            "GameCompatibility.AssetLoading.cs",
            "GameCompatibility.TornadoCadence.cs",
            "GameCompatibility.ReporterPass.cs"
        ];

        Assert.Contains("partial class GameCompatibility", facade, StringComparison.Ordinal);
        foreach (string fileName in capabilityFiles)
        {
            string source = SourceText($"Code/Compatibility/{fileName}");
            Assert.Contains("partial class GameCompatibility", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("static class Finisher", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class Prepared", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class Transition", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class Typography", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class Feedback", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class AssetLoading", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class TornadoCadence", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("static class ReporterPass", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void FeedbackLocalizationMergesIntoBaseSettingsUiTable()
    {
        string patches = SourceText("Code/Patches/NinjaSlayerFeedbackPatches.cs");
        string zhs = File.ReadAllText(Path.Combine(Root, "NinjaSlayer", "localization", "zhs", "settings_ui.json"));
        string eng = File.ReadAllText(Path.Combine(Root, "NinjaSlayer", "localization", "eng", "settings_ui.json"));

        Assert.Contains("private const string LocTable = \"settings_ui\"", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("LocString(\"feedback\"", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"feedback\"", patches, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root, "NinjaSlayer", "localization", "zhs", "feedback.json")));
        Assert.False(File.Exists(Path.Combine(Root, "NinjaSlayer", "localization", "eng", "feedback.json")));

        foreach (string key in new[]
                 {
                     "NINJA_SLAYER_FEEDBACK_DESCRIPTION_PLACEHOLDER",
                     "NINJA_SLAYER_FEEDBACK_CONFIRM_HEADER",
                     "NINJA_SLAYER_FEEDBACK_CONFIRM_BODY",
                     "NINJA_SLAYER_FEEDBACK_CONFIRM_CANCEL",
                     "NINJA_SLAYER_FEEDBACK_CONFIRM_SEND"
                 })
        {
            Assert.Contains(key, zhs, StringComparison.Ordinal);
            Assert.Contains(key, eng, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("NINJA_SLAYER_FEEDBACK_SEND_BUTTON", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("NINJA_SLAYER_FEEDBACK_SEND_BUTTON", zhs, StringComparison.Ordinal);
        Assert.DoesNotContain("NINJA_SLAYER_FEEDBACK_SEND_BUTTON", eng, StringComparison.Ordinal);
    }

    [Fact]
    public void LeafCardsDoNotDependOnFinisherOrchestratorInternals()
    {
        string facadePath = Normalize("Cards/Base/FinisherAttackExtensions.cs");
        SourceDocument[] leafCards = Sources
            .Where(source => source.RelativePath.StartsWith("Cards/", StringComparison.Ordinal)
                && Normalize(source.RelativePath) != facadePath)
            .ToArray();

        Assert.NotEmpty(leafCards);
        foreach (SourceDocument card in leafCards)
        {
            string source = card.Root.ToFullString();
            Assert.DoesNotContain("NinjaSlayerFinisherCinematic", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FinisherAttackSpec", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FinisherForecastInputsUseOneDescriptor()
    {
        string orchestrator = SourceText("Code/ExternalAnimations/NinjaSlayerFinisherCinematic.cs");
        string descriptor = SourceText("Code/ExternalAnimations/FinisherForecastDescriptor.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");
        string frameKey = SourceText("Code/ExternalAnimations/FinisherForecastFrameKey.cs");

        Assert.Contains("CardPlay CardPlay,", orchestrator, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastDescriptor Forecast", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Func<Creature, decimal> Damage", descriptor, StringComparison.Ordinal);
        Assert.Contains("FinisherTargeting Targeting", descriptor, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastDescriptor descriptor = spec.Forecast", forecast, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastDescriptor descriptor = spec.Forecast", frameKey, StringComparison.Ordinal);
    }

    private static string SourceText(string relativePath) => Sources
        .Single(source => source.RelativePath == relativePath)
        .Root
        .ToFullString();

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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

    private static IEnumerable<string> TypeNames(ClassDeclarationSyntax catalog, string fieldName)
    {
        CollectionExpressionSyntax collection = catalog.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == fieldName)
            .Initializer?.Value as CollectionExpressionSyntax
            ?? throw new InvalidOperationException($"{fieldName} must use a collection expression.");
        return collection.Elements
            .OfType<ExpressionElementSyntax>()
            .Select(element => element.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .Select(expression => expression.Type.ToString());
    }

    private static Dictionary<string, string> ReplacementTypes(ClassDeclarationSyntax catalog)
    {
        CollectionExpressionSyntax collection = catalog.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == "Replacements")
            .Initializer?.Value as CollectionExpressionSyntax
            ?? throw new InvalidOperationException("Replacements must use a collection expression.");
        return collection.Elements
            .OfType<ExpressionElementSyntax>()
            .Select(element => element.Expression)
            .OfType<TupleExpressionSyntax>()
            .ToDictionary(
                tuple => ((TypeOfExpressionSyntax)tuple.Arguments[0].Expression).Type.ToString(),
                tuple => ((TypeOfExpressionSyntax)tuple.Arguments[1].Expression).Type.ToString(),
                StringComparer.Ordinal);
    }

    private static IEnumerable<(string Rarity, string Type)> RequiredRewardBuckets(
        VariableDeclaratorSyntax requiredBuckets)
    {
        CollectionExpressionSyntax collection = requiredBuckets.Initializer?.Value as CollectionExpressionSyntax
            ?? throw new InvalidOperationException("RequiredBuckets must use a collection expression.");
        foreach (BaseObjectCreationExpressionSyntax creation in collection.Elements
                     .OfType<ExpressionElementSyntax>()
                     .Select(element => element.Expression)
                     .OfType<BaseObjectCreationExpressionSyntax>())
        {
            SeparatedSyntaxList<ArgumentSyntax> arguments = creation.ArgumentList?.Arguments
                ?? throw new InvalidOperationException("A reward bucket must declare rarity and type arguments.");
            yield return (
                ((MemberAccessExpressionSyntax)arguments[0].Expression).Name.Identifier.Text,
                ((MemberAccessExpressionSyntax)arguments[1].Expression).Name.Identifier.Text);
        }
    }

    private static Dictionary<string, (string Rarity, string Type)> CardRewardMetadata()
    {
        var result = new Dictionary<string, (string Rarity, string Type)>(StringComparer.Ordinal);
        foreach (ClassDeclarationSyntax declaration in Sources
                     .Where(source => source.RelativePath.StartsWith("Cards/", StringComparison.Ordinal))
                     .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()))
        {
            VariableDeclaratorSyntax? cardSpec = declaration.Members.OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables)
                .SingleOrDefault(variable => variable.Identifier.Text == "CardSpec");
            if (cardSpec?.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation
                || creation.ArgumentList?.Arguments.Count < 4)
            {
                continue;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments = creation.ArgumentList!.Arguments;
            result.Add(
                declaration.Identifier.Text,
                (
                    ((MemberAccessExpressionSyntax)arguments[3].Expression).Name.Identifier.Text,
                    ((MemberAccessExpressionSyntax)arguments[2].Expression).Name.Identifier.Text
                ));
        }
        return result;
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
