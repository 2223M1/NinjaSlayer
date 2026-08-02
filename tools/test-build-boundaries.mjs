import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const projectPath = join(root, 'NinjaSlayer.csproj');
const privateProjectPath = join(root, 'tools', 'private-contract', 'NinjaSlayer.PrivateBuild.csproj');
const versionPropsPath = join(root, 'eng', 'NinjaSlayer.Version.props');
const versionTargetsPath = join(root, 'eng', 'NinjaSlayer.Version.targets');
const packagingTargetsPath = join(root, 'eng', 'NinjaSlayer.Packaging.targets');
const particlesContainerPath = join(
  root,
  'Code',
  'Nodes',
  'NinjaSlayerNParticlesContainer.cs',
);
const energyVfxScenePaths = [
  join(root, 'NinjaSlayer', 'scenes', 'vfx', 'energy', 'ironclad', 'ironclad_energy_vfx_back.tscn'),
  join(root, 'NinjaSlayer', 'scenes', 'vfx', 'energy', 'ironclad', 'ironclad_energy_vfx_front.tscn'),
];
const compatibilityPath = join(root, 'eng', 'compatibility.json');
const contractProjectPath = join(
  root,
  'Tests',
  'NinjaSlayer.RitsuLibContractTests',
  'NinjaSlayer.RitsuLibContractTests.csproj',
);
const contractWorkflowPath = join(root, '.github', 'workflows', 'contract.yml');
const smokeWorkflowPath = join(root, '.github', 'workflows', 'smoke.yml');
const ciWorkflowPath = join(root, '.github', 'workflows', 'ci.yml');
const releaseWorkflowPath = join(root, '.github', 'workflows', 'release.yml');
const workshopWorkflowPath = join(root, '.github', 'workflows', 'workshop.yml');
const quickReleasePath = join(root, 'tools', 'release', 'Publish-QuickRelease.ps1');
const fastReleasePath = join(root, 'tools', 'release', 'Publish-FastRelease.ps1');
const oneClickReleasePath = join(root, 'tools', 'release', 'Invoke-OneClickRelease.ps1');
const workshopQuickReleasePath = join(
  root,
  'tools',
  'release',
  'Publish-WorkshopQuickRelease.ps1',
);
const channelBuildPath = join(
  root,
  'tools',
  'release',
  'Invoke-NinjaSlayerChannelBuild.ps1',
);
const ephemeralRunnerPath = join(
  root,
  'tools',
  'private-contract',
  'Start-EphemeralContractRunner.ps1',
);
const processNetworkIsolationPath = join(
  root,
  '.github',
  'scripts',
  'process-network-isolation.ps1',
);
const processNetworkIsolationTestPath = join(root, 'tools', 'test-process-network-isolation.ps1');
const smokeLauncherPath = join(root, 'tools', 'smoke-harness', 'Invoke-NinjaSlayerSmoke.ps1');
const smokeDriverProjectPath = join(
  root,
  'tools',
  'smoke-harness',
  'NinjaSlayer.SmokeDriver',
  'NinjaSlayer.SmokeDriver.csproj',
);
const privateRunnerReadmePath = join(root, 'tools', 'private-contract', 'README.md');
const contractVerifierPath = join(root, '.github', 'scripts', 'verify-contract-attestation.ps1');
const smokeVerifierPath = join(root, '.github', 'scripts', 'verify-smoke-attestation.ps1');
const networkProbeProjectPath = join(
  root,
  'Tests',
  'NinjaSlayer.NetworkIsolationProbe',
  'NinjaSlayer.NetworkIsolationProbe.csproj',
);
const compatibility = JSON.parse(readFileSync(compatibilityPath, 'utf8'));
const contractProject = readFileSync(contractProjectPath, 'utf8');
const contractWorkflow = readFileSync(contractWorkflowPath, 'utf8');
const smokeWorkflow = readFileSync(smokeWorkflowPath, 'utf8');
const ciWorkflow = readFileSync(ciWorkflowPath, 'utf8');
const processNetworkIsolation = readFileSync(processNetworkIsolationPath, 'utf8');
const processNetworkIsolationTest = readFileSync(processNetworkIsolationTestPath, 'utf8');
const smokeLauncher = readFileSync(smokeLauncherPath, 'utf8');
const smokeDriverProject = readFileSync(smokeDriverProjectPath, 'utf8');
const privateRunnerReadme = readFileSync(privateRunnerReadmePath, 'utf8');
const contractVerifier = readFileSync(contractVerifierPath, 'utf8');
const smokeVerifier = readFileSync(smokeVerifierPath, 'utf8');
const networkProbeProject = readFileSync(networkProbeProjectPath, 'utf8');
const particlesContainer = readFileSync(particlesContainerPath, 'utf8');
const energyVfxScenes = energyVfxScenePaths.map((path) => readFileSync(path, 'utf8'));
const defaultChannel = compatibility.defaultBuildChannel;
const channelBuild = readFileSync(channelBuildPath, 'utf8');

function xml(value) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function runMsbuild(project, target, properties = {}) {
  const args = ['msbuild', project, '-nologo', '-v:minimal', `-t:${target}`];
  for (const [name, value] of Object.entries(properties)) args.push(`-p:${name}=${value}`);
  return spawnSync('dotnet', args, { cwd: root, encoding: 'utf8' });
}

function requireSuccess(result, operation) {
  assert.equal(
    result.status,
    0,
    `${operation} failed.\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`,
  );
}

