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
    public void MerchantVisualUsesRequestedShopOffset()
    {
        string profile = SourceText("Content/NinjaSlayerWorldVisualProfile.cs");

        Assert.Contains("BodyPositionX = 0f", profile, StringComparison.Ordinal);
        Assert.Contains("TextureHeight = 500f", profile, StringComparison.Ordinal);
        Assert.Contains("BottomBaselineY = 40f", profile, StringComparison.Ordinal);
        Assert.Contains(
            "BodyPositionY = BottomBaselineY - TextureHeight * BodyScale * 0.5f",
            profile,
            StringComparison.Ordinal);
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
    public void YamotoKokiUsesDedicatedVisualsAudioAndSavedPresentationLifecycle()
    {
        string monster = File.ReadAllText(Path.Combine(Root, "Monsters", "YamotoKokiMonster.cs"));
        string animations = SourceText("Code/ExternalAnimations/YamotoKokiCombatAnimations.cs");
        string dodgeAnimation = SourceText("Code/ExternalAnimations/CombatDodgeAnimation.cs");
        string dodge = SourceText("Code/Patches/EnemyAttackDodgePatch.cs");
        string layout = SourceText("Code/Patches/YamotoKokiAllyLayoutPatch.cs");
        string facing = SourceText("Code/Nodes/YamotoKokiAllyFacingController.cs");
        string facingState = SourceText("Code/ExternalAnimations/NinjaSlayerFacingState.cs");
        string orbit = SourceText("Code/Nodes/YamotoKokiOrigamiMissileOrbitController.cs");
        string missileVisuals = SourceText("Code/Nodes/YamotoKokiOrigamiMissileVisuals.cs");
        string missileVfx = SourceText("Code/Nodes/NYamotoKokiOrigamiMissileVfx.cs");
        string missileBob = SourceText("Code/Nodes/NYamotoKokiOrigamiMissileIdleBob.cs");
        string intents = File.ReadAllText(Path.Combine(Root, "Monsters", "YamotoKokiIntents.cs"));
        string intentAnimation = SourceText("Code/Patches/YamotoKokiIntentAnimationPatch.cs");
        string intentLifecycle = SourceText("Code/Combat/YamotoKokiIntentLifecycle.cs");
        string partyState = SourceText("Code/Combat/YamotoKokiPartyState.cs");
        string restore = SourceText("Code/Patches/YamotoKokiFinishedCombatRestorePatch.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");
        string patchGroups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string missile = File.ReadAllText(Path.Combine(Root, "Monsters", "YamotoKokiOrigamiMissile.cs"));
        string relic = SourceText("Relics/YamotoKokiCuteRelic.cs");
        string eventModel = SourceText("Events/YamotoKokiCuteEvent.cs");
        string audio = SourceText("Content/NinjaSlayerAudio.cs");
        string intentLocalization = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "localization",
            "eng",
            "intents.json"));
        string monsterLocalization = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "localization",
            "eng",
            "monsters.json"));
        string scene = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "scenes",
            "creature_visuals",
            "yamoto_koki.tscn"));
        string missileScene = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "scenes",
            "creature_visuals",
            "yamoto_koki_missile.tscn"));
        string missileSkeletonData = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "animations",
            "monsters",
            "yamoto_koki_missile",
            "yamoto_koki_missile_skel_data.tres"));
        string missileAtlas = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "animations",
            "monsters",
            "yamoto_koki_missile",
            "gas_bomb.spatlas"));
        Assert.Contains("res://NinjaSlayer/scenes/creature_visuals/yamoto_koki.tscn", monster, StringComparison.Ordinal);
        Assert.Contains("protected override string VisualsPath", monster, StringComparison.Ordinal);
        Assert.DoesNotContain("NinjaSlayerAssetProfile.VisualsPath", monster, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildCombatAnimationStateMachine", monster, StringComparison.Ordinal);
        Assert.Contains("scale = Vector2(0.4588, 0.4588)", scene, StringComparison.Ordinal);
        Assert.Contains("scale = Vector2(0.326, 0.326)", scene, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(scene, "position = Vector2(0, -20.625)"));
        Assert.Contains("position = Vector2(-32, -106.975)", scene, StringComparison.Ordinal);
        Assert.Contains("offset_top = -243.24", scene, StringComparison.Ordinal);
        Assert.Contains("offset_bottom = -12.0", scene, StringComparison.Ordinal);
        Assert.Contains("offset_left = -135.56", scene, StringComparison.Ordinal);
        Assert.Contains("offset_right = 71.56", scene, StringComparison.Ordinal);
        Assert.Contains("position = Vector2(0, -127.62)", scene, StringComparison.Ordinal);
        Assert.Contains("position = Vector2(0, -275.08)", scene, StringComparison.Ordinal);
        Assert.Contains("position = Vector2(-15.21, -231.23)", scene, StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/images/monsters/yamoto_koki.png", scene, StringComparison.Ordinal);

        Assert.Contains("case \"Dodge\":", animations, StringComparison.Ordinal);
        Assert.Contains("CombatDodgeAnimation.PlayImmediate(creature)", animations, StringComparison.Ordinal);
        Assert.Contains("case \"SlowAttack\":", animations, StringComparison.Ordinal);
        Assert.Contains("GroundOffsetFromPivot = 8.625f", animations, StringComparison.Ordinal);
        Assert.Contains("new(65.495f, 3.137f)", animations, StringComparison.Ordinal);
        Assert.Contains("PlaySummon", monster, StringComparison.Ordinal);
        Assert.Contains("SummonMissileCount = 2", monster, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiFastAttackEvent", monster, StringComparison.Ordinal);
        Assert.Contains("BeginSpawnBatch(owner, SummonMissileCount)", monster, StringComparison.Ordinal);
        Assert.Contains("EndSpawnBatch(owner)", monster, StringComparison.Ordinal);
        Assert.Contains("target.Player ?? target.PetOwner", dodge, StringComparison.Ordinal);
        Assert.Contains("PreImpactLeadSeconds = 0.3f", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("OutwardSeconds = 0.08f", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("PostImpactHoldSeconds = 0.2f", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("ReturnSeconds = 0.14f", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("Tween.EaseType.In", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("Tween.TransitionType.Cubic", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("CombatDodgeAnimation.NotifyImpact(yamotoKoki)", dodge, StringComparison.Ordinal);
        Assert.Contains("owner.GetPower<EvasionPower>()", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("CombatDodgeAnimation.NotifyImpact(owner)", dodge, StringComparison.Ordinal);
        Assert.Contains("AttackIntentPreviewContext.Enter()", dodge, StringComparison.Ordinal);
        Assert.Contains("nameof(AttackCommand.Execute)", dodge, StringComparison.Ordinal);
        Assert.Contains("nameof(CreatureCmd.TriggerAnim)", dodge, StringComparison.Ordinal);
        Assert.Contains("override bool TryModifyPowerAmountReceived", monster, StringComparison.Ordinal);
        Assert.Contains("applier.Side == target.Side", monster, StringComparison.Ordinal);
        Assert.Contains("canonicalPower.GetTypeForAmount(amount) != PowerType.Debuff", monster, StringComparison.Ordinal);
        Assert.Contains("modifiedAmount = 0m", monster, StringComparison.Ordinal);
        Assert.Contains("nameof(NCombatRoom.AddCreature)", layout, StringComparison.Ordinal);
        Assert.Contains("nameof(NCombatRoom.RemoveCreatureNode)", layout, StringComparison.Ordinal);
        Assert.Contains("new Slot(yamotoKoki, playerSlot.Width)", layout, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiAllyFacingController.Ensure(room).SyncNow()", layout, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiMonster or YamotoKokiOrigamiMissile", facing, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerFacingState.ResolveFacingLeft(ownerNode)", facing, StringComparison.Ordinal);
        Assert.Contains("ownerNode.Entity.CombatState?.HittableEnemies", facing, StringComparison.Ordinal);
        Assert.Contains("enemy.HasPower<BackAttackLeftPower>()", facing, StringComparison.Ordinal);
        Assert.Contains("enemy.HasPower<BackAttackRightPower>()", facing, StringComparison.Ordinal);
        Assert.Contains("hasEnemyOnLeft && hasEnemyOnRight", facing, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiFacingPolicy.ResolveCompanionFacing", facing, StringComparison.Ordinal);
        Assert.Contains("FacingScaleMath.WithFacing(body.Scale.X, faceLeft)", facing, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiAllyFacingController.SyncCurrentRoom()", facingState, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Creature, FacingSnapshot>", facingState, StringComparison.Ordinal);
        Assert.Contains("PersistentFacing.TryGetValue", facingState, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiOrigamiMissile", orbit, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiOrigamiMissile { IsLaunching: false }", orbit, StringComparison.Ordinal);
        Assert.DoesNotContain("SetScaleAndHue", orbit, StringComparison.Ordinal);
        Assert.Contains("%DamageAmount", orbit, StringComparison.Ordinal);
        Assert.Contains("GetExplodeDamage()", orbit, StringComparison.Ordinal);
        Assert.Contains("damageAmount == null", orbit, StringComparison.Ordinal);
        Assert.Contains("!GodotObject.IsInstanceValid(damageAmount)", orbit, StringComparison.Ordinal);
        Assert.Contains("!missileNode.Visuals.IsAncestorOf(damageAmount)", orbit, StringComparison.Ordinal);
        Assert.Contains("DamageText", orbit, StringComparison.Ordinal);
        Assert.Contains("_reservedMissileCounts", orbit, StringComparison.Ordinal);
        Assert.Contains("BeginSpawnBatch", orbit, StringComparison.Ordinal);
        Assert.Contains("int layoutCount = Math.Max", orbit, StringComparison.Ordinal);
        Assert.Contains("GetOffset(layoutCount, i)", orbit, StringComparison.Ordinal);
        Assert.Contains("LayoutTweenSeconds = 0.45f", orbit, StringComparison.Ordinal);
        Assert.Contains("Tween.EaseType.InOut", orbit, StringComparison.Ordinal);
        Assert.Contains("Tween.TransitionType.Sine", orbit, StringComparison.Ordinal);
        Assert.Contains("_layoutTweens.ContainsKey(missile)", orbit, StringComparison.Ordinal);
        Assert.Contains("StartLayoutTween(missileNode)", orbit, StringComparison.Ordinal);
        Assert.Contains("EarliestExplosionTurn", missile, StringComparison.Ordinal);
        Assert.Contains("ExplodeDamage = 4", missile, StringComparison.Ordinal);
        Assert.Contains("ValueProp.Move | ValueProp.Unpowered", missile, StringComparison.Ordinal);
        Assert.Contains("IsHealthBarVisible => false", missile, StringComparison.Ordinal);
        Assert.Contains("ShouldAllowHitting", missile, StringComparison.Ordinal);
        Assert.Contains("TryCreateCreatureVisuals()", missile, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiOrigamiMissileVisuals.Create()", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("SetupSkins", missile, StringComparison.Ordinal);
        Assert.Contains("StopAndReset()", missile, StringComparison.Ordinal);
        Assert.Contains("public bool IsLaunching", missile, StringComparison.Ordinal);
        Assert.Contains("LaunchAtTarget(target)", missile, StringComparison.Ordinal);
        Assert.Contains("new NodePath(\"global_position\")", missile, StringComparison.Ordinal);
        Assert.Contains("Tween.EaseType.In", missile, StringComparison.Ordinal);
        Assert.Contains("Tween.TransitionType.Cubic", missile, StringComparison.Ordinal);
        Assert.Contains("damageAmount.Visible = false", missile, StringComparison.Ordinal);
        Assert.Contains("new(ExplodeMoveId, ExplodeMove)", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("DeathBlowIntent", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("PerformIntent()", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("NGaseousImpactVfx", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("#402f45", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("DieForYouPower", missile, StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/scenes/creature_visuals/yamoto_koki_missile.tscn", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>", missileVisuals, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelDb.Monster<GasBomb>()", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("SetScaleAndHue(MissileScale, 0f)", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("new NYamotoKokiOrigamiMissileIdleBob", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("UniqueNameInOwner = true", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("VBoxContainer labelContainer", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("Name = \"DamageLabelContainer\"", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("labelContainer.AddChild(damageAmount)", missileVisuals, StringComparison.Ordinal);
        Assert.DoesNotContain("ZIndex = 20", missileVisuals, StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/animations/monsters/yamoto_koki_missile", missileScene, StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/images/relics/YamotoKokiCuteRelic.png", missileScene, StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/images/vfx/yamoto_koki_missile", missileScene, StringComparison.Ordinal);
        Assert.Contains("position = Vector2(-2.2, 0.55)", missileScene, StringComparison.Ordinal);
        Assert.Contains("scale = Vector2(1.1, 1.1)", missileScene, StringComparison.Ordinal);
        Assert.Contains("flip_h = true", missileScene, StringComparison.Ordinal);
        Assert.Contains("name=\"OrigamiGlow\"", missileScene.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/images/vfx/common/common_glow.png", missileScene, StringComparison.Ordinal);
        Assert.Contains("scale = Vector2(2.9, 2.9)", missileScene, StringComparison.Ordinal);
        Assert.Contains("modulate = Color(0.91, 0.61, 0.94, 0.82)", missileScene, StringComparison.Ordinal);
        Assert.True(
            missileScene.IndexOf("name=\"PaperKraneCore\"", StringComparison.Ordinal)
            < missileScene.IndexOf("name=\"OrigamiGlow\"", StringComparison.Ordinal));
        Assert.Contains("DotParticles", missileScene, StringComparison.Ordinal);
        Assert.Contains("BitParticles", missileScene, StringComparison.Ordinal);
        Assert.DoesNotContain("PuffParticles", missileScene, StringComparison.Ordinal);
        Assert.DoesNotContain("ExplodePuffParticles", missileScene, StringComparison.Ordinal);
        Assert.DoesNotContain("res://scenes/", missileScene, StringComparison.Ordinal);
        Assert.DoesNotContain("res://animations/", missileScene, StringComparison.Ordinal);
        Assert.DoesNotContain("res://images/", missileScene, StringComparison.Ordinal);
        Assert.Contains("gas_bomb.spatlas", missileSkeletonData, StringComparison.Ordinal);
        Assert.Contains("gas_bomb.spskel", missileSkeletonData, StringComparison.Ordinal);
        Assert.Contains(
            "res://NinjaSlayer/animations/monsters/yamoto_koki_missile/gas_bomb.atlas",
            missileAtlas,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "res://animations/monsters/gas_bomb",
            missileAtlas,
            StringComparison.Ordinal);
        Assert.Contains("_megaSprite = new MegaSprite(body)", missileVfx, StringComparison.Ordinal);
        Assert.Contains("_bitParticles.Restart()", missileVfx, StringComparison.Ordinal);
        Assert.Contains("case \"burst\":", missileVfx, StringComparison.Ordinal);
        Assert.Contains("PaperKraneCore", missileVfx, StringComparison.Ordinal);
        Assert.Contains("_paperKraneCore.Hide()", missileVfx, StringComparison.Ordinal);
        Assert.Contains("OrigamiGlow", missileVfx, StringComparison.Ordinal);
        Assert.Contains("_origamiGlow.Hide()", missileVfx, StringComparison.Ordinal);
        Assert.DoesNotContain("PuffParticles", missileVfx, StringComparison.Ordinal);
        Assert.Contains("Amplitude = 10f", missileBob, StringComparison.Ordinal);
        Assert.Contains("PeriodSeconds = 2f", missileBob, StringComparison.Ordinal);
        Assert.Contains("Mathf.Sin", missileBob, StringComparison.Ordinal);
        Assert.Contains("_body!.Position = _bodyBasePosition + offset", missileBob, StringComparison.Ordinal);
        Assert.Contains(
            "_damageLabelContainer!.Position = _damageLabelBasePosition + offset",
            missileBob,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetCreatureNode()", missileBob, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            Root,
            "NinjaSlayer",
            "scenes",
            "creature_visuals",
            "yamoto_koki_missile.tscn")));
        Assert.True(File.Exists(Path.Combine(
            Root,
            "NinjaSlayer",
            "animations",
            "monsters",
            "yamoto_koki_missile",
            "gas_bomb.skel")));
        Assert.True(File.Exists(Path.Combine(
            Root,
            "NinjaSlayer",
            "animations",
            "monsters",
            "yamoto_koki_missile",
            "gas_bomb.spskel")));
        Assert.True(File.Exists(Path.Combine(
            Root,
            "NinjaSlayer",
            "animations",
            "monsters",
            "yamoto_koki_missile",
            "gas_bomb.spatlas")));
        Assert.True(File.Exists(Path.Combine(
            Root,
            "NinjaSlayer",
            "animations",
            "monsters",
            "yamoto_koki_missile",
            "gas_bomb.png")));
        Assert.InRange(
            new FileInfo(Path.Combine(
                Root,
                "NinjaSlayer",
                "animations",
                "monsters",
                "yamoto_koki_missile",
                "gas_bomb_2.png")).Length,
            1,
            256);
        Assert.Contains("class YamotoKokiSummonIntent", intents, StringComparison.Ordinal);
        Assert.Contains("class YamotoKokiIaiSlashIntent", intents, StringComparison.Ordinal);
        Assert.Contains("yamoto_koki_summon.png", intents, StringComparison.Ordinal);
        Assert.Contains("yamoto_koki_iai_slash.png", intents, StringComparison.Ordinal);
        Assert.Contains("IntentAnimData.summon", intents, StringComparison.Ordinal);
        Assert.Contains("IntentAnimData.attack1", intents, StringComparison.Ordinal);
        Assert.DoesNotContain("ninja_slayer_yamoto_koki_summon", intents, StringComparison.Ordinal);
        Assert.DoesNotContain("ninja_slayer_yamoto_koki_iai_slash", intents, StringComparison.Ordinal);
        Assert.DoesNotContain("Shader", intents, StringComparison.Ordinal);
        Assert.Contains("nameof(NIntent.UpdateIntent)", intentAnimation, StringComparison.Ordinal);
        Assert.Contains("holder.MoveChild(customIcon", intentAnimation, StringComparison.Ordinal);
        Assert.Contains("vanillaIcon.Hide()", intentAnimation, StringComparison.Ordinal);
        Assert.DoesNotContain("IntentAnimData.GetAnimationFrame", intentAnimation, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(NIntent._Process)", intentAnimation, StringComparison.Ordinal);
        Assert.Equal((72, 72), ReadPngDimensions("NinjaSlayer/images/intents/yamoto_koki_summon.png"));
        Assert.Equal((72, 72), ReadPngDimensions("NinjaSlayer/images/intents/yamoto_koki_iai_slash.png"));
        Assert.Contains("This ally intends to summon", intentLocalization, StringComparison.Ordinal);
        Assert.Contains("This ally intends to", intentLocalization, StringComparison.Ordinal);
        Assert.Contains("Origami Missile", monsterLocalization, StringComparison.Ordinal);
        Assert.Contains("SUMMON_ORIGAMI_MISSILE", monster, StringComparison.Ordinal);
        Assert.DoesNotContain("YamotoKokiGasBomb", string.Join('\n', Sources.Select(source => source.Root.ToFullString())), StringComparison.Ordinal);
        Assert.DoesNotContain("YamotoKokiIntentFramePatch", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<YamotoKokiIntentUpdatePatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<YamotoKokiIntentGenerationPatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<YamotoKokiLastEnemyDeathIntentPatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<YamotoKokiOrigamiMissileHitSparkPatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<YamotoKokiFinishedCombatRestorePatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerEnemyAttackVfxBaselinePatch>()", patchGroups, StringComparison.Ordinal);
        Assert.DoesNotContain("YamotoKokiIntentFrameCountPatch", patchGroups, StringComparison.Ordinal);
        Assert.DoesNotContain("YamotoKokiIntentFramePathPatch", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<EnemyAttackDodgeScopePatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<EnemyAttackDodgeAnimationPatch>()", patchGroups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<AttackIntentDamagePreviewPatch>()", patchGroups, StringComparison.Ordinal);

        Assert.Contains("NinjaSlayerRelicAssets.For(this)", relic, StringComparison.Ordinal);
        Assert.Equal((128, 128), ReadPngDimensions("NinjaSlayer/images/relics/YamotoKokiCuteRelic.png"));
        Assert.Equal((256, 256), ReadPngDimensions("NinjaSlayer/images/relics/YamotoKokiCuteRelic_large.png"));
        Assert.Equal((128, 128), ReadPngDimensions("NinjaSlayer/images/relics/YamotoKokiCuteRelic_outline.png"));
        Assert.Contains("public bool HasPlayedEntrance", relic, StringComparison.Ordinal);
        Assert.Contains("public bool HasPlayedFarewell", relic, StringComparison.Ordinal);
        Assert.Contains("AfterCombatEnd(CombatRoom room)", relic, StringComparison.Ordinal);
        Assert.Contains("Status = IsUsedUp ? RelicStatus.Disabled : RelicStatus.Normal", relic, StringComparison.Ordinal);
        Assert.True(CountOccurrences(relic, "YamotoKokiPartyState.IsController(this)") >= 2);
        Assert.Contains("GetActiveRelicCount", partyState, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(IsActive)", partyState, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiRelicLifetimePolicy.IsActive", partyState, StringComparison.Ordinal);
        Assert.Contains("ScaleForActiveRelics", monster, StringComparison.Ordinal);
        Assert.Contains("ScaleForActiveRelics", missile, StringComparison.Ordinal);
        Assert.DoesNotContain("RunState.Players.Count", monster, StringComparison.Ordinal);
        Assert.DoesNotContain("RunState.Players.Count", missile, StringComparison.Ordinal);
        Assert.Contains("TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayEntrance", relic, StringComparison.Ordinal);
        Assert.Contains("PlayFarewell(yamotoKoki)", relic, StringComparison.Ordinal);
        Assert.Contains("TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayFarewell", relic, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIntentLifecycle.BeginCombat", relic, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIntentLifecycle.Invalidate", relic, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIntentGeneration generation", relic, StringComparison.Ordinal);
        Assert.Contains("EvaluateYamotoKokiNextTurn", relic, StringComparison.Ordinal);
        Assert.Contains("missile.CanExplodeOnTurn(nextTurn)", relic, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastBranchQuantifier.AnyBranch", forecast, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Creature, GenerationState>", intentLifecycle, StringComparison.Ordinal);
        Assert.Contains("container.Visible = false", intentLifecycle, StringComparison.Ordinal);
        Assert.Contains("container.RemoveChild(child)", intentLifecycle, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIntentLifecycle.InvalidateCombat", intentAnimation, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIntentLifecycle.IsActive", intentAnimation, StringComparison.Ordinal);
        Assert.Contains("if (!shouldRemove", intentAnimation, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAudio.YamotoKokiByeEvent", relic, StringComparison.Ordinal);
        Assert.DoesNotContain("NinjaSlayerAudio.YamotoKokiByeEvent", animations, StringComparison.Ordinal);
        Assert.Contains("missile.CanExplodeOnTurn(turnNumber)", relic, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(
                relic,
                "NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiFastAttackEvent)"));
        Assert.Contains("DeathAnimationTask", relic, StringComparison.Ordinal);
        Assert.Contains("MissileAttackIntervalSeconds = 0.2f", relic, StringComparison.Ordinal);
        Assert.Contains("StartMissileAttacksStaggered(armedMissiles)", relic, StringComparison.Ordinal);
        Assert.Contains("Cmd.Wait(MissileAttackIntervalSeconds)", relic, StringComparison.Ordinal);
        Assert.Contains("missile.ExecuteExplosion(armedMissile)", relic, StringComparison.Ordinal);
        Assert.Contains("CompleteMissileOperations(missileOperations)", relic, StringComparison.Ordinal);
        Assert.Contains("MoveAfterMissilesDelaySeconds = 0.2f", relic, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(relic, "Cmd.Wait(MoveAfterMissilesDelaySeconds)"));
        Assert.DoesNotContain("summonDuringMissiles", relic, StringComparison.Ordinal);
        Assert.DoesNotContain("PerformScheduledMoveAfterMissileDelay", relic, StringComparison.Ordinal);
        Assert.Contains("await StartMissileAttacksStaggered(armedMissiles)", relic, StringComparison.Ordinal);
        Assert.Contains("WaitForMissileResolutions(missileOperations)", relic, StringComparison.Ordinal);
        Assert.True(
            relic.IndexOf("await Task.WhenAll(postLaunchDelay, missileResolution)", StringComparison.Ordinal)
            < relic.IndexOf("Task moveTask = PerformScheduledMove", StringComparison.Ordinal));
        Assert.Contains(
            "Task.WhenAll(CompleteMissileOperations(missileOperations), moveTask)",
            relic,
            StringComparison.Ordinal);
        Assert.Contains("releasing the player turn instead of blocking card input", relic, StringComparison.Ordinal);
        Assert.Contains("overlapIntentPresentation: true", relic, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(intentTask, moveTask)", relic, StringComparison.Ordinal);
        Assert.Contains("for (int i = 0; i < SummonMissileCount; i++)", monster, StringComparison.Ordinal);
        Assert.Contains("await YamotoKokiCombatAnimations.PlaySummon", monster, StringComparison.Ordinal);
        Assert.True(
            monster.IndexOf("for (int i = 0; i < SummonMissileCount; i++)", StringComparison.Ordinal)
            < monster.IndexOf("await YamotoKokiCombatAnimations.PlaySummon", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(monster, "PlayerCmd.AddPet<YamotoKokiOrigamiMissile>"));
        Assert.Equal(1, CountOccurrences(relic, "OnMovePerformed(scheduledMove)"));
        Assert.Contains("YamotoKokiPartyState.FindLivingCompanion(Owner.RunState)", relic, StringComparison.Ordinal);
        int beforeCombatStart = relic.IndexOf("public override async Task BeforeCombatStart", StringComparison.Ordinal);
        int afterCombatEnd = relic.IndexOf("public override Task AfterCombatEnd", StringComparison.Ordinal);
        int combatConsumed = relic.IndexOf("YamotoKokiRelicLifetimePolicy.CompleteCombat", StringComparison.Ordinal);
        Assert.True(beforeCombatStart >= 0 && afterCombatEnd > beforeCombatStart);
        Assert.True(combatConsumed > afterCombatEnd);
        Assert.Contains("YamotoKokiMonster.SummonMissileMoveId", relic, StringComparison.Ordinal);
        Assert.DoesNotContain("BeforeSideTurnStart", relic, StringComparison.Ordinal);
        Assert.Contains("AfterEventStarted()", eventModel, StringComparison.Ordinal);
        Assert.Contains("owner.Relics.All(relic => relic is not YamotoKokiCuteRelic)", eventModel, StringComparison.Ordinal);
        Assert.Contains("RelicCmd.Obtain<YamotoKokiCuteRelic>(owner)", eventModel, StringComparison.Ordinal);
        Assert.DoesNotContain("partyAlreadyHasRelic", eventModel, StringComparison.Ordinal);
        Assert.Contains("MapPointType.Treasure", eventModel, StringComparison.Ordinal);
        Assert.Contains("1 => passedFixedTreasure", eventModel, StringComparison.Ordinal);
        Assert.Contains("2 => !passedFixedTreasure", eventModel, StringComparison.Ordinal);
        Assert.Contains("CombatRoomMode.FinishedCombat", restore, StringComparison.Ordinal);
        Assert.Contains("new Creature(model, CombatSide.Player, null)", restore, StringComparison.Ordinal);
        Assert.Contains("PetOwner = controller.Owner", restore, StringComparison.Ordinal);
        Assert.Contains("room.AddCreature(companion)", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerCmd.AddPet", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateCreature", restore, StringComparison.Ordinal);

        foreach (string eventName in new[]
                 {
                     "yamoto_koki_bye",
                     "yamoto_koki_event",
                     "yamoto_koki_fast_attack",
                     "yamoto_koki_go"
                 })
        {
            Assert.Contains(eventName, audio, StringComparison.Ordinal);
        }
        Assert.Contains("defect/defect_dark_channel", audio, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiMissileSummonEvent", monster, StringComparison.Ordinal);
    }

    [Fact]
    public void YamotoKokiIaiUsesSelfContainedRecoloredGrandFinaleVfx()
    {
        string monster = File.ReadAllText(Path.Combine(Root, "Monsters", "YamotoKokiMonster.cs"));
        string combatVfx = SourceText("Content/NinjaSlayerCombatVfx.cs");
        string petals = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "scenes",
            "vfx",
            "yamoto_koki_iai",
            "grand_finale",
            "vfx_grand_finale_petals.tscn"));
        string impact = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "scenes",
            "vfx",
            "yamoto_koki_iai",
            "vfx_grand_finale_impact.tscn"));
        string ribbon = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "materials",
            "vfx",
            "yamoto_koki_iai",
            "ribbon_flipbook",
            "vfx_ribbon_flipbook_1_2_silent.tres"));

        Assert.Contains("PlayYamotoKokiIaiPetals(Creature)", monster, StringComparison.Ordinal);
        Assert.Contains("PlayYamotoKokiIaiImpact(enemies)", monster, StringComparison.Ordinal);
        Assert.Contains("PreloadYamotoKokiIaiFeedback()", monster, StringComparison.Ordinal);
        Assert.Contains("NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack)", combatVfx, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(combatVfx, "NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack)"));
        Assert.True(
            combatVfx.IndexOf("if (playedImpact)", StringComparison.Ordinal)
            < combatVfx.LastIndexOf(
                "NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack)",
                StringComparison.Ordinal));
        Assert.DoesNotContain("giantHorizontalSlashPath", monster, StringComparison.Ordinal);
        Assert.Contains("0.9098039, 0.6117647, 0.9372549", petals, StringComparison.Ordinal);
        Assert.Contains("0.9882353, 0.89411765, 0.99215686", impact, StringComparison.Ordinal);
        Assert.Contains("0.9882353, 0.89411765, 0.99215686", ribbon, StringComparison.Ordinal);

        string[] roots =
        [
            "NinjaSlayer/scenes/vfx/yamoto_koki_iai",
            "NinjaSlayer/materials/vfx/yamoto_koki_iai",
            "NinjaSlayer/shaders/vfx/yamoto_koki_iai"
        ];
        foreach (string root in roots)
        {
            foreach (string file in Directory.EnumerateFiles(
                         Path.Combine(Root, root.Replace('/', Path.DirectorySeparatorChar)),
                         "*",
                         SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".tscn" or ".tres" or ".gdshader" or ".gdshaderinc"))
                {
                    continue;
                }

                string content = File.ReadAllText(file);
                Assert.DoesNotContain("res://scenes/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://materials/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://images/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://shaders/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://src/Core/", content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void OrigamiMissileUsesOneScopedProjectLocalPinkHitSpark()
    {
        string missile = File.ReadAllText(Path.Combine(Root, "Monsters", "YamotoKokiOrigamiMissile.cs"));
        string scope = SourceText("Code/ExternalAnimations/YamotoKokiOrigamiMissileHitSparkScope.cs");
        string node = SourceText("Code/Nodes/NYamotoKokiOrigamiMissileHitSparkVfx.cs");
        string patches = SourceText("Code/Patches/NinjaSlayerFinisherPatches.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string scene = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "scenes",
            "vfx",
            "yamoto_koki_missile_hit_spark",
            "hit_spark_vfx.tscn"));

        Assert.Contains("YamotoKokiOrigamiMissileHitSparkScope.Enter(target)", missile, StringComparison.Ordinal);
        Assert.Contains("frame.Consumed = true", scope, StringComparison.Ordinal);
        Assert.Contains("frame.Target != target", scope, StringComparison.Ordinal);
        Assert.Contains("NYamotoKokiOrigamiMissileHitSparkVfx.Create", scope, StringComparison.Ordinal);
        Assert.Contains("res://NinjaSlayer/scenes/vfx/yamoto_koki_missile_hit_spark", node, StringComparison.Ordinal);
        Assert.Contains("GetNodeOrNull<GpuParticles2D>(nodePath)", node, StringComparison.Ordinal);
        Assert.DoesNotContain("[Export]", node, StringComparison.Ordinal);
        Assert.Contains("nameof(NHitSparkVfx.Create)", patches, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<YamotoKokiOrigamiMissileHitSparkPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("0.909804, 0.611765, 0.937255", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("_missileParticles", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("_missileSpecks", scene, StringComparison.Ordinal);

        string[] roots =
        [
            "NinjaSlayer/scenes/vfx/yamoto_koki_missile_hit_spark",
            "NinjaSlayer/materials/vfx/yamoto_koki_missile_hit_spark",
            "NinjaSlayer/shaders/vfx/yamoto_koki_missile_hit_spark"
        ];
        foreach (string root in roots)
        {
            foreach (string file in Directory.EnumerateFiles(
                         Path.Combine(Root, root.Replace('/', Path.DirectorySeparatorChar)),
                         "*",
                         SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".tscn" or ".tres" or ".gdshader" or ".gdshaderinc"))
                {
                    continue;
                }

                string content = File.ReadAllText(file);
                Assert.DoesNotContain("res://scenes/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://materials/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://images/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://shaders/vfx/", content, StringComparison.Ordinal);
                Assert.DoesNotContain("res://src/Core/", content, StringComparison.Ordinal);
            }
        }
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
        Assert.DoesNotContain(
            "FinisherSessionRegistry.HasRegisteredSession()",
            orchestrator,
            StringComparison.Ordinal);
        Assert.Contains("class FinisherSession", session, StringComparison.Ordinal);
        Assert.Contains("static class FinisherSessionRegistry", registry, StringComparison.Ordinal);
        Assert.Contains("HasRegisteredSessionForCombat", registry, StringComparison.Ordinal);
        Assert.Contains("FinisherCompletionMode.ReleaseOnly", registry, StringComparison.Ordinal);
        Assert.Contains(
            "FinisherSessionRegistry.HasRegisteredSessionForCombat(combatState, room)",
            eligibility,
            StringComparison.Ordinal);
        Assert.Contains("static class FinisherEligibilityService", eligibility, StringComparison.Ordinal);
        Assert.Contains("static class FinisherProtectionService", protection, StringComparison.Ordinal);
        Assert.Contains("static class FinisherCleanupService", cleanup, StringComparison.Ordinal);
        Assert.Contains("static class FinisherForecast", forecast, StringComparison.Ordinal);
    }

    [Fact]
    public void CinematicCameraLeaseCannotSurviveCombatRoomReplacement()
    {
        string lease = SourceText(
            "Code/ExternalAnimations/CombatCinematicCameraLease.cs");

        Assert.Contains("_room.TreeExiting += OnRoomTreeExiting", lease, StringComparison.Ordinal);
        Assert.Contains("_room.TreeExiting -= OnRoomTreeExiting", lease, StringComparison.Ordinal);
        Assert.Contains("_active.IsCurrentRoomLease()", lease, StringComparison.Ordinal);
        Assert.Contains("_active.Dispose()", lease, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(NCombatRoom.Instance, _room)", lease, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherCardVisualMonitorStaysIdleUntilItsDrainStarts()
    {
        string suppression = SourceText("Code/ExternalAnimations/FinisherCardVisualSuppression.cs");
        int addChild = suppression.IndexOf("room.AddChild(_monitor)", StringComparison.Ordinal);
        int disableProcess = suppression.IndexOf("_monitor.SetProcess(false)", StringComparison.Ordinal);
        int enableProcess = suppression.IndexOf("_monitor.SetProcess(true)", StringComparison.Ordinal);

        Assert.True(addChild >= 0);
        Assert.True(disableProcess > addChild);
        Assert.True(enableProcess > disableProcess);
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
    public void TransitionSmoothingPreservesMediaTimingAndReleasesLoadingAfterViewTakeover()
    {
        string audio = SourceText("Content/NinjaSlayerAudio.cs");
        string smoothing = SourceText("Code/Transition/NinjaSlayerTransitionLoadSmoothing.cs");
        string session = SourceText("Code/Transition/NinjaSlayerTransitionSession.cs");
        string overlay = SourceText("Code/Nodes/NinjaSlayerTransitionOverlay.cs");
        string transition = SourceText("Code/Patches/NinjaSlayerTransitionPatch.cs");

        Assert.Contains("TransitionVisualSeconds = 2f", audio, StringComparison.Ordinal);
        Assert.Contains("EmbarkLoadStartDelaySeconds = 0.2f", audio, StringComparison.Ordinal);
        Assert.Contains("SaveLoadStartDelaySeconds = 0.6f", audio, StringComparison.Ordinal);
        Assert.Contains("TransitionPresentationEnabled", transition, StringComparison.Ordinal);
        Assert.Contains("WaitForViewReadyAndLoadDelayAsync", transition, StringComparison.Ordinal);
        Assert.Contains("session.WaitForViewReadyAsync", transition, StringComparison.Ordinal);
        Assert.Contains("WaitForLoadDelayAsync", transition, StringComparison.Ordinal);
        Assert.DoesNotContain("AwaitReadyAsync", transition, StringComparison.Ordinal);
        Assert.Contains("_viewReadiness.TryMarkReady()", session, StringComparison.Ordinal);
        Assert.Contains("_viewReadiness.TryMarkUnavailable()", session, StringComparison.Ordinal);
        Assert.Contains("GCCollectionMode.Optimized", smoothing, StringComparison.Ordinal);
        Assert.Contains("blocking: false", smoothing, StringComparison.Ordinal);
        Assert.Contains("TransitionGcRequestExecutor.Execute", smoothing, StringComparison.Ordinal);
        Assert.DoesNotContain("EndAnimationAndCollectDeferred", smoothing, StringComparison.Ordinal);
        Assert.Contains("EndAnimationSmoothing", session, StringComparison.Ordinal);
        Assert.Contains("CompleteLoadSmoothing", session, StringComparison.Ordinal);
        Assert.Contains("RecordFrame(delta, videoPosition)", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionVideoPreloadCachesOnlyTheResource()
    {
        string overlay = SourceText("Code/Nodes/NinjaSlayerTransitionOverlay.cs");
        string video = SourceText("Code/Transition/NinjaSlayerTransitionVideo.cs");
        string preloadPatch = SourceText("Code/Patches/NinjaSlayerTransitionPreloadPatch.cs");
        string transition = SourceText("Code/Patches/NinjaSlayerTransitionPatch.cs");

        Assert.Contains("typeof(NMainMenu)", preloadPatch, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerTransitionVideo.BeginPreload();", preloadPatch, StringComparison.Ordinal);
        Assert.Contains("ResourceLoader.Load<VideoStream>", video, StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceLoader.LoadThreaded", video, StringComparison.Ordinal);
        Assert.DoesNotContain("SubViewport", preloadPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("SeekPrimer", preloadPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("TakeForPlayback", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("AwaitReadyAsync", transition, StringComparison.Ordinal);
        Assert.DoesNotContain("Prewarm", overlay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransitionPlaybackPreservesNaturalDecoderOrder()
    {
        string overlay = SourceText("Code/Nodes/NinjaSlayerTransitionOverlay.cs");
        string bossGreeting = SourceText("Code/ExternalAnimations/BossGreetingCinematic.cs");

        Assert.Contains("PlaybackTimeoutPaddingSeconds = 1f", overlay, StringComparison.Ordinal);
        Assert.Contains("elapsed += await this.AwaitProcessFrame", overlay, StringComparison.Ordinal);
        Assert.Contains("while (videoPlayer.IsPlaying() && elapsed < timeout)", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("videoPlayer.StreamPosition =", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("TransitionFrameDropClock", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Prewarm", overlay, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TransitionFrameDropClock", bossGreeting, StringComparison.Ordinal);
    }

    [Fact]
    public void BossGreetingFocusStaysInsideTheCombatScene()
    {
        string greeting = SourceText("Code/ExternalAnimations/BossGreetingCinematic.cs");
        string camera = SourceText("Code/ExternalAnimations/CombatCinematicCameraLease.cs");

        Assert.Contains("public Vector2 ClampPosition", camera, StringComparison.Ordinal);
        Assert.Contains("GetCameraCenter(position, scale, screenCenter)", camera, StringComparison.Ordinal);
        Assert.Contains(
            "GetCameraPosition(ClampTarget(center, scale), scale, screenCenter)",
            camera,
            StringComparison.Ordinal);
        Assert.Contains(
            "TweenCameraToClamped(cameraPosition, targetZoom, BossCameraMoveSeconds)",
            greeting,
            StringComparison.Ordinal);
        Assert.Contains(
            "TweenCameraToClamped(bossFocus, targetZoom, BossCameraMoveSeconds)",
            greeting,
            StringComparison.Ordinal);
        Assert.Contains("clampToScene: true", greeting, StringComparison.Ordinal);
        Assert.Contains("_camera.ClampPosition(position, scale)", greeting, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TweenCameraTo(cameraPosition, targetZoom, BossCameraMoveSeconds)",
            greeting,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TweenCameraTo(bossFocus, targetZoom, BossCameraMoveSeconds)",
            greeting,
            StringComparison.Ordinal);
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
    public void TransitionDoesNotQueueCustomRunResourcePrefetches()
    {
        string entry = SourceText("Scripts/Entry.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string transition = SourceText("Code/Patches/NinjaSlayerTransitionPatch.cs");

        Assert.DoesNotContain("TransitionAssetPrefetch", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("TransitionAssetPrefetch", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("NinjaSlayerTransitionScenePrewarmer", transition, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code",
            "Patches",
            "NinjaSlayerTransitionAssetPrefetchPatch.cs")));
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code",
            "Transition",
            "NinjaSlayerRunAssetPrefetcher.cs")));
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code",
            "Transition",
            "NinjaSlayerTransitionScenePrewarmer.cs")));
    }

    [Fact]
    public void TransitionEarlyLoadingOwnsACompletePresentationBarrier()
    {
        string session = SourceText("Code/Transition/NinjaSlayerTransitionSession.cs");
        string barrier = SourceText("Code/Transition/TransitionPresentationBarrier.cs");
        string lease = SourceText("Code/Transition/TransitionNodeProcessLease.cs");
        string patches = SourceText("Code/Patches/NinjaSlayerTransitionPresentationPatch.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string entry = SourceText("Scripts/Entry.cs");
        string reveal = SourceText("Code/Patches/NinjaSlayerTransitionRevealGatePatch.cs");

        Assert.Contains("TransitionPresentationPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("InstallCapability<TransitionPresentationPatchGroup>", entry, StringComparison.Ordinal);
        Assert.Contains("TransitionPresentationBarrier", session, StringComparison.Ordinal);
        Assert.Contains("TransitionNodeProcessLease", session, StringComparison.Ordinal);
        Assert.Contains("NodeAdded += OnNodeAdded", lease, StringComparison.Ordinal);
        Assert.Contains("ProcessModeEnum.Disabled", lease, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureAndDisable(_root)", lease, StringComparison.Ordinal);
        Assert.Contains("TransitionPresentationDisposition.Discarded", barrier, StringComparison.Ordinal);
        Assert.Contains("root.TreeExiting += OnPresentationRootTreeExiting", session, StringComparison.Ordinal);
        Assert.Contains("ReleasePresentationRootLifetime", session, StringComparison.Ordinal);
        Assert.Contains("TransitionCompletionStatus.Cancelled", session, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerTransitionTeardownPresentationPatch", groups, StringComparison.Ordinal);
        Assert.Contains("A replacement NRun appeared", SourceText("Code/Transition/NinjaSlayerTransitionGate.cs"), StringComparison.Ordinal);
        Assert.Contains("NGame.ReturnToMainMenu", patches, StringComparison.Ordinal);
        Assert.Contains("NGame.ReturnToMainMenuWithInternalError", patches, StringComparison.Ordinal);
        Assert.Contains("RunManager.CleanUp", patches, StringComparison.Ordinal);
        Assert.Contains("CombatManager.AfterCombatRoomLoaded", patches, StringComparison.Ordinal);
        Assert.Contains("NAncientEventLayout.OnSetupComplete", patches, StringComparison.Ordinal);
        Assert.Contains("AncientHealVfx", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("victory.mp3", patches, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/Patches/NinjaSlayerVictoryRewardSfxPatch.cs")));
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/Combat/NinjaSlayerVictorySfxPolicy.cs")));
        Assert.Contains("NRunMusicController.UpdateMusic", patches, StringComparison.Ordinal);
        Assert.Contains("SfxCmd.PlayLoop", patches, StringComparison.Ordinal);

        int release = reveal.IndexOf("session.ReleasePresentation();", StringComparison.Ordinal);
        int roomFade = reveal.IndexOf("await transition.RoomFadeIn", StringComparison.Ordinal);
        int fullFade = reveal.LastIndexOf("await transition.FadeIn", StringComparison.Ordinal);
        Assert.True(release >= 0 && roomFade > release);
        Assert.True(fullFade > reveal.LastIndexOf("session.ReleasePresentation();", StringComparison.Ordinal));
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
    public void NinjaSlayerMapHistoryAndEvasionPresentationUseStableScopes()
    {
        string mapPatch = SourceText("Code/Patches/NinjaSlayerMapHistoryPatch.cs");
        string alignment = SourceText("Code/Combat/VisitedMapHistoryAlignment.cs");
        string dodgePatch = SourceText("Code/Patches/EnemyAttackDodgePatch.cs");
        string dodgeAnimation = SourceText("Code/ExternalAnimations/CombatDodgeAnimation.cs");
        string evasion = SourceText("Powers/EvasionPower.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");

        Assert.Contains("VisitedMapCoords", mapPatch, StringComparison.Ordinal);
        Assert.Contains("VisitedMapHistoryAlignment.ResolveHistoryIndex", mapPatch, StringComparison.Ordinal);
        Assert.Contains("expectedPrefix", alignment, StringComparison.Ordinal);
        Assert.Contains("LocalContext.GetMe(runState)?.Character is not INinjaSlayerCharacter", mapPatch, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerMapHistoryIconPatch>()", groups, StringComparison.Ordinal);

        Assert.Contains("AttackIntentPreviewContext.IsActive", evasion, StringComparison.Ordinal);
        Assert.Contains("CombatDodgeAnimation.NotifyImpact(Owner)", evasion, StringComparison.Ordinal);
        Assert.DoesNotContain("FastAttackAnimation.Play", evasion, StringComparison.Ordinal);
        Assert.Contains("owner.GetPower<EvasionPower>()", dodgeAnimation, StringComparison.Ordinal);
        Assert.Contains("nameof(AttackIntent.GetSingleDamage)", dodgePatch, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<AttackIntentDamagePreviewPatch>()", groups, StringComparison.Ordinal);
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
    public void UnifiedFinisherSessionOwnsForwardKokiAndReverseDeathLifecycles()
    {
        string request = SourceText("Code/ExternalAnimations/FinisherSessionRequest.cs");
        string session = SourceText("Code/ExternalAnimations/FinisherSession.cs");
        string eligibility = SourceText("Code/ExternalAnimations/FinisherEligibilityService.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");
        string classifier = SourceText("Code/ExternalAnimations/NinjaSlayerDeathClassifier.cs");
        string continuation = SourceText("Code/ExternalAnimations/FinisherDeathContinuationRegistry.cs");
        string death = SourceText("Code/ExternalAnimations/DeathAnimation.cs");
        string deathPatch = SourceText("Code/Patches/NinjaSlayerDeathAnimPatch.cs");
        string finisherPatches = SourceText("Code/Patches/NinjaSlayerFinisherPatches.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string adapters = SourceText("Code/ExternalAnimations/FinisherActionAdapters.cs");
        string approach = SourceText("Code/Combat/FinisherApproachPath.cs");
        string impactPosition = SourceText(
            "Code/ExternalAnimations/FinisherImpactPositionResolver.cs");
        string layerLease = SourceText(
            "Code/ExternalAnimations/FinisherActorLayerLease.cs");
        string animationPatch = SourceText("Code/Patches/NinjaSlayerAnimationPatch.cs");
        string freezeLease = SourceText("Code/ExternalAnimations/FinisherImpactVfxFreezeLease.cs");
        string cinematic = SourceText("Code/ExternalAnimations/NinjaSlayerFinisherCinematic.cs");
        string yamotoKoki = File.ReadAllText(Path.Combine(Root, "Monsters", "YamotoKokiMonster.cs"));
        string yamotoAnimations = SourceText("Code/ExternalAnimations/YamotoKokiCombatAnimations.cs");

        Assert.Contains("NinjaSlayerAttack", request, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIaiSlash", request, StringComparison.Ordinal);
        Assert.Contains("EnemyExecutesNinjaSlayer", request, StringComparison.Ordinal);
        Assert.Contains("AllCandidatesLethal", request, StringComparison.Ordinal);
        Assert.Contains("AnyCandidateLethal", request, StringComparison.Ordinal);
        Assert.Contains("FinisherSessionRequest request", session, StringComparison.Ordinal);
        Assert.Contains("IFinisherActionAdapter ActionAdapter", request, StringComparison.Ordinal);
        Assert.Contains("FinisherActionAdapters.YamotoKokiIai", eligibility, StringComparison.Ordinal);
        Assert.Contains("FinisherActionAdapters.Stationary", classifier, StringComparison.Ordinal);
        Assert.Contains("IFinisherActionAdapter Fast", adapters, StringComparison.Ordinal);
        Assert.Contains("IFinisherActionAdapter Slow", adapters, StringComparison.Ordinal);
        Assert.Contains("IFinisherActionAdapter Combo", adapters, StringComparison.Ordinal);
        Assert.Contains("record struct FinisherActionContext", adapters, StringComparison.Ordinal);
        Assert.Contains("FinisherApproachMode.ContinuousToImpact", adapters, StringComparison.Ordinal);
        Assert.Contains("FinisherApproachMode.TeleportAtPeak", adapters, StringComparison.Ordinal);
        Assert.Contains("FinisherApproachMode.PrepositionThenLunge", adapters, StringComparison.Ordinal);
        Assert.Contains("FinisherApproachMode.Stationary", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain("TravelThenSnap", approach, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, IFinisherActionAdapter> TriggerRegistry", adapters, StringComparison.Ordinal);
        Assert.Contains("internal static void Register", adapters, StringComparison.Ordinal);
        Assert.Contains("bool RequiresPositioning", adapters, StringComparison.Ordinal);
        Assert.Contains("bool HasContinuousTravel", adapters, StringComparison.Ordinal);
        Assert.Contains("Vector2 squashMultiplier", adapters, StringComparison.Ordinal);
        Assert.Contains("_actionAdapter.GetTravelProgress", session, StringComparison.Ordinal);
        Assert.Contains("_actionAdapter.CreateContext", session, StringComparison.Ordinal);
        Assert.Contains("_actionContext.TravelStartPosition.Lerp", session, StringComparison.Ordinal);
        Assert.Contains("_actionContext.TravelEndPosition", session, StringComparison.Ordinal);
        Assert.Contains("_actorNode.Position = _actionContext.ImpactPosition", session, StringComparison.Ordinal);
        Assert.Contains("FinisherImpactPositionResolver.ResolveImpactX", adapters, StringComparison.Ordinal);
        Assert.Contains("FinisherSquashAnchorPolicy.Resolve", impactPosition, StringComparison.Ordinal);
        Assert.Contains("GetGlobalTransformWithCanvas", impactPosition, StringComparison.Ordinal);
        Assert.Contains("FinisherActorLayerLease.TryAcquire", session, StringComparison.Ordinal);
        Assert.Contains("%HealthBar", layerLease, StringComparison.Ordinal);
        Assert.Contains("body.ZAsRelative = false", layerLease, StringComparison.Ordinal);
        Assert.Contains("_actorLayerLease?.Dispose()", session, StringComparison.Ordinal);
        Assert.Contains("WrapOwnedTriggerAtActionPeak", animationPatch, StringComparison.Ordinal);
        Assert.Contains("EnsureActionPeakCore", session, StringComparison.Ordinal);
        Assert.Contains("!commandState.ShouldPlayAnimation", cinematic, StringComparison.Ordinal);
        Assert.Contains("await session.EnsureActionPeak()", cinematic, StringComparison.Ordinal);
        Assert.Contains("UsesNinjaSlayerSignatureImpact", request, StringComparison.Ordinal);
        Assert.Contains(
            "Scenario == FinisherScenarioKind.NinjaSlayerAttack",
            request,
            StringComparison.Ordinal);
        Assert.Contains("FinisherImpactPresentation.Create(_room, _ledger.Victims.Count)", session, StringComparison.Ordinal);
        Assert.Contains("FinisherImpactPresentation.CreateBackdropOnly(_room)", session, StringComparison.Ordinal);
        Assert.Contains("SetSignatureImpactState", session, StringComparison.Ordinal);
        Assert.Contains("FinisherImpactPresentation.CreateBackdropOnly(room)", death, StringComparison.Ordinal);

        Assert.Contains("TryCreateYamotoKokiSession", eligibility, StringComparison.Ordinal);
        Assert.Contains("FinisherScenarioKind.YamotoKokiIaiSlash", eligibility, StringComparison.Ordinal);
        Assert.Contains("EvaluateAction", forecast, StringComparison.Ordinal);
        Assert.Contains("owner.Player?.RunState ?? owner.PetOwner?.RunState", forecast, StringComparison.Ordinal);
        Assert.Contains("IaiApproachDistance = 120f", yamotoAnimations, StringComparison.Ordinal);
        Assert.Contains("IaiApproachSeconds = 0.25f", yamotoAnimations, StringComparison.Ordinal);
        Assert.Contains("await TweenIaiApproach", yamotoAnimations, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIaiApproachMode.StandardLunge", yamotoKoki, StringComparison.Ordinal);
        Assert.Contains("YamotoKokiIaiApproachMode.FinisherCloseRange", yamotoKoki, StringComparison.Ordinal);
        Assert.Contains("approachStart = originalPosition", yamotoAnimations, StringComparison.Ordinal);
        Assert.Contains("creature.Side == CombatSide.Player", yamotoAnimations, StringComparison.Ordinal);
        Assert.True(CountOccurrences(yamotoKoki, "GetCurrentIaiTargets()") >= 2);
        Assert.Contains("ReferenceEquals(enemy.CombatState, combatState)", yamotoKoki, StringComparison.Ordinal);
        Assert.True(
            yamotoKoki.IndexOf("PlayYamotoKokiIaiPetals", StringComparison.Ordinal)
            < yamotoKoki.IndexOf("await PlayIaiImpact()", StringComparison.Ordinal));

        Assert.Contains("TryStartReverseFinisher", classifier, StringComparison.Ordinal);
        Assert.Contains("FinisherScenarioKind.EnemyExecutesNinjaSlayer", classifier, StringComparison.Ordinal);
        Assert.Contains("FinisherCompletionCondition.AnyCandidateLethal", classifier, StringComparison.Ordinal);
        Assert.Contains("FinisherDeathContinuationRegistry.Arm", session, StringComparison.Ordinal);
        Assert.Contains("TryConsumeReverseFlight", deathPatch, StringComparison.Ordinal);
        Assert.Contains("PlayEnemyFinisherFlightOnly", deathPatch, StringComparison.Ordinal);
        Assert.Contains("static class FinisherDeathContinuationRegistry", continuation, StringComparison.Ordinal);
        Assert.Contains("PlayEnemyFinisherFlightOnly", death, StringComparison.Ordinal);
        Assert.Contains("FreezeReverseVictimVisuals", session, StringComparison.Ordinal);
        Assert.Contains("FinisherImpactVfxFreezeLease.Acquire", session, StringComparison.Ordinal);
        Assert.Contains("FinisherImpactVfxFreezeLease.Acquire", death, StringComparison.Ordinal);
        Assert.Contains("ContainsVisualNearTargets", freezeLease, StringComparison.Ordinal);
        Assert.Contains("target.Entity.GetVfxContainer()", freezeLease, StringComparison.Ordinal);
        Assert.Contains("CaptureBaseline", freezeLease, StringComparison.Ordinal);
        Assert.Contains("FinisherAttackVfxBaselineContext", freezeLease, StringComparison.Ordinal);
        Assert.Contains("player.Character is not INinjaSlayerCharacter", freezeLease, StringComparison.Ordinal);

        Assert.Contains("class NinjaSlayerDeathCompletionPatch", finisherPatches, StringComparison.Ordinal);
        Assert.Contains("nameof(Hook.AfterDeath)", finisherPatches, StringComparison.Ordinal);
        Assert.Contains("await deathAnimationTask", finisherPatches, StringComparison.Ordinal);
        Assert.True(
            finisherPatches.IndexOf("await deathAnimationTask", StringComparison.Ordinal)
            < finisherPatches.IndexOf("await original", StringComparison.Ordinal));
        Assert.Contains("RegisterPatch<NinjaSlayerDeathCompletionPatch>()", groups, StringComparison.Ordinal);
    }

    [Fact]
    public void DoomSquashOnlyAnchorsVerticalCompressionAndAllCloseRangeActionsUseZeroGap()
    {
        string session = SourceText("Code/ExternalAnimations/FinisherSession.cs");
        string anchorPolicy = SourceText("Code/Combat/FinisherSquashAnchorPolicy.cs");
        string visuals = SourceText("Content/NinjaSlayerCombatVisuals.cs");
        string classifier = SourceText("Code/ExternalAnimations/NinjaSlayerDeathClassifier.cs");
        string alabama = SourceText("Code/ExternalAnimations/AlabamaDropAnimation.cs");
        string architect = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");

        Assert.Contains("FinisherSquashAnchorKind.BottomCenter", session, StringComparison.Ordinal);
        Assert.DoesNotContain("LeftCenter", anchorPolicy, StringComparison.Ordinal);
        Assert.Contains(
            "ApplyDeathSquashTransform(state, multiplier, snapshot.Rotation)",
            session,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyDeathSquashTransform(state, squashMultiplier, rotation)",
            session,
            StringComparison.Ordinal);
        Assert.Contains("body.Position = state.OriginalPosition", session, StringComparison.Ordinal);
        Assert.Contains("public const float CloseRangeApproachGap = 0f", visuals, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerCombatVisuals.CloseRangeApproachGap", alabama, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerCombatVisuals.CloseRangeApproachGap", architect, StringComparison.Ordinal);
        Assert.Contains("FinisherActionAdapters.Stationary", classifier, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetedRelicFlashesFollowDynamicCreatureLayoutWithoutReplacingVanillaTween()
    {
        string follower = SourceText("Code/Nodes/NCreatureTopVfxFollower.cs");
        string patch = SourceText("Code/Patches/TargetedRelicFlashAnchorPatch.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");

        Assert.Contains("targetNode.GetTopOfHitbox()", follower, StringComparison.Ordinal);
        Assert.Contains("VisualNodeNames = [\"Image1\", \"Image2\", \"Image3\"]", follower, StringComparison.Ordinal);
        Assert.Contains("visual?.Reparent(this, true)", follower, StringComparison.Ordinal);
        Assert.Contains("typeof(NRelicFlashVfx)", patch, StringComparison.Ordinal);
        Assert.Contains("[typeof(RelicModel), typeof(Creature)]", patch, StringComparison.Ordinal);
        Assert.Contains("NCreatureTopVfxFollower.Attach(__result, target)", patch, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<TargetedRelicFlashAnchorPatch>()", groups, StringComparison.Ordinal);
    }

    [Fact]
    public void AbandonDeathHasOneGameOverPresentationOwnerInEveryRoomType()
    {
        string patches = SourceText("Code/Patches/NinjaSlayerOutsideCombatDeathFeedbackPatch.cs");
        string deathPatch = SourceText("Code/Patches/NinjaSlayerDeathAnimPatch.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");

        Assert.Contains("RunManager.Instance.IsAbandoned", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("CombatManager.Instance.IsInProgress", patches, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAbandonDeathFeedback.TryMark(creature)", patches, StringComparison.Ordinal);
        Assert.Contains("RegisterVisual(existingVisuals, creature)", patches, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAbandonDeathFeedback.IsPending(__instance.Entity)", deathPatch, StringComparison.Ordinal);
        Assert.Contains("await original;", patches, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAudio.NinjaSlayerSuicideEvent", patches, StringComparison.Ordinal);
        Assert.Contains("RegisterVisual(__result, __instance)", patches, StringComparison.Ordinal);
        Assert.Contains("NMerchantCharacter.PlayAnimation", patches, StringComparison.Ordinal);
        Assert.Contains("MoveCreaturesToDifferentLayerAndDisableUi", patches, StringComparison.Ordinal);
        Assert.Contains("playSuicideSfx: false", patches, StringComparison.Ordinal);
        Assert.Contains("EnumerateDescendants<NCreatureVisuals>", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("FindChildren", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("DebugOnlyGetState", patches, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerAbandonDeathCapturePatch>", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerAbandonVisualCreationPatch>", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerAbandonMerchantDeathPatch>", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NinjaSlayerAbandonGameOverDeathFeedbackPatch>", groups, StringComparison.Ordinal);
    }

    [Fact]
    public void NarakuLifeExtendsRightWithoutMovingTheVanillaBlockAnchor()
    {
        string patch = SourceText("Code/Patches/NarakuLifeHealthBarLayoutPatch.cs");
        string layout = SourceText("Code/Combat/ExtendedHealthBarLayout.cs");
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");

        Assert.Contains("HarmonyAfter(\"com.ritsukage.sts2-RitsuLib.framework-core\")", patch, StringComparison.Ordinal);
        Assert.Contains("SetHpBarContainerSizeWithOffsetsImmediately", patch, StringComparison.Ordinal);
        Assert.Contains("barPosition.X = layout.BarLeft", patch, StringComparison.Ordinal);
        Assert.Contains("AnchorBlock(__instance, layout.BlockLeft)", patch, StringComparison.Ordinal);
        Assert.Contains("barLeft + barWidth", layout, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<NarakuLifeHealthBarLayoutPatch>", groups, StringComparison.Ordinal);
    }

    // The assertions below describe the retired per-part semantic atlas implementation.
#if false
    [Fact]
    public void BossFragmentsUseCapturedMeshesAndFourByFourXpbdSoftBodies()
    {
        string presentation = SourceText("Code/ExternalAnimations/BossDismembermentPresentation.cs");
        string body = SourceText("Code/Combat/SoftFragmentBody.cs");
        string solver = SourceText("Code/Combat/BossSoftBodySolver.cs");
        string dismembermentMath = SourceText("Code/Combat/BossDismembermentMath.cs");
        string launchProfile = SourceText("Code/Combat/BossFountainLaunchProfile.cs");
        string launchActuator = SourceText("Code/Combat/SoftBodyLaunchActuator.cs");
        string deformationExciter = SourceText("Code/Combat/SoftBodyDeformationExciter.cs");
        string broadphase = SourceText("Code/Combat/SoftCollisionBroadphase.cs");
        string ragdollLink = SourceText("Code/Combat/SoftRagdollLink.cs");
        string manifoldBuilder = SourceText("Code/Combat/SoftContactManifoldBuilder.cs");
        string contactSolver = SourceText("Code/Combat/SoftContactSolver.cs");
        string boundarySolver = SourceText("Code/Combat/SoftBoundaryContactSolver.cs");
        string energyAudit = SourceText("Code/Combat/SoftBodyEnergyAudit.cs");
        string materialProfile = SourceText("Code/Combat/SoftBodyMaterialProfile.cs");
        string surface = SourceText(
            "Code/ExternalAnimations/BossCapturedFragmentRenderSurface.cs");
        string capture = SourceText("Code/ExternalAnimations/BossVisualCapture.cs");
        string partitioner = SourceText("Code/ExternalAnimations/BossFragmentPartitioner.cs");
        string semanticPolicy = SourceText("Code/Combat/BossSemanticPartPolicy.cs");
        string semanticMergePolicy = SourceText(
            "Code/Combat/BossSemanticPartMergePolicy.cs");
        string spineTopology = SourceText("Code/Combat/BossSpineTopologyPolicy.cs");
        string captureSampling = SourceText("Code/Combat/BossCaptureSamplingMath.cs");
        string pose = SourceText("Code/Combat/SoftBodyRenderPose.cs");
        string shader = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "shaders",
            "vfx",
            "boss_dismemberment_clip.gdshader"));
        string controller = SourceText("Code/ExternalAnimations/BossDeathPresentationController.cs");
        string sentinelScript = File.ReadAllText(Path.Combine(
            Root,
            "tools",
            "gpu-sentinel",
            "BossSoftBodyGpuSentinel.gd"));
        string sentinelRunner = File.ReadAllText(Path.Combine(
            Root,
            "tools",
            "Test-BossSoftBodyGpuSentinel.ps1"));
        int launchMethodStart = body.IndexOf(
            "public void ApplyLaunchVelocityDelta",
            StringComparison.Ordinal);
        int launchMethodEnd = body.IndexOf(
            "public void Predict",
            launchMethodStart,
            StringComparison.Ordinal);
        Assert.True(launchMethodStart >= 0 && launchMethodEnd > launchMethodStart);
        string launchMethod = body[launchMethodStart..launchMethodEnd];

        Assert.Contains("private const float PhysicsStep = 1f / 60f", presentation, StringComparison.Ordinal);
        Assert.Contains("catchUpSteps < MaximumCatchUpSteps", presentation, StringComparison.Ordinal);
        Assert.Contains("private const int MaximumCatchUpSteps = 4", presentation, StringComparison.Ordinal);
        Assert.Contains("IsFullyOutsideScene(fragment.Body)", presentation, StringComparison.Ordinal);
        Assert.Contains("BuildRagdollLinks", presentation, StringComparison.Ordinal);
        Assert.Contains("Time.GetTicksUsec()", presentation, StringComparison.Ordinal);
        Assert.Contains("private const float CompressionSeconds = 0.1f", presentation, StringComparison.Ordinal);
        Assert.Contains("private const float PumpOpenSeconds = 0.06f", presentation, StringComparison.Ordinal);
        Assert.Contains("PinCompressed", presentation, StringComparison.Ordinal);
        Assert.Contains("TriggerArchitectBurst", presentation, StringComparison.Ordinal);
        Assert.Contains("SolveSoftBodies(seconds, _floorY)", presentation, StringComparison.Ordinal);
        Assert.Contains("SoftFragmentBody", presentation, StringComparison.Ordinal);
        Assert.Contains("BossSoftBodySolver", presentation, StringComparison.Ordinal);
        Assert.Contains("BossCapturedFragmentRenderSurface", surface, StringComparison.Ordinal);
        Assert.Contains("private const int RenderGridSize = 10", surface, StringComparison.Ordinal);
        Assert.Contains("SoftBodyRenderPoseResolver.TryResolve", surface, StringComparison.Ordinal);
        Assert.Contains("control_offsets", surface, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderBounds", surface, StringComparison.Ordinal);
        Assert.Contains("return atlasPart.SourceBounds", surface, StringComparison.Ordinal);
        Assert.Contains(
            "Godot.Collections.Array<Vector2> _controlOffsets",
            surface,
            StringComparison.Ordinal);
        Assert.Contains("StringName ControlOffsetsParameter", surface, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Vector2[] _controlOffsets",
            surface,
            StringComparison.Ordinal);
        Assert.Contains("parent.AddChildSafely(anchor)", surface, StringComparison.Ordinal);
        Assert.Contains("anchor.AddChildSafely(meshNode)", surface, StringComparison.Ordinal);
        Assert.Contains("EnsureInsideTree(anchor", surface, StringComparison.Ordinal);
        Assert.Contains("EnsureInsideTree(meshNode", surface, StringComparison.Ordinal);
        Assert.Contains("!_meshNode.IsInsideTree()", surface, StringComparison.Ordinal);
        Assert.Contains("render binding failed", surface, StringComparison.Ordinal);
        Assert.Contains("public const int GridSize = 4", body, StringComparison.Ordinal);
        Assert.Contains("public const int ParticleCount = GridSize * GridSize", body, StringComparison.Ordinal);
        Assert.Contains("BuildAreaConstraints", body, StringComparison.Ordinal);
        Assert.Contains("orientation", body, StringComparison.Ordinal);
        Assert.Contains("ApplyShapeMemoryForces", body, StringComparison.Ordinal);
        Assert.Contains("SoftBodyDeformationExciter", body, StringComparison.Ordinal);
        Assert.Contains("ApplySafetyBoundaryRestitution", body, StringComparison.Ordinal);
        Assert.Contains("TryProjectCandidateToSafety", body, StringComparison.Ordinal);
        Assert.Contains("MaximumResidualRmsRatio", materialProfile, StringComparison.Ordinal);
        Assert.Contains("QuadAreaConstraint", body, StringComparison.Ordinal);
        Assert.Contains("TriangleBarrier", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveTargetShape", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ActuationStrength", body, StringComparison.Ordinal);
        Assert.Contains(
            "private readonly BossFragmentPoint[] _launchDeltas",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new BossFragmentPoint[ParticleCount]",
            launchMethod,
            StringComparison.Ordinal);
        Assert.Contains("CaptureSubstepState", solver, StringComparison.Ordinal);
        Assert.Contains("TryCommitSubstep", solver, StringComparison.Ordinal);
        Assert.Contains("public const int MinimumSubsteps = 2", solver, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumSubsteps = 4", solver, StringComparison.Ordinal);
        Assert.Contains("ResolveSubstepCount", solver, StringComparison.Ordinal);
        Assert.Contains("public const int ConstraintIterations = 6", solver, StringComparison.Ordinal);
        Assert.Contains("SoftContactManifoldBuilder", solver, StringComparison.Ordinal);
        Assert.Contains("SoftContactSolver.SolvePositions", solver, StringComparison.Ordinal);
        Assert.Contains("SoftContactSolver.SolveVelocities", solver, StringComparison.Ordinal);
        Assert.Contains("SoftBodyEnergyAudit", solver, StringComparison.Ordinal);
        Assert.Contains("centerSpeedLimit", solver, StringComparison.Ordinal);
        Assert.DoesNotContain("SolveCenterVelocity", solver, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCenterVelocityImpulse", body, StringComparison.Ordinal);
        Assert.Contains("IsNewContact", contactSolver, StringComparison.Ordinal);
        Assert.Contains("_excludedPairs", manifoldBuilder, StringComparison.Ordinal);
        Assert.Contains("_armedPairs.Add(key)", manifoldBuilder, StringComparison.Ordinal);
        Assert.Contains("TemporalSamples", manifoldBuilder, StringComparison.Ordinal);
        Assert.Contains("TimeOfImpactIterations", manifoldBuilder, StringComparison.Ordinal);
        Assert.Contains("AveragePosition(secondHull)", manifoldBuilder, StringComparison.Ordinal);
        Assert.Contains(
            "enableTemporalSampling: substeps == MaximumSubsteps && iteration == 0",
            solver,
            StringComparison.Ordinal);
        Assert.Contains("RewindSweptBodies", solver, StringComparison.Ordinal);
        Assert.Contains("ProjectSweptOrderings", solver, StringComparison.Ordinal);
        Assert.Contains("TranslateForContinuousCollision", body, StringComparison.Ordinal);
        Assert.Contains("Restitution = 0.48f", boundarySolver, StringComparison.Ordinal);
        Assert.Contains("Friction = 0.12f", boundarySolver, StringComparison.Ordinal);
        Assert.Contains("CopyDeformedHull", boundarySolver, StringComparison.Ordinal);
        Assert.Contains("_latchedContacts", boundarySolver, StringComparison.Ordinal);
        Assert.Contains("LimitContactEnergy", energyAudit, StringComparison.Ordinal);
        Assert.Contains("RestoreParticleVelocities", energyAudit, StringComparison.Ordinal);
        Assert.Contains("_velocityPairKeys", solver, StringComparison.Ordinal);
        Assert.Contains("_cellPool", broadphase, StringComparison.Ordinal);
        Assert.Contains("_cells.Clear()", broadphase, StringComparison.Ordinal);
        Assert.Contains("MaximumCellsPerAxis", broadphase, StringComparison.Ordinal);
        Assert.Contains("float.IsFinite(minimum.X)", broadphase, StringComparison.Ordinal);
        Assert.Contains("CanBreak", ragdollLink, StringComparison.Ordinal);
        Assert.Contains("GetParticleInverseMass", ragdollLink, StringComparison.Ordinal);
        Assert.Contains("BreakDeadlineSeconds", ragdollLink, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSoftBodyPhysics.cs")));
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/Combat/SoftBodySecondaryMotionState.cs")));
        Assert.DoesNotContain("ApplyDirectionalDeformation", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("FragmentState", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("JellyDeformation", presentation, StringComparison.Ordinal);
        Assert.Contains("uniform vec2 control_offsets[16]", shader, StringComparison.Ordinal);
        Assert.Contains("VERTEX += mix(top, bottom, blend.y)", shader, StringComparison.Ordinal);
        Assert.Contains("COLOR = texture(TEXTURE, UV) * COLOR", shader, StringComparison.Ordinal);
        Assert.Contains("uniform vec2 seed_23", shader, StringComparison.Ordinal);
        Assert.Contains("atlas_content_min", shader, StringComparison.Ordinal);
        Assert.Contains("atlas_logical_size", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("texture_bounds_min", shader, StringComparison.Ordinal);
        Assert.Contains("SubViewport", capture, StringComparison.Ordinal);
        Assert.Contains("SubViewport.UpdateMode.Once", capture, StringComparison.Ordinal);
        Assert.Contains("Node.DuplicateFlags.Groups", capture, StringComparison.Ordinal);
        Assert.Contains("case Control control:", capture, StringComparison.Ordinal);
        Assert.Contains("control.Visible = false", capture, StringComparison.Ordinal);
        Assert.Contains("clone.TopLevel = false", capture, StringComparison.Ordinal);
        Assert.Contains("Boss capture bounds must be finite", capture, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(capture, "GetImage()"));
        Assert.Contains("CompleteStagingCapture", capture, StringComparison.Ordinal);
        Assert.Contains("CompleteAtlasCapture", capture, StringComparison.Ordinal);
        Assert.Contains("StagingTilePixels = 256", capture, StringComparison.Ordinal);
        Assert.Contains("AlphaSamplingAxis = 32", capture, StringComparison.Ordinal);
        Assert.Contains("occupiedSampleCells", capture, StringComparison.Ordinal);
        Assert.Contains("AtlasSupersampling = 2", capture, StringComparison.Ordinal);
        Assert.Contains("MaximumAtlasPixels = 8192", capture, StringComparison.Ordinal);
        Assert.Contains("MinimumAtlasBudgetBytes", capture, StringComparison.Ordinal);
        Assert.Contains("MaximumAtlasBudgetBytes", capture, StringComparison.Ordinal);
        Assert.Contains("IsolateSlots", capture, StringComparison.Ordinal);
        Assert.Contains("slot.Call(\"set_color\"", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("set_attachment", capture, StringComparison.Ordinal);
        int attachClone = capture.IndexOf("AttachTemporaryVisual(parent, clone, name)", StringComparison.Ordinal);
        int freezeClone = capture.IndexOf("FreezeSpineAnimation(clone)", attachClone, StringComparison.Ordinal);
        int isolateClone = capture.IndexOf("IsolateSlots(clone, visibleSlotIndices)", attachClone, StringComparison.Ordinal);
        Assert.True(attachClone >= 0 && freezeClone > attachClone && isolateClone > freezeClone);
        Assert.Contains("RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled", capture, StringComparison.Ordinal);
        Assert.Contains("GPU_SENTINEL_PASS", sentinelScript, StringComparison.Ordinal);
        Assert.Contains("get_image()", sentinelScript, StringComparison.Ordinal);
        Assert.Contains("part_bounds_min", sentinelScript, StringComparison.Ordinal);
        Assert.Contains("atlas_content_min", sentinelScript, StringComparison.Ordinal);
        Assert.Contains("atlas_logical_size", sentinelScript, StringComparison.Ordinal);
        Assert.DoesNotContain("texture_bounds_min", sentinelScript, StringComparison.Ordinal);
        Assert.DoesNotContain("texture_bounds_size", sentinelScript, StringComparison.Ordinal);
        Assert.Contains("Start-Process", sentinelRunner, StringComparison.Ordinal);
        Assert.Contains("GPU_SENTINEL_PASS", sentinelRunner, StringComparison.Ordinal);
        Assert.Contains("SetupElapsedTicks", capture, StringComparison.Ordinal);
        Assert.Contains("FitSemanticPartsToAtlas", capture, StringComparison.Ordinal);
        Assert.Contains("MergeSmallestRelatedPart", capture, StringComparison.Ordinal);
        Assert.Contains("MaximumAtlasPixels / rows", capture, StringComparison.Ordinal);
        Assert.Contains(
            "(MaximumAtlasPixels - StagingOrientationStripPixels) / columns",
            capture,
            StringComparison.Ordinal);
        Assert.Contains("_atlasCapturePadding", capture, StringComparison.Ordinal);
        Assert.Contains("MapViewportYToImage", capture, StringComparison.Ordinal);
        Assert.Contains("ReadbackOrientationMarker", capture, StringComparison.Ordinal);
        Assert.Contains("ResolveReadbackVerticalFlip", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveMeasurementScore", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("FlipY()", capture, StringComparison.Ordinal);
        Assert.Contains("imageHeight - 1 - viewportY", captureSampling, StringComparison.Ordinal);
        Assert.Contains("this.AddChildSafely(_stagingViewport)", capture, StringComparison.Ordinal);
        Assert.Contains("this.AddChildSafely(_atlasViewport)", capture, StringComparison.Ordinal);
        Assert.Contains("BuildSemanticParts", capture, StringComparison.Ordinal);
        Assert.Contains("BuildSemanticPartition", capture, StringComparison.Ordinal);
        Assert.Contains("BossFragmentRect SourceBounds", partitioner, StringComparison.Ordinal);
        Assert.Contains("capture.Partition", presentation, StringComparison.Ordinal);
        Assert.Contains("BossFragmentRect expected = partition.SourceBounds", presentation, StringComparison.Ordinal);
        Assert.Contains("TryResolveUniformBoundsCalibration", presentation, StringComparison.Ordinal);
        Assert.Contains("_bodyToPresentation *= bodyCalibration", presentation, StringComparison.Ordinal);
        Assert.Contains("ResolveCaptureBounds(creature)", presentation, StringComparison.Ordinal);
        Assert.Contains("skeleton.GetBounds()", presentation, StringComparison.Ordinal);
        Assert.Contains("descriptor.Fragment.BodyAreaRatio", presentation, StringComparison.Ordinal);
        Assert.Contains("AssignFountainLaunches();", presentation, StringComparison.Ordinal);
        Assert.Contains("descriptor.Part.Definition.BelongsToDetachedPart", presentation, StringComparison.Ordinal);
        Assert.Contains("fragment.CompressionOrigin", presentation, StringComparison.Ordinal);
        Assert.Contains("ToLocalPoint(detachedExplosionCenter.Value)", presentation, StringComparison.Ordinal);
        Assert.Contains("TryGetPartCenter()", controller, StringComparison.Ordinal);
        Assert.Contains("return _partFlight.GlobalCenter", controller, StringComparison.Ordinal);
        Assert.Contains("TryHidePartFlight()", controller, StringComparison.Ordinal);
        Assert.Contains("GroupBy(slot => slot.BoneId", partitioner, StringComparison.Ordinal);
        Assert.Contains("Call(\"get_bones\")", partitioner, StringComparison.Ordinal);
        Assert.Contains("Call(\"get_draw_order\")", partitioner, StringComparison.Ordinal);
        Assert.Contains("ReadString(data, \"get_bone_name\"", partitioner, StringComparison.Ordinal);
        Assert.Contains("CallObject(data, \"get_parent\")", partitioner, StringComparison.Ordinal);
        Assert.Contains("parentData?.Dispose()", partitioner, StringComparison.Ordinal);
        Assert.DoesNotContain("GetInstanceId()", partitioner, StringComparison.Ordinal);
        Assert.Contains("checked((ulong)boneIndex + 1UL)", spineTopology, StringComparison.Ordinal);
        Assert.Contains("ancestorIds.Add(ToBoneId(parentIndex))", spineTopology, StringComparison.Ordinal);
        Assert.Contains("BuildDrawOrder", spineTopology, StringComparison.Ordinal);
        Assert.Contains(
            "BossSemanticPartMergePolicy.Normalize",
            partitioner,
            StringComparison.Ordinal);
        Assert.Contains("MergeTinyOverlays", semanticMergePolicy, StringComparison.Ordinal);
        Assert.Contains("MergeBlendOnlyLayers", semanticMergePolicy, StringComparison.Ordinal);
        Assert.Contains("ReadSetupSlotIndex", capture, StringComparison.Ordinal);
        Assert.Contains("ReadUsesNormalBlend", partitioner, StringComparison.Ordinal);
        Assert.Contains(
            "part => part.Contains(overlay.Center)",
            semanticMergePolicy,
            StringComparison.Ordinal);
        Assert.Contains(
            "BossDismembermentMath.ConvexPolygonsOverlap",
            semanticMergePolicy,
            StringComparison.Ordinal);
        Assert.Contains("MergeInterleavedOverlaps", semanticMergePolicy, StringComparison.Ordinal);
        Assert.Contains("BuildSpatialFallback", partitioner, StringComparison.Ordinal);
        Assert.Contains("requestedCount: 9", partitioner, StringComparison.Ordinal);
        Assert.Contains("OversizedAreaRatio = 0.22f", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("OversizedSpanRatio = 0.45f", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("MaximumFountainLinks = 2", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("string PrimaryBoneName", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("BuildVoronoiCells", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("SelectAlphaSeeds", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("part.AlphaSamples", semanticPolicy, StringComparison.Ordinal);
        Assert.Contains("Unwrap", pose, StringComparison.Ordinal);
        Assert.Contains("MaximumResidual", pose, StringComparison.Ordinal);
        Assert.Contains("ProcessMode = ProcessModeEnum.Always", presentation, StringComparison.Ordinal);
        Assert.Contains("BossFountainLaunchProfile.CreatePlan", presentation, StringComparison.Ordinal);
        Assert.Contains("SoftBodyLaunchActuator", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveCollisionHullScale", launchProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("CorrelateEntangledLaunches", presentation, StringComparison.Ordinal);
        Assert.Contains("boneNamesByBodyId", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Clamp(particleRestLength, 0.5f, 72f)", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildEntangledLinks", dismembermentMath, StringComparison.Ordinal);
        Assert.Contains("Body.Release(TargetVelocity", launchActuator, StringComparison.Ordinal);
        Assert.Contains("MaximumDeformationSpeed", launchActuator, StringComparison.Ordinal);
        Assert.Contains("LaunchActuatorSeconds = 0.06f", launchProfile, StringComparison.Ordinal);
        Assert.Contains("MinimumLaunchSpeed = 780f", launchProfile, StringComparison.Ordinal);
        Assert.Contains("MaximumLaunchSpeed = 1250f", launchProfile, StringComparison.Ordinal);
        Assert.Contains("Gravity = 1450f", launchProfile, StringComparison.Ordinal);
        Assert.Contains("MaximumCenterSpeed = 2400f", launchProfile, StringComparison.Ordinal);
        Assert.Contains("CollisionMarginFullSeconds = 0.5f", launchProfile, StringComparison.Ordinal);
        Assert.Contains("SoftHorizontalBoundary", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("coverage_width_075", presentation, StringComparison.Ordinal);
        Assert.Contains("settlement_fraction", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("CountPredictedSettled", launchProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumGravity", launchProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumGravity", launchProfile, StringComparison.Ordinal);
        Assert.Contains("ApplyLaunchVelocityDelta", launchActuator, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAccelerationExcitation", deformationExciter, StringComparison.Ordinal);
        Assert.Contains("AddLaunchExcitation", deformationExciter, StringComparison.Ordinal);
        Assert.Contains("RemoveRigidComponents", deformationExciter, StringComparison.Ordinal);
        Assert.Contains("ShapeMemoryDampingRatio: 0.2f", materialProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveBurstDirection", dismembermentMath, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveLaunch", dismembermentMath, StringComparison.Ordinal);
        Assert.Contains("ZAsRelative = false", presentation, StringComparison.Ordinal);
        Assert.Contains("BossBurstPresentationCoordinator.FragmentZIndex", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstPresentationCoordinator.IsPresentationPaused(_room)",
            presentation,
            StringComparison.Ordinal);
        Assert.Contains("public override void _ExitTree()", presentation, StringComparison.Ordinal);
        Assert.Contains("Boss soft-body presentation failed", presentation, StringComparison.Ordinal);
        Assert.Contains("SetProcess(false);", presentation, StringComparison.Ordinal);
        Assert.Contains("ClearFragments();", presentation, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(presentation, "Entry.Logger.Info("));
        Assert.Contains("capture_bytes=", presentation, StringComparison.Ordinal);
        Assert.Contains("constraints=", presentation, StringComparison.Ordinal);
        Assert.Contains("launch_up=", presentation, StringComparison.Ordinal);
        Assert.Contains("capture_total_ms=", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("Boss dismemberment spawned", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("StartOriginalFadeFallback", presentation, StringComparison.Ordinal);
        Assert.Contains("BossBurstPresentationCoordinator.Register", controller, StringComparison.Ordinal);
        Assert.Contains("await registration.CombatRelease.WaitAsync", controller, StringComparison.Ordinal);
        int snapshotCapture = controller.IndexOf(
            "_dismembermentSnapshot = BossDismembermentPresentation.TryCapture(",
            StringComparison.Ordinal);
        int deadTrigger = controller.IndexOf("_boss.SetAnimationTrigger(\"Dead\")", StringComparison.Ordinal);
        Assert.True(snapshotCapture >= 0 && deadTrigger > snapshotCapture);
        Assert.Contains("_partSpec?.BoneName", controller, StringComparison.Ordinal);
        Assert.Contains("BossVisualCapture.TryCreate", presentation, StringComparison.Ordinal);
        Assert.Contains("capture.Texture", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate.Material", presentation, StringComparison.Ordinal);
        Assert.Contains("snapshot?.Dispose()", controller, StringComparison.Ordinal);
        Assert.Contains("snapshot == null", controller, StringComparison.Ordinal);
        Assert.Contains("CompleteWithoutFragments", controller, StringComparison.Ordinal);
        Assert.Contains("boss.AddChildSafely(controller)", controller, StringComparison.Ordinal);
        Assert.Contains("!controller.IsInsideTree()", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("StartOriginalFadeFallback", controller, StringComparison.Ordinal);
        Assert.True(CountOccurrences(controller, "DisposeDismembermentSnapshot()") >= 3);
    }
#endif

    [Fact]
    public void BossFragmentsUseOneSemanticGpuAtlasAndFrozenBattleScale()
    {
        string capture = SourceText("Code/ExternalAnimations/BossVisualCapture.cs");
        string partitioner = SourceText("Code/ExternalAnimations/BossFragmentPartitioner.cs");
        string presentation = SourceText("Code/ExternalAnimations/BossDismembermentPresentation.cs");
        string math = SourceText("Code/Combat/BossDismembermentMath.cs");
        string shader = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "shaders",
            "vfx",
            "boss_dismemberment_clip.gdshader"));

        Assert.Contains("PreferredScreenSupersampling = 2f", capture, StringComparison.Ordinal);
        Assert.Contains("MaximumAtlasPixels = 4096", capture, StringComparison.Ordinal);
        Assert.Contains("PartsPerFrame = 2", capture, StringComparison.Ordinal);
        Assert.Contains("SubViewport.UpdateMode.Once", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("GetImage(", capture, StringComparison.Ordinal);
        Assert.Contains("TryPackAtlas", capture, StringComparison.Ordinal);
        Assert.Contains("IsolateSlots", capture, StringComparison.Ordinal);
        Assert.Contains("slot.Call(\"set_color\"", capture, StringComparison.Ordinal);
        Assert.Contains("GroupBy(slot => slot.BoneId)", partitioner, StringComparison.Ordinal);
        Assert.Contains("MeasureIsolatedBounds", partitioner, StringComparison.Ordinal);
        Assert.Contains("slot.Call(\"set_attachment\"", partitioner, StringComparison.Ordinal);
        Assert.Contains("OversizedAreaRatio = 0.22f", partitioner, StringComparison.Ordinal);
        Assert.Contains("OversizedSpanRatio = 0.45f", partitioner, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumPieces = 16", math, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFountainRagdollLinks", presentation, StringComparison.Ordinal);
        Assert.Contains("CombatVfxContainer", presentation, StringComparison.Ordinal);
        Assert.Contains("BodyToSceneContainer", presentation, StringComparison.Ordinal);
        Assert.Contains("ValidateBaselineFragmentGeometry", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "Vector2 compressionOrigin = fragment.CompressionOrigin",
            presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_burstOrigin.X + MathF.Cos(phase) * CompressionSlideRadius",
            presentation,
            StringComparison.Ordinal);
        Assert.Contains("keeping the original death pose visible", presentation, StringComparison.Ordinal);
        Assert.Contains("uniform vec2 control_offsets[16]", shader, StringComparison.Ordinal);
        Assert.Contains("part_bounds_min", shader, StringComparison.Ordinal);
        Assert.Contains("atlas_content_min", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("texture_bounds_min", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("seed_16", shader, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossCaptureSamplingMath.cs")));
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSemanticPartPolicy.cs")));
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSemanticPartMergePolicy.cs")));
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSpineTopologyPolicy.cs")));
    }

    [Fact]
    public void BossBurstAtomicallyOwnsCombatEndMusicAndDeathFade()
    {
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string entry = SourceText("Scripts/Entry.cs");
        string patches = SourceText("Code/Patches/BossBurstPresentationPatches.cs");
        string compatibility = SourceText("Code/Compatibility/GameCompatibility.BossBurst.cs");
        string registry = SourceText(
            "Code/ExternalAnimations/BossBurstParticipationRegistry.cs");
        string fadeRegistry = SourceText(
            "Code/ExternalAnimations/BossBurstDeathFadeRegistry.cs");
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string musicSession = SourceText(
            "Code/ExternalAnimations/BossBurstMusicSession.cs");
        string deathPatch = SourceText("Code/Patches/BossDeathPresentationPatch.cs");

        Assert.Contains("class BossBurstPresentationPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossDeathPresentationPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstCombatEndMusicPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstSingleDeathFadePatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstGroupedDeathFadePatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstDeathFadePlaybackPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("InstallCapability<BossBurstPresentationPatchGroup>", entry, StringComparison.Ordinal);
        Assert.Contains("GameCompatibility.BossBurst.GetProbes()", entry, StringComparison.Ordinal);
        Assert.Contains("nameof(NRunMusicController.UpdateTrack)", patches, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstParticipationRegistry.ShouldSuppressCombatEndMusicAfterFailure()",
            patches,
            StringComparison.Ordinal);
        Assert.Contains("typeof(List<NCreature>)", patches, StringComparison.Ordinal);
        Assert.Contains("Prefix(NCreature creatureNode", patches, StringComparison.Ordinal);
        Assert.Contains("Prefix(ref List<NCreature> creatureNodes", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("object[] __args", patches, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstPresentationPolicy.ResolveGroupedDeathFade",
            patches,
            StringComparison.Ordinal);
        Assert.Contains("creatureNodes = remaining", patches, StringComparison.Ordinal);
        Assert.Contains("BossBurstDeathFadeRegistry.MarkPlaybackSuppressed", patches, StringComparison.Ordinal);
        Assert.Contains("BossBurstDeathFadeRegistry.ConsumePlaybackSuppression", patches, StringComparison.Ordinal);
        Assert.Contains("__result = Task.CompletedTask", patches, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<NMonsterDeathVfx", fadeRegistry, StringComparison.Ordinal);
        Assert.Contains("musicEvent.Call(\"stop\", 1)", compatibility, StringComparison.Ordinal);
        Assert.Contains(
            "proxy.Set(\"_musicEv\", default(Variant))",
            compatibility,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__instance.StopMusic()", patches, StringComparison.Ordinal);
        Assert.Contains(
            "proxy.Call(\"update_global_parameter\", \"Progress\", 0f)",
            compatibility,
            StringComparison.Ordinal);
        Assert.Contains("NRunMusicController.ResolveMusic", compatibility, StringComparison.Ordinal);
        Assert.Contains("nameof(NMonsterDeathVfx.PlayVfx)", compatibility, StringComparison.Ordinal);
        Assert.Contains("CurrentTrack?.SetValue", compatibility, StringComparison.Ordinal);
        Assert.Contains("TryStopBossMusicImmediately", musicSession, StringComparison.Ordinal);
        int musicBegin = deathPatch.IndexOf("BossBurstMusicSession.Begin(room)", StringComparison.Ordinal);
        int deathAnimation = deathPatch.IndexOf("controller.StartDeathAnimation(shouldRemove)", StringComparison.Ordinal);
        Assert.True(musicBegin >= 0 && deathAnimation > musicBegin);
        Assert.Contains("BossBurstPresentationPolicy.ShouldRollbackMusic", deathPatch, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<NCreature", registry, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<NCombatRoom", registry, StringComparison.Ordinal);
        Assert.Contains("Rooms.Remove(sceneRoom)", registry, StringComparison.Ordinal);
        Assert.Contains("rawInstance.Call(\"start\")", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("audioEvent.TryPlay()", coordinator, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/Patches/NinjaSlayerVictoryRewardSfxPatch.cs")));
    }

    [Fact]
    public void ArchitectExecutionUsesTheSharedSoftBodyLeadAndConcurrentExit()
    {
        string cinematic = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string deathSession = SourceText("Code/ExternalAnimations/ArchitectDeathPresentationSession.cs");
        string presentation = SourceText("Code/ExternalAnimations/BossDismembermentPresentation.cs");
        string patch = SourceText("Code/Patches/ArchitectExecutionPatch.cs");

        Assert.Contains("CreatureCmd.Kill(_architectNode.Entity, force: true)", cinematic, StringComparison.Ordinal);
        Assert.Contains("WaitUntilDeathStarts(killTask", cinematic, StringComparison.Ordinal);
        int capture = cinematic.IndexOf(
            "BossDismembermentPresentation.TryCapture(",
            StringComparison.Ordinal);
        int kill = cinematic.IndexOf(
            "CreatureCmd.Kill(_architectNode.Entity, force: true)",
            StringComparison.Ordinal);
        Assert.True(capture >= 0 && kill > capture);
        Assert.Contains("TrySpawnArchitectLead", cinematic, StringComparison.Ordinal);
        Assert.Contains("BossBurstPresentationCoordinator.Register", cinematic, StringComparison.Ordinal);
        Assert.Contains("await registration.Cue.WaitAsync", cinematic, StringComparison.Ordinal);
        Assert.Contains("registration.CombatRelease.WaitAsync", cinematic, StringComparison.Ordinal);
        Assert.DoesNotContain("registration.Completion.WaitAsync", cinematic, StringComparison.Ordinal);
        Assert.Contains("private const float ExitSpeedPixelsPerSecond = 840f", cinematic, StringComparison.Ordinal);
        int logicalDeath = cinematic.IndexOf("_deathSession.CompleteVisuals()", StringComparison.Ordinal);
        int exitStart = cinematic.IndexOf("StartExitScene()", StringComparison.Ordinal);
        Assert.True(logicalDeath >= 0 && exitStart > logicalDeath);
        Assert.Contains("private CinematicSessionLifetime? _exitLifetime", cinematic, StringComparison.Ordinal);
        Assert.Contains("private async Task RunExitScene", cinematic, StringComparison.Ordinal);
        Assert.Contains("room.AddChildSafely(controller)", cinematic, StringComparison.Ordinal);
        Assert.Contains("private bool _initialized", cinematic, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", cinematic, StringComparison.Ordinal);
        Assert.DoesNotContain("NinjaSlayerNinjaSoulEvent", cinematic, StringComparison.Ordinal);
        Assert.Contains(
            "public const float DurationSeconds = BossBurstTimeline.LeadSeconds",
            deathSession,
            StringComparison.Ordinal);
        Assert.Contains("private readonly ulong _architectInstanceId", deathSession, StringComparison.Ordinal);
        Assert.DoesNotContain("_architect.GetInstanceId()", deathSession, StringComparison.Ordinal);
        Assert.Contains("PresentationMode.ArchitectLead", presentation, StringComparison.Ordinal);
        Assert.Contains("SolveSoftBodies(seconds, _floorY)", presentation, StringComparison.Ordinal);
        Assert.Contains("maximumClusterSize", presentation, StringComparison.Ordinal);
        Assert.Contains("CanBreak = canBreak", presentation, StringComparison.Ordinal);
        Assert.Contains("_joints.Clear();", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "BuildRagdollLinks(_bodies.Count, canBreak: false)",
            presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFountainRagdollLinks", presentation, StringComparison.Ordinal);
        int architectBurst = presentation.IndexOf(
            "internal bool TriggerArchitectBurst()",
            StringComparison.Ordinal);
        int clearLeadLinks = presentation.IndexOf(
            "_joints.Clear();",
            architectBurst,
            StringComparison.Ordinal);
        int applyFountainPlan = presentation.IndexOf(
            "ApplyFountainPlan();",
            architectBurst,
            StringComparison.Ordinal);
        Assert.True(
            architectBurst >= 0
            && clearLeadLinks > architectBurst
            && applyFountainPlan > clearLeadLinks);
        Assert.Contains("fragment.Body.PinCompressed", presentation, StringComparison.Ordinal);
        Assert.Contains("ApplyRenderFrame();", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "SetCollisionEnvelope(hullScale: 1f, marginScale: 0f)",
            presentation,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/ExternalAnimations/ArchitectRagdollDeathAnimation.cs")));
        Assert.Contains("__instance.DeathAnimationTask = deathTask", patch, StringComparison.Ordinal);
        Assert.Contains("__result = ArchitectDeathPresentationSession.DurationSeconds", patch, StringComparison.Ordinal);
        Assert.Contains("return false", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void BossBurstVideoCannotBeSkippedAndKeepsGlobalStatusUiAboveIt()
    {
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string patch = SourceText("Code/Patches/BossDeathPresentationPatch.cs");

        Assert.Contains("public const int VideoZIndex = 100", coordinator, StringComparison.Ordinal);
        Assert.Contains("public const int TopBarZIndex = 110", coordinator, StringComparison.Ordinal);
        Assert.Contains("SetLayer(globalUi.TopBar, TopBarZIndex)", coordinator, StringComparison.Ordinal);
        Assert.Contains("creature.Visuals.GetCurrentBody()", coordinator, StringComparison.Ordinal);
        Assert.Contains("LowerShadows(creature.Visuals)", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLayer(creature.Visuals, ActorZIndex)", coordinator, StringComparison.Ordinal);
        Assert.Contains("Volume = 0f", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstTimeline.ResolveFadeAlpha(videoPosition)",
            coordinator,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CombatReleaseSeconds", coordinator, StringComparison.Ordinal);
        int videoTimeline = coordinator.IndexOf(
            "private async Task PlayVideoTimeline",
            StringComparison.Ordinal);
        int videoStop = coordinator.IndexOf(
            "FreeVideo(batch)",
            videoTimeline,
            StringComparison.Ordinal);
        int combatRelease = coordinator.IndexOf(
            "batch.CombatReleaseSource.TrySetResult()",
            videoStop,
            StringComparison.Ordinal);
        Assert.True(videoTimeline >= 0 && videoStop > videoTimeline && combatRelease > videoStop);
        Assert.Contains("rawInstance.HasMethod(\"get_playback_state\")", coordinator, StringComparison.Ordinal);
        Assert.Contains("hasUnreleasedPresentation", coordinator, StringComparison.Ordinal);
        Assert.Contains("!batch.CombatReleaseSource.Task.IsCompleted", coordinator, StringComparison.Ordinal);
        Assert.Contains("private ulong _roomInstanceId", coordinator, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(active, this)", coordinator, StringComparison.Ordinal);
        Assert.Contains("if (ownsRegistration)", coordinator, StringComparison.Ordinal);
        Assert.Contains("FinalizeBatch(batch", coordinator, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref batch.Finalized, 1)", coordinator, StringComparison.Ordinal);
        Assert.Contains("room.GetTree().Paused || IsBlockingUiOpen()", coordinator, StringComparison.Ordinal);
        Assert.Contains("using the timed fallback", coordinator, StringComparison.Ordinal);
        Assert.Contains("root.AddChildSafely(player)", coordinator, StringComparison.Ordinal);
        Assert.Contains("!player.IsInsideTree()", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("room.ProcessMode == ProcessModeEnum.Disabled", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.IsKeyPressed", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.Space", coordinator, StringComparison.Ordinal);
        Assert.Contains("__instance.Entity.IsPrimaryEnemy", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void BossBurstOwnsTheDeathTaskAndBlocksCombatEndInInstantMode()
    {
        string controller = SourceText(
            "Code/ExternalAnimations/BossDeathPresentationController.cs");
        string patch = SourceText("Code/Patches/BossDeathPresentationPatch.cs");
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string feedback = SourceText("Content/NinjaSlayerCombatFeedback.cs");

        Assert.Contains("internal float StartDeathAnimation(bool shouldRemove)", controller, StringComparison.Ordinal);
        Assert.Contains("_boss.DeathAnimationTask = deathTask", controller, StringComparison.Ordinal);
        Assert.Contains("return BossBurstTimeline.LeadSeconds", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IDeathDelayer", controller, StringComparison.Ordinal);
        Assert.Contains("controller.StartDeathAnimation(shouldRemove)", patch, StringComparison.Ordinal);
        Assert.Contains("return false", patch, StringComparison.Ordinal);
        Assert.Contains("WaitForCombatRelease", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "await BossBurstPresentationCoordinator.WaitForCombatRelease()",
            feedback,
            StringComparison.Ordinal);
        Assert.Contains("registration.CombatRelease.WaitAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("registration.Completion.WaitAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void BossDeathWhiteoutRunsDuringTheFinalLeadWindowAndRestoresSpineState()
    {
        string controller = SourceText(
            "Code/ExternalAnimations/BossDeathPresentationController.cs");
        string lease = SourceText("Code/ExternalAnimations/BossDeathWhiteoutLease.cs");
        string timeline = SourceText("Code/Combat/BossBurstTimeline.cs");
        string character = SourceText("Content/NinjaSlayerCharacter.cs");
        string shader = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "shaders",
            "vfx",
            "boss_death_whiteout.gdshader"));

        Assert.Contains("WhiteoutStartSeconds = LeadSeconds - WhiteoutSeconds", timeline, StringComparison.Ordinal);
        Assert.Contains("ResolveWhiteoutMix", timeline, StringComparison.Ordinal);
        Assert.Contains("RunWhiteoutUntilCue", controller, StringComparison.Ordinal);
        Assert.Contains("Task whiteoutTask", controller, StringComparison.Ordinal);
        Assert.Contains("DisposeWhiteout()", controller, StringComparison.Ordinal);
        Assert.Contains("GetNormalMaterial()", lease, StringComparison.Ordinal);
        Assert.Contains("SetNormalMaterial", lease, StringComparison.Ordinal);
        Assert.Contains("get_color", lease, StringComparison.Ordinal);
        Assert.Contains("set_color", lease, StringComparison.Ordinal);
        Assert.Contains("state.Original.A", lease, StringComparison.Ordinal);
        Assert.Contains("BossDeathWhiteoutLease.AssetPaths", character, StringComparison.Ordinal);
        Assert.Contains("color.rgb = mix(color.rgb, vec3(1.0), white_mix)", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("color.a =", shader, StringComparison.Ordinal);
    }

    [Fact]
    public void BossBurstUsesOnlyNinjaSoulAndTheProjectVideo()
    {
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string character = SourceText("Content/NinjaSlayerCharacter.cs");
        string architect = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string controller = SourceText(
            "Code/ExternalAnimations/BossDeathPresentationController.cs");

        Assert.Contains("ninja_slayer_boss_burst.ogv", coordinator, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerNinjaSoulEvent", coordinator, StringComparison.Ordinal);
        Assert.Contains("BossBurstPresentationCoordinator.AssetPaths", character, StringComparison.Ordinal);
        Assert.DoesNotContain("BossDeathExplosionVfx", architect, StringComparison.Ordinal);
        Assert.DoesNotContain("BossDeathExplosionVfx", controller, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/ExternalAnimations/BossDeathExplosionVfx.cs")));
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
            "GameCompatibility.ReporterPass.cs",
            "GameCompatibility.BossBurst.cs"
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
        Assert.DoesNotContain("static class BossBurst", facade, StringComparison.Ordinal);
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

    private static (int Width, int Height) ReadPngDimensions(string relativePath)
    {
        byte[] header = new byte[24];
        using FileStream stream = File.OpenRead(Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        stream.ReadExactly(header);
        if (!header.AsSpan(1, 3).SequenceEqual("PNG"u8))
        {
            throw new InvalidDataException($"{relativePath} is not a PNG file.");
        }

        return (
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
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