function evaluateCompileFiles(project, properties = {}) {
  const args = ['msbuild', project, '-nologo', '-getItem:Compile'];
  for (const [name, value] of Object.entries(properties)) args.push(`-p:${name}=${value}`);
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8' });
  requireSuccess(result, `Compile item evaluation for ${project}`);

  let evaluation;
  try {
    evaluation = JSON.parse(result.stdout);
  } catch (error) {
    assert.fail(`MSBuild returned invalid Compile item JSON for ${project}: ${error.message}`);
  }

  return (evaluation.Items?.Compile ?? []).map((item) => {
    const fullPath = resolve(
      item.FullPath
        ?? (isAbsolute(item.Identity) ? item.Identity : join(dirname(project), item.Identity)),
    );
    const repositoryPath = relative(root, fullPath).replaceAll('\\', '/');
    assert(
      repositoryPath !== '..' && !repositoryPath.startsWith('../'),
      `${project} compiles a source outside the candidate repository: ${fullPath}`,
    );
    return repositoryPath;
  }).sort();
}

function evaluateProperties(project, names, properties = {}) {
  const args = ['msbuild', project, '-nologo', `-getProperty:${names.join(',')}`];
  for (const [name, value] of Object.entries(properties)) args.push(`-p:${name}=${value}`);
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8' });
  requireSuccess(result, `Property evaluation for ${project}`);

  try {
    return JSON.parse(result.stdout).Properties;
  } catch (error) {
    assert.fail(`MSBuild returned invalid property JSON for ${project}: ${error.message}`);
  }
}

function fileHash(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex').toUpperCase();
}

function xmlElement(source, element, name) {
  const match = source.match(new RegExp(`<${element}\\s+[^>]*${name}[^>]*>([\\s\\S]*?)<\\/${element}>`));
  assert(match, `Missing ${element} ${name}.`);
  return match[0];
}

const project = readFileSync(projectPath, 'utf8');
assert(project.includes('<Import Project="eng\\NinjaSlayer.Version.props" />'));
assert(project.includes('<Import Project="eng\\NinjaSlayer.Version.targets" />'));
assert(project.includes('<Import Project="eng\\NinjaSlayer.Packaging.targets" />'));
assert(!project.includes('<Target '), 'NinjaSlayer.csproj must not own executable delivery targets.');
assert(!project.includes('<UsingTask'), 'NinjaSlayer.csproj must not own delivery task implementations.');
assert(!project.includes('AfterTargets='), 'Ordinary builds must not trigger delivery through AfterTargets.');
assert(
  project.includes('<Compile Remove="build\\**\\*.cs" />'),
  'Channel-local generated sources under build/ must never enter the shipping compilation.',
);
const isolatedPathRoot = join(tmpdir(), 'ninjaslayer-isolated-output-contract');
const evaluatedPaths = evaluateProperties(
  projectPath,
  ['OutputPath', 'IntermediateOutputPath'],
  {
    Configuration: 'Release',
    NinjaSlayerIsolatedOutputRoot: join(isolatedPathRoot, 'bin'),
    NinjaSlayerIsolatedIntermediateRoot: join(isolatedPathRoot, 'obj'),
  },
);
assert.equal(resolve(evaluatedPaths.OutputPath), resolve(isolatedPathRoot, 'bin', 'Release'));
assert.equal(
  resolve(evaluatedPaths.IntermediateOutputPath),
  resolve(isolatedPathRoot, 'obj', 'Release'),
);
for (const property of ['PostBuildModDir', 'SteamModDir', 'WorkshopContentDir', 'WorkshopUploaderExe']) {
  assert(!project.includes(`<${property}`), `${property} belongs in the packaging import.`);
}

const packagingTargets = readFileSync(packagingTargetsPath, 'utf8');
const releaseWorkflow = readFileSync(releaseWorkflowPath, 'utf8');
const workshopWorkflow = readFileSync(workshopWorkflowPath, 'utf8');
const quickRelease = readFileSync(quickReleasePath, 'utf8');
const fastRelease = readFileSync(fastReleasePath, 'utf8');
const oneClickRelease = readFileSync(oneClickReleasePath, 'utf8');
const workshopQuickRelease = readFileSync(workshopQuickReleasePath, 'utf8');
const ephemeralRunner = readFileSync(ephemeralRunnerPath, 'utf8');
const stableTagPattern = '^v(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$';
for (const [channelName, channel] of Object.entries(compatibility.channels)) {
  for (const assembly of ['SmartFormat.dll', 'SmartFormat.ZString.dll', 'Steamworks.NET.dll']) {
    assert(
      channel.runtimeAssemblies.includes(assembly),
      `${channelName}.runtimeAssemblies must include ${assembly}.`,
    );
  }
}
assert(contractProject.includes('<_NinjaSlayerRuntimeReference Include="$(NinjaSlayerRuntimeAssemblies)" />'));
assert(contractProject.includes('<HintPath>$(Sts2DataDir)/%(Identity)</HintPath>'));
assert(!contractProject.includes('<Reference Include="Sentry"'));
for (const workflow of [contractWorkflow, smokeWorkflow]) {
  assert(workflow.includes('ref: ${{ github.sha }}'));
  assert(workflow.includes('WORKFLOW_SHA: ${{ github.sha }}'));
  assert(workflow.includes('$candidateSha -cne $workflowSha'));
  assert(!workflow.includes('ref: main'));
}
assert(contractWorkflow.includes('$programs = @($dotnet, $godot)'));
assert(contractWorkflow.includes('must be elevated for process firewall isolation'));
assert(contractWorkflow.includes('-RemoteScope NonLoopback'));
assert(contractWorkflow.includes('NINJASLAYER_CONTRACT_DOTNET_EXE'));
assert(contractWorkflow.includes('NinjaSlayer.NetworkIsolationProbe.dll'));
assert(contractWorkflow.includes("NINJASLAYER_CONTRACT_REQUIRE_NETWORK_ISOLATION = '1'"));
assert(!contractWorkflow.includes('New-NetFirewallRule'));
assert(!contractWorkflow.includes('$rule = "NinjaSlayer-Contract'));
assert(smokeWorkflow.includes('Invoke-NinjaSlayerSmoke.ps1'));
assert(smokeLauncher.includes('New-NinjaSlayerProcessFirewallLease'));
assert(smokeLauncher.includes('-RemoteScope All'));
assert(smokeLauncher.includes('Remove-NinjaSlayerProcessFirewallLease'));
assert(!smokeLauncher.includes('New-NetFirewallRule'));
assert(
  smokeDriverProject.includes(
    '<PackageDownload Include="$(NinjaSlayerRitsuLibPackageId)" Version="[$(NinjaSlayerRitsuLibVersion)]" />',
  ),
  'SmokeDriver must download the channel-specific pinned RitsuLib compile package.',
);
assert(
  smokeDriverProject.includes(
    "<PinnedRitsuLibAssemblyPath>$([System.IO.Path]::Combine('$(NuGetPackageRoot)'",
  ),
  'SmokeDriver must resolve the pinned DLL from the active NuGet package root.',
);
assert(smokeDriverProject.includes('<HintPath>$(PinnedRitsuLibAssemblyPath)</HintPath>'));
assert(!smokeDriverProject.includes('<PackageReference Include="$(NinjaSlayerRitsuLibPackageId)"'));
assert(!smokeLauncher.includes("Add-MsBuildProperty $driverArguments 'RitsuLibAssemblyPath'"));
assert(smokeLauncher.includes('Copy-Item -LiteralPath $RitsuLibModDirectory'));
for (const required of [
  '0.0.0.0-126.255.255.255',
  '128.0.0.0-255.255.255.255',
  '::-::',
  '::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff',
  '[Collections.Generic.List[object]]::new()',
  'for ($index = $ruleNames.Count - 1; $index -ge 0; $index--)',
  'Get-NetFirewallRule -Name $ruleName',
]) {
  assert(processNetworkIsolation.includes(required), `Process firewall helper is missing: ${required}`);
}
for (const required of [
  'Duplicate executable paths must produce one firewall rule.',
  'Injected firewall creation failure',
  'Injected isolated action failure.',
  'cleanup must remove rules in reverse order.',
]) {
  assert(processNetworkIsolationTest.includes(required), `Process firewall tests are missing: ${required}`);
}
assert(ciWorkflow.includes('./tools/test-process-network-isolation.ps1'));
assert(ciWorkflow.includes('./tools/test-compatibility-powershell.ps1'));
assert(ciWorkflow.includes('powershell-compatibility:'));
assert(ciWorkflow.includes('runs-on: windows-latest'));
assert(ciWorkflow.includes('shell: powershell'));
assert(ciWorkflow.includes('needs: powershell-compatibility'));
assert(ciWorkflow.includes('if: ${{ always() }}'));
assert(ciWorkflow.includes("if: ${{ needs.powershell-compatibility.result != 'success' }}"));
assert(ciWorkflow.includes('PowerShell 5.1 compatibility job must succeed.'));
assert(ciWorkflow.includes('Tests/NinjaSlayer.NetworkIsolationProbe/NinjaSlayer.NetworkIsolationProbe.csproj'));
assert(networkProbeProject.includes('<UseAppHost>false</UseAppHost>'));
assert(privateRunnerReadme.includes('| Contract | `4` |'));
assert(privateRunnerReadme.includes('| Smoke | `3` |'));
assert(contractVerifier.includes('$attestation.schemaVersion) 4'));
assert(smokeVerifier.includes('$attestation.schemaVersion) 3'));
assert(!privateRunnerReadme.includes('Contract and Smoke artifacts contain only schema 3'));
for (const source of [releaseWorkflow, workshopWorkflow]) {
  assert(
    source.includes(stableTagPattern),
    'Release and Workshop workflows must enforce stable SemVer tags.',
  );
}
assert(releaseWorkflow.includes('environment: release-production'));
assert(releaseWorkflow.includes('Assert-NinjaSlayerImmutableReleasesEnabled'));
assert(
  releaseWorkflow.match(/Test-NinjaSlayerGitHubReleaseExists/g)?.length === 2,
  'Release must use the structured REST probe before building and before publishing.',
);
assert(
  !releaseWorkflow.includes('gh release view'),
  'Windows PowerShell 5.1 must not probe an absent release through native stderr.',
);
assert(releaseWorkflow.includes('workflow_dispatch:'));
assert(releaseWorkflow.includes('RELEASE_POLICY_TOKEN: ${{ secrets.RELEASE_POLICY_TOKEN }}'));
assert(releaseWorkflow.includes('-Token $env:RELEASE_POLICY_TOKEN'));
assert(
  releaseWorkflow.includes('release-production must provide RELEASE_POLICY_TOKEN'),
  'Release must fail clearly when its immutable-policy credential is unavailable.',
);
const releaseCheckoutStart = releaseWorkflow.indexOf('- name: Checkout complete history');
const releaseValidationStart = releaseWorkflow.indexOf('- name: Validate new release tag at origin/main HEAD');
assert(
  releaseCheckoutStart >= 0 && releaseValidationStart > releaseCheckoutStart,
  'Release checkout and validation steps must remain ordered.',
);
const releaseCheckout = releaseWorkflow.slice(
  releaseCheckoutStart,
  releaseValidationStart,
);
assert(
  releaseCheckout.includes('persist-credentials: false'),
  'Release checkout must not persist workflow credentials.',
);
assert(
  releaseCheckout.includes('set-safe-directory: false'),
  'Release checkout must not create a temporary global safe-directory config.',
);
assert(
  !releaseWorkflow.includes('shell: pwsh'),
  'The dedicated Windows Release runner must not require PowerShell 7.',
);
const releaseCommandFileWrites = releaseWorkflow
  .split('\n')
  .filter(
    (line) =>
      line.includes('Out-File')
      && (line.includes('$env:GITHUB_ENV') || line.includes('$env:GITHUB_OUTPUT')),
  );
assert(releaseCommandFileWrites.length > 0, 'Release must publish command-file outputs.');
assert(
  releaseCommandFileWrites.every((line) => line.includes('-Encoding utf8')),
  'Windows PowerShell 5.1 must write GitHub command files as UTF-8.',
);
assert(!releaseWorkflow.includes("tags:\n      - 'v0.1.*'"), 'Tag pushes must not automatically queue the protected release path.');
assert(releaseWorkflow.includes('git rev-list -n 1 $env:RELEASE_TAG'));
assert(releaseWorkflow.includes('Exactly two release archives are required.'));
assert(releaseWorkflow.includes('$releaseSha -ne $originMain'));
assert(releaseWorkflow.includes("'${{ github.ref }}' -cne 'refs/heads/main'"));
assert(releaseWorkflow.includes("$workflowSha = '${{ github.sha }}'.ToLowerInvariant()"));
assert(releaseWorkflow.includes('Invoke-NinjaSlayerChannelBuild.ps1'));
assert(releaseWorkflow.includes('new-release-attestation.ps1'));
assert(
  releaseWorkflow.includes(
    'protected-release-${{ env.RELEASE_TAG }}-${{ steps.release.outputs.release_sha }}',
  )
    && releaseWorkflow.includes('path: ${{ steps.package.outputs.protected_release_dir }}'),
  'The protected artifact name and path must use explicit step outputs.',
);
assert(!releaseWorkflow.includes('--clobber'));
assert(!releaseWorkflow.includes("@('release', 'upload'"));
assert(releaseWorkflow.includes('$channel-sts2-$($channelHost.gameApiVersion).zip'));
assert(workshopWorkflow.includes("'${{ github.ref }}' -cne 'refs/heads/main'"));
assert(workshopWorkflow.includes('verify-release-attestation.ps1'));
assert(workshopWorkflow.includes('The public GitHub Release asset does not match'));
assert(
  workshopWorkflow.includes('node ./tools/package-contract.mjs validate-manifest'),
  'Workshop manifest validation must use a path that works on Linux runners.',
);
assert(
  !workshopWorkflow.includes('node .\\tools\\package-contract.mjs'),
  'Workshop manifest validation must not use a Windows-only path.',
);
assert(
  releaseWorkflow.includes('runs-on: [self-hosted, Windows, X64, ninjaslayer-release]'),
  'Release packaging must run only on the dedicated ephemeral release runner.',
);
for (const safeguard of [
  "[ValidatePattern('^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$')]",
  "if (-not $Confirm)",
  "if ($branch -ne 'main')",
  "if ($head -ne $originMain)",
  "if (-not $SkipWorkshop)",
  "'workflow'",
  "'release.yml'",
  '"release_tag=$tag"',
]) {
  assert(quickRelease.includes(safeguard), `Quick release must retain safeguard: ${safeguard}`);
}
for (const forbidden of [
  "@('release', 'create'",
  "@('release', 'upload'",
  'Compress-Archive',
  'CreateFromDirectory',
]) {
  assert(!quickRelease.includes(forbidden), `Quick release must not bypass protected Release with ${forbidden}.`);
}
for (const forwardedParameter of [
  '$workshopParameters.Sts2DataDir = $Sts2DataDir',
  '$workshopParameters.SteamModDir = $SteamModDir',
  '$workshopParameters.GodotExe = $GodotExe',
]) {
  assert(
    quickRelease.includes(forwardedParameter),
    `Quick release must forward the local stable build parameter: ${forwardedParameter}`,
  );
}
// The routine one-click path is a thin wrapper over the local fast publisher. The publisher builds
// and validates both channels before performing any irreversible repository or remote operation.
assert(
  oneClickRelease.includes("Join-Path $PSScriptRoot 'Publish-FastRelease.ps1'"),
  'One-click release must delegate to the fast official publisher.',
);
assert(
  oneClickRelease.includes('& $publisher @PSBoundParameters'),
  'One-click release must forward the caller parameters without owning release logic.',
);
for (const safeguard of [
  'if (-not $DryRun -and -not $Confirm)',
  'if ($AllowDirty -and -not $DryRun)',
  "if ($branch -ne 'main')",
  'if ($script:head -ne $originMain)',
  'Read-NinjaSlayerPackageArchive',
  '[IO.Compression.CompressionLevel]::NoCompression',
  "'release', 'create', $tag",
  "'tag', '-a', $tag",
  "'push', 'origin', $tag",
  "'upload', '-w', 'NinjaSlayer'",
  'ReuseCache = -not $CleanBuildCache',
  'reusable = -not $AllowDirty',
  '$state.reusable -ne $true',
  'function Assert-ReleaseNoteIsFresh',
  'Release note must be tracked and committed before publishing',
  'Release note matches the previous release',
  'Where-Object { $_.Version -lt $requestedVersion }',
  'appeared remotely on a different commit during packaging',
  'Preparation exceeded the $BudgetSeconds-second budget before publication',
]) {
  assert(fastRelease.includes(safeguard), `Fast release must retain safeguard: ${safeguard}`);
}
assert(
  fastRelease.indexOf('New-ExactPackageArchive $packageDirectory')
    < fastRelease.indexOf("Invoke-Native git @('tag', '-a', $tag"),
  'Fast release must finish archive creation before creating the release tag.',
);
assert(
  fastRelease.indexOf("if ($DryRun)")
    < fastRelease.indexOf("Invoke-Native git @('tag', '-a', $tag"),
  'DryRun must return before creating the release tag.',
);
assert(
  fastRelease.indexOf('Assert-ReleaseNoteIsFresh -ReleaseNotePath')
    < fastRelease.indexOf("Invoke-Native git @('tag', '-a', $tag"),
  'Fast release must verify the committed release note before creating the release tag.',
);
for (const slowGate of [
  'verify-contract-attestation.ps1',
  'verify-smoke-attestation.ps1',
  'Start-EphemeralContractRunner.ps1',
  "'workflow', 'run'",
]) {
  assert(!fastRelease.includes(slowGate), `Routine fast release must not wait for ${slowGate}.`);
}
assert(channelBuild.includes('[switch]$ReuseCache'));
assert(channelBuild.includes('if (-not $ReuseCache)'));

// The Workshop publisher may read local tags to pick the next version, but nothing it does may
// mutate the repository or reach GitHub.
assert(
  workshopQuickRelease.includes("if (-not $Confirm)"),
  'Workshop quick release must stay disabled without -Confirm.',
);
assert(
  workshopQuickRelease.includes("[ValidatePattern('^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$')]"),
  'Workshop quick release must enforce stable SemVer.',
);
assert(
  workshopQuickRelease.includes('Package checksum mismatch'),
  'Workshop quick release must verify SHA256SUMS before upload.',
);
assert(
  workshopQuickRelease.includes('NINJASLAYER_STS2_STABLE_DATA_DIR')
    && workshopQuickRelease.includes('Invoke-NinjaSlayerChannelBuild.ps1')
    && workshopQuickRelease.includes("Target = 'InstallLocalAndStageWorkshop'"),
  'Workshop quick release must use the isolated explicit stable channel build.',
);
assert(
  workshopQuickRelease.includes(
    "Invoke-Native -Command $uploader -Arguments @('upload', '-w', 'NinjaSlayer')",
  ),
  'Workshop quick release must upload through the local uploader.',
);
assert(
  workshopQuickRelease.includes(
    'GitHub commits, tags, pushes, pull requests, and Releases are disabled for this path.',
  ),
  'Workshop quick release must declare its GitHub boundary.',
);
for (const forbidden of [
  "Invoke-Native -Command gh",
  "Invoke-Native -Command git",
  "@('add'",
  "@('commit'",
  "@('push'",
  "@('tag'",
]) {
  assert(
    !workshopQuickRelease.includes(forbidden),
    `Workshop quick release must not perform the repository operation: ${forbidden}`,
  );
}
assert(releaseWorkflow.includes('if (-not $file.IsReadOnly)'));
assert(releaseWorkflow.includes('must remain outside the repository workspace'));
assert(releaseWorkflow.includes('NINJASLAYER_SPINE_DIR'));
assert.equal(
  releaseWorkflow.match(/\$workspace = \[IO\.Path\]::GetFullPath\(\$env:GITHUB_WORKSPACE\)/g)?.length,
  2,
  'Each PowerShell input-validation step must initialize its own workspace boundary.',
);
assert(
  releaseWorkflow.includes('854D827B8926B00BA6459093033BF0C0898EFA2B6E1C85EB0ABC78CA153EA58C'),
  'Release packaging must pin the verified Spine extension hash.',
);
assert(smokeWorkflow.includes('NINJASLAYER_SPINE_DIR'));
assert(smokeWorkflow.includes('Install verified Spine extension'));
assert(
  smokeWorkflow.includes('854D827B8926B00BA6459093033BF0C0898EFA2B6E1C85EB0ABC78CA153EA58C'),
  'Smoke packaging must pin the verified Spine extension hash.',
);
assert(
  smokeWorkflow.indexOf('Install verified Spine extension')
    < smokeWorkflow.indexOf('Run isolated stable and preview smoke'),
  'Smoke must install the verified Spine extension before invoking PackageMod.',
);
for (const forbidden of [
  'STS2_REFERENCE_BUNDLE_URL',
  'STS2_REFERENCE_BUNDLE_TOKEN',
  'UseSts2RefLib',
  'windows-latest',
]) {
  assert(!releaseWorkflow.includes(forbidden), `Release workflow must not contain ${forbidden}.`);
}
for (const required of [
  "[ValidateSet('Contract', 'Release', 'Smoke')]",
  "'Contract' { 'ninjaslayer-contract' }",
  "'Release' { 'ninjaslayer-release' }",
  "'Smoke' { 'ninjaslayer-smoke' }",
  "Set-RunnerEnvironment -Name 'NINJASLAYER_SPINE_DIR'",
  'Set-RunnerEnvironment -Name $name -Value $previousEnvironment[$name]',
  '[string]$GameDataDirectoryStable',
  '[string]$GameDataDirectoryPreview',
  '[string]$GameRootDirectoryStable',
  '[string]$GameRootDirectoryPreview',
  "'NINJASLAYER_STS2_STABLE_DATA_DIR'",
  "'NINJASLAYER_STS2_PREVIEW_DATA_DIR'",
  "'NINJASLAYER_SMOKE_STABLE_GAME_ROOT'",
  "'NINJASLAYER_SMOKE_PREVIEW_GAME_ROOT'",
  "Read-NinjaSlayerCompatibility -Path (Join-Path $repositoryRoot 'eng\\compatibility.json')",
  'must be at least $minimumRitsuVersion',
  'Remove-SessionDirectory -Path $sessionRoot',
]) {
  assert(ephemeralRunner.includes(required), `Ephemeral runner launcher is missing: ${required}`);
}
const spineValidationBlock = `if ($RunnerPurpose -in @('Release', 'Smoke')) {
    foreach ($fileName in $requiredSpineFiles) {
        $source = Join-Path $SpineExtensionDirectory $fileName`;
assert(
  ephemeralRunner.replaceAll('\r\n', '\n').includes(spineValidationBlock),
  'Release and Smoke runners must validate every Spine input.',
);
const spineIsolationBlock = `    if ($RunnerPurpose -in @('Release', 'Smoke')) {
        New-Item -ItemType Directory -Path $spineDirectory -Force | Out-Null
        foreach ($fileName in $requiredSpineFiles) {
            $destination = Join-Path $spineDirectory $fileName
            Copy-Item -LiteralPath (Join-Path $SpineExtensionDirectory $fileName) -Destination $destination
            (Get-Item -LiteralPath $destination).IsReadOnly = $true`;
assert(
  ephemeralRunner.replaceAll('\r\n', '\n').includes(spineIsolationBlock),
  'Release and Smoke runners must copy their Spine inputs into a read-only isolated directory.',
);
assert(
  ephemeralRunner.includes(
    "-Value $(if ($RunnerPurpose -in @('Release', 'Smoke')) { $spineDirectory } else { $null })",
  ),
  'Release and Smoke runners must expose only the isolated Spine directory.',
);
assert(
  !packagingTargets.includes('BeforeTargets=') && !packagingTargets.includes('AfterTargets='),
  'Delivery targets must remain explicit and must not attach themselves to ordinary builds.',
);
for (const target of [
  'ValidatePackageHost',
  'BuildGodotEditorAssembly',
  'ImportGodotProjectForPackage',
  'ResolveLocalInstallVersion',
  'PackageMod',
  'InstallLocal',
  'ValidateWorkshopPublish',
  'StageWorkshop',
  'PublishWorkshop',
]) {
  assert(packagingTargets.includes(`<Target Name="${target}"`), `Missing delivery target ${target}.`);
}
assert(
  packagingTargets.includes(
    'BuildGodotEditorAssembly;Build;ImportGodotProjectForPackage;SyncFmodBankForPackage',
  ),
  'Packaging must import Godot resources after the final build and before export.',
);
const editorBuildTarget = xmlElement(
  packagingTargets,
  'Target',
  'Name="BuildGodotEditorAssembly"',
);
assert(editorBuildTarget.includes(`Condition="'$(Configuration)' != 'Debug'"`));
assert(editorBuildTarget.includes('Targets="Build"'));
assert(
  editorBuildTarget.includes(
    'Properties="Configuration=Debug;GodotDisabledSourceGenerators=ScriptPathAttribute"',
  ),
  'The export-only editor assembly must not register game-dependent mod scripts.',
);
assert(editorBuildTarget.includes('BuildInParallel="false"'));
assert(
  editorBuildTarget.includes(
    'RemoveProperties="BaseOutputPath;NinjaSlayerIsolatedOutputRoot"',
  ),
  'The Godot editor assembly must be written to .godot instead of the isolated package output.',
);
const importGodotTarget = xmlElement(
  packagingTargets,
  'Target',
  'Name="ImportGodotProjectForPackage"',
);
assert(
  importGodotTarget.includes('&quot;$(GodotExe)&quot; --headless --import'),
  'Packaging must wait for Godot to import resources before export.',
);
assert(importGodotTarget.includes('WorkingDirectory="$(MSBuildProjectDirectory)"'));
assert(
  importGodotTarget.includes(
    'EnvironmentVariables="IsInnerGodotExport=true;MSBUILDDISABLENODEREUSE=1"',
  ),
);
const godotErrorRegex = 'System\\.[A-Za-z0-9_.]+Exception:|SCRIPT ERROR:|ERROR:';
assert(
  importGodotTarget.includes(`CustomErrorRegularExpression="${godotErrorRegex}"`),
  'Godot import must treat managed and Godot errors as build failures.',
);
const exportGodotTarget = xmlElement(
  packagingTargets,
  'Target',
  'Name="ExportPckForPackage"',
);
assert(
  exportGodotTarget.includes(`CustomErrorRegularExpression="${godotErrorRegex}"`),
  'Godot import and export must use the same error detection.',
);
assert(
  !particlesContainer.includes('[Export(')
    && !particlesContainer.includes('private new Array<GpuParticles2D>'),
  'Cross-assembly private NParticlesContainer fields must not be serialized by mod scenes.',
);
const particlesAssignment = particlesContainer.indexOf('ParticlesField.SetValue(this, particles);');
const baseReady = particlesContainer.indexOf('base._Ready();');
assert(
  particlesAssignment >= 0 && baseReady > particlesAssignment,
  'The base particle field must be populated before the inherited ready lifecycle runs.',
);
for (const scene of energyVfxScenes) {
  assert(
    !scene.includes('_particles'),
    'Energy VFX scenes must let NinjaSlayerNParticlesContainer discover direct particle children.',
  );
}
assert(
  packagingTargets.includes(
    '<InstallLocalDependsOn Condition="\'$(InstallLocalDependsOn)\' == \'\'">ResolveLocalInstallVersion;PackageMod</InstallLocalDependsOn>',
  ),
  'Local installation must resolve a Workshop-safe local version before packaging.',
);
assert(
  packagingTargets.includes('ValidateWorkshopPublish;PackageMod;StageWorkshop'),
  'Workshop publication must validate before packaging or staging.',
);
for (const required of [
  "[ValidateSet('stable', 'preview')]",
  "[ValidateSet('PackageMod', 'InstallLocal', 'StageWorkshop', 'InstallLocalAndStageWorkshop')]",
  "'BaseIntermediateOutputPath'",
  "'MSBuildProjectExtensionsPath'",
  "'BaseOutputPath'",
  "'NinjaSlayerIsolatedIntermediateRoot'",
  "'NinjaSlayerIsolatedOutputRoot'",
  "'PostBuildModDir'",
  "'project.assets.json'",
  '$expectedPackageIdentity',
  'compatibility.ritsuLibVersion',
  "Get-NinjaSlayerGameModuleMvid",
]) {
  assert(channelBuild.includes(required), `Channel build entry is missing: ${required}`);
}
assert(
  packagingTargets.includes('CustomErrorRegularExpression="System\\.[A-Za-z0-9_.]+Exception:|SCRIPT ERROR:|ERROR:"'),
  'Godot export must fail when the editor reports a managed or Godot error with exit code zero.',
);
const sts2Reference = xmlElement(project, 'Reference', 'Include="sts2"');
const harmonyReference = xmlElement(project, 'Reference', 'Include="0Harmony"');
assert(
  sts2Reference.includes('<Private>true</Private>')
    && harmonyReference.includes('<Private>true</Private>'),
  'Local game references must be present in the Godot editor dependency context.',
);
assert.deepEqual(
  evaluateCompileFiles(privateProjectPath, {
    CandidateRoot: root,
    NinjaSlayerHostChannel: defaultChannel,
  }),
  evaluateCompileFiles(projectPath, { NinjaSlayerHostChannel: defaultChannel }),
  'The private contract must compile exactly the shipping project source set.',
);

const sandbox = mkdtempSync(join(tmpdir(), 'ninjaslayer-build-boundaries-'));
try {
  const privateReferenceDir = join(sandbox, 'private-references');
  mkdirSync(privateReferenceDir);
  writeFileSync(join(privateReferenceDir, 'sts2.dll'), 'contract-fixture', 'utf8');
  writeFileSync(join(privateReferenceDir, '0Harmony.dll'), 'contract-fixture', 'utf8');

  const missingChannel = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    CandidateRoot: root,
    Sts2DataDir: privateReferenceDir,
  });
  assert.notEqual(missingChannel.status, 0, 'The private build must require an explicit channel.');
  assert.match(
    `${missingChannel.stdout}\n${missingChannel.stderr}`,
    /requires an explicit NinjaSlayerHostChannel=stable\|preview/,
  );

  const emptyCandidate = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerHostChannel: defaultChannel,
  });
  assert.notEqual(emptyCandidate.status, 0, 'An empty CandidateRoot must fail validation.');
  const emptyCandidateOutput = `${emptyCandidate.stdout}\n${emptyCandidate.stderr}`;
  assert.match(
    emptyCandidateOutput,
    /CandidateRoot is required and must point to a checked-out NinjaSlayer repository\./,
  );
  assert.doesNotMatch(
    emptyCandidateOutput,
    /MSB4184/,
    'An empty CandidateRoot must not reach Path.GetFullPath.',
  );

  const invalidCandidate = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    CandidateRoot: sandbox,
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerHostChannel: defaultChannel,
  });
  assert.notEqual(invalidCandidate.status, 0, 'An invalid CandidateRoot must fail validation.');
  assert.match(
    `${invalidCandidate.stdout}\n${invalidCandidate.stderr}`,
    /does not contain NinjaSlayer\.csproj\./,
  );

  const versionHarnessPath = join(sandbox, 'VersionHarness.proj');
  writeFileSync(versionHarnessPath, `
<Project>
  <Import Project="${xml(versionPropsPath)}" />
  <Import Project="${xml(versionTargetsPath)}" />
  <Import Project="${xml(packagingTargetsPath)}" />
  <Target Name="CaptureVersion" DependsOnTargets="ResolveNinjaSlayerVersion">
    <WriteLinesToFile File="$(CaptureFile)" Lines="$(NinjaSlayerVersion)|$(IsExactReleaseTag)|$(IsSupportedReleaseTag)|$(GitTag)" Overwrite="true" />
  </Target>
  <Target Name="CaptureLocalInstallVersion" DependsOnTargets="ResolveLocalInstallVersion">
    <WriteLinesToFile File="$(CaptureFile)" Lines="$(NinjaSlayerVersion)" Overwrite="true" />
  </Target>
</Project>
`.trimStart(), 'utf8');
  const versionCases = [
    ['v0.1.0-0-gabcdef', '', '0.1.0|true|true|v0.1.0'],
    ['v0.1.100-0-gabcdef', '', '0.1.100|true|true|v0.1.100'],
    ['v2.3.4-0-gabcdef', '', '2.3.4|true|true|v2.3.4'],
    ['v0.1.7-0-gabcdef-dirty', '', '0.1.8-dev.0+gabcdef.dirty|false|false|'],
    ['v2.3.4-7-gabcdef', '', '2.3.5-dev.7+gabcdef|false|false|'],
    ['v2.3.4-7-gabcdef-dirty', '', '2.3.5-dev.7+gabcdef.dirty|false|false|'],
    ['v0.1.20-7-gabcdef', 'v0.1.27|v0.1.24|v0.1.20', '0.1.28-dev.7+gabcdef|false|false|'],
    ['v01.2.3-0-gabcdef', '', '0.1.0-dev.0+gabcdef|false|false|'],
    ['abcdef', '', '0.1.0-dev.0+gabcdef|false|false|'],
  ];
  for (const [describe, releaseTags, expected] of versionCases) {
    const captureFile = join(sandbox, `version-${describe.replaceAll(/[^a-z0-9]/gi, '-')}.txt`);
    const properties = {
      CaptureFile: captureFile,
      GitDescribe: describe,
    };
    if (releaseTags) properties.GitReleaseTags = releaseTags;
    requireSuccess(runMsbuild(versionHarnessPath, 'CaptureVersion', properties), `version resolution for ${describe}`);
    assert.equal(readFileSync(captureFile, 'utf8').trim(), expected);
  }

  const localVersionCapture = join(sandbox, 'version-local-install.txt');
  requireSuccess(
    runMsbuild(versionHarnessPath, 'CaptureLocalInstallVersion', {
      CaptureFile: localVersionCapture,
      GitDescribe: 'v0.1.28-0-gabcdef-dirty',
      GitReleaseTags: 'v0.1.28|v0.1.27',
    }),
    'local install version resolution',
  );
  assert.equal(
    readFileSync(localVersionCapture, 'utf8').trim(),
    '0.1.29+local.gabcdef.dirty',
  );

  const packageDir = join(sandbox, 'package');
  const installDir = join(sandbox, 'installed');
  const workshopDir = join(sandbox, 'workshop');
  const harnessPath = join(sandbox, 'BuildBoundaryHarness.proj');
  const harness = `
<Project>
  <PropertyGroup>
    <NinjaSlayerVersion>0.1.7</NinjaSlayerVersion>
    <GitDescribe>v0.1.7-0-gabcdef</GitDescribe>
    <IsWindows>true</IsWindows>
    <Configuration>Debug</Configuration>
    <NinjaSlayerHostChannel>stable</NinjaSlayerHostChannel>
    <NinjaSlayerHostChannelWasExplicit>true</NinjaSlayerHostChannelWasExplicit>
    <NinjaSlayerDistributionChannel>public</NinjaSlayerDistributionChannel>
    <NinjaSlayerArtifactName>NinjaSlayer</NinjaSlayerArtifactName>
    <PostBuildModDir>${xml(packageDir)}</PostBuildModDir>
    <SteamModDir>${xml(installDir)}</SteamModDir>
    <WorkshopContentDir>${xml(workshopDir)}</WorkshopContentDir>
    <WorkshopUploadRoot>${xml(sandbox)}</WorkshopUploadRoot>
    <WorkshopUploaderExe>${xml(join(sandbox, 'must-not-run.exe'))}</WorkshopUploaderExe>
    <PackageModDependsOn>PrepareBuildTestPackage;GeneratePackageChecksums</PackageModDependsOn>
    <StageWorkshopDependsOn>PrepareBuildTestPackage;GeneratePackageChecksums;RequireExplicitPackageHostChannel</StageWorkshopDependsOn>
  </PropertyGroup>
  <Import Project="${xml(versionPropsPath)}" />
  <Import Project="${xml(versionTargetsPath)}" />
  <Import Project="${xml(packagingTargetsPath)}" />
  <Target Name="PrepareBuildTestPackage">
    <MakeDir Directories="$(PostBuildModDir)" />
    <WriteLinesToFile File="$(PostBuildModDir)NinjaSlayer.dll" Lines="dll-fixture" Overwrite="true" />
    <WriteLinesToFile File="$(PostBuildModDir)NinjaSlayer.json" Lines="json-fixture" Overwrite="true" />
    <WriteLinesToFile File="$(PostBuildModDir)NinjaSlayer.pck" Lines="pck-fixture" Overwrite="true" />
  </Target>
</Project>
`;
  writeFileSync(harnessPath, harness.trimStart(), 'utf8');

  const fakeGodotPath = join(sandbox, process.platform === 'win32' ? 'fake-godot.cmd' : 'fake-godot.sh');
  const fakeGodot = process.platform === 'win32'
    ? '@echo off\r\necho System.TypeLoadException: simulated editor load failure\r\nexit /b 0\r\n'
    : '#!/bin/sh\nprintf "%s\\n" "System.TypeLoadException: simulated editor load failure"\nexit 0\n';
  writeFileSync(fakeGodotPath, fakeGodot, 'utf8');
  if (process.platform !== 'win32') chmodSync(fakeGodotPath, 0o755);

  const godotFailure = runMsbuild(harnessPath, 'ExportPckForPackage', {
    GodotExe: fakeGodotPath,
  });
  assert.notEqual(godotFailure.status, 0, 'Managed Godot errors must fail packaging even with exit code zero.');
  assert.match(`${godotFailure.stdout}\n${godotFailure.stderr}`, /System\.TypeLoadException/);

  const godotImportFailure = runMsbuild(harnessPath, 'ImportGodotProjectForPackage', {
    GodotExe: fakeGodotPath,
  });
  assert.notEqual(godotImportFailure.status, 0, 'Managed Godot import errors must fail packaging.');
  assert.match(`${godotImportFailure.stdout}\n${godotImportFailure.stderr}`, /System\.TypeLoadException/);

  requireSuccess(runMsbuild(harnessPath, 'InstallLocal'), 'temporary InstallLocal');
  const artifactNames = ['NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck'];
  for (const name of [...artifactNames, 'SHA256SUMS']) {
    assert(existsSync(join(packageDir, name)), `Package is missing ${name}.`);
    assert(existsSync(join(installDir, name)), `Temporary install is missing ${name}.`);
    assert.equal(fileHash(join(packageDir, name)), fileHash(join(installDir, name)));
  }

  const checksumLines = readFileSync(join(packageDir, 'SHA256SUMS'), 'utf8').trim().split(/\r?\n/);
  assert.equal(checksumLines.length, artifactNames.length);
  for (const name of artifactNames) {
    assert(checksumLines.includes(`${fileHash(join(packageDir, name))} *${name}`));
  }

  mkdirSync(workshopDir, { recursive: true });
  writeFileSync(join(workshopDir, 'stale-preview.dll'), 'must be removed', 'utf8');
  requireSuccess(runMsbuild(harnessPath, 'StageWorkshop'), 'temporary StageWorkshop');
  const stagedNames = [...artifactNames, 'SHA256SUMS'];
  assert.deepEqual(readdirSync(workshopDir).sort(), stagedNames.sort());
  for (const name of stagedNames) {
    assert.equal(fileHash(join(packageDir, name)), fileHash(join(workshopDir, name)));
  }

  const guardPackageDir = join(sandbox, 'guard-package');
  const guardWorkshopDir = join(sandbox, 'guard-workshop');
  const guarded = runMsbuild(harnessPath, 'PublishWorkshop', {
    PostBuildModDir: guardPackageDir,
    WorkshopContentDir: guardWorkshopDir,
  });
  assert.notEqual(guarded.status, 0, 'PublishWorkshop must reject a Debug build.');
  assert.match(`${guarded.stdout}\n${guarded.stderr}`, /requires Configuration=Release/);
  assert(!existsSync(guardPackageDir), 'Fail-fast publication must not package before validation.');
  assert(!existsSync(guardWorkshopDir), 'Fail-fast publication must not stage before validation.');

  const unsupportedVersion = runMsbuild(harnessPath, 'PublishWorkshop', {
    Configuration: 'Release',
    GitDescribe: 'v01.1.0-0-gabcdef',
    NinjaSlayerVersion: '01.1.0',
    PublishWorkshopConfirmed: 'true',
    PostBuildModDir: guardPackageDir,
    WorkshopContentDir: guardWorkshopDir,
  });
  assert.notEqual(unsupportedVersion.status, 0, 'PublishWorkshop must reject non-SemVer tags.');
  assert.match(`${unsupportedVersion.stdout}\n${unsupportedVersion.stderr}`, /requires a clean stable SemVer tag/);
  assert(!existsSync(guardPackageDir), 'Unsupported versions must not package before validation.');
  assert(!existsSync(guardWorkshopDir), 'Unsupported versions must not stage before validation.');
} finally {
  rmSync(sandbox, { recursive: true, force: true });
}

console.log('Build boundary tests passed.');
