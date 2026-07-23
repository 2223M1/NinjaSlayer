import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  chmodSync,
  existsSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const projectPath = join(root, 'NinjaSlayer.csproj');
const versionPropsPath = join(root, 'eng', 'NinjaSlayer.Version.props');
const versionTargetsPath = join(root, 'eng', 'NinjaSlayer.Version.targets');
const packagingTargetsPath = join(root, 'eng', 'NinjaSlayer.Packaging.targets');
const releaseWorkflowPath = join(root, '.github', 'workflows', 'release.yml');
const workshopWorkflowPath = join(root, '.github', 'workflows', 'workshop.yml');
const ephemeralRunnerPath = join(
  root,
  'tools',
  'private-contract',
  'Start-EphemeralContractRunner.ps1',
);

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
for (const property of ['PostBuildModDir', 'SteamModDir', 'WorkshopContentDir', 'WorkshopUploaderExe']) {
  assert(!project.includes(`<${property}`), `${property} belongs in the packaging import.`);
}

const packagingTargets = readFileSync(packagingTargetsPath, 'utf8');
const releaseWorkflow = readFileSync(releaseWorkflowPath, 'utf8');
const workshopWorkflow = readFileSync(workshopWorkflowPath, 'utf8');
const ephemeralRunner = readFileSync(ephemeralRunnerPath, 'utf8');
for (const source of [releaseWorkflow, workshopWorkflow]) {
  assert(
    source.includes('^v0\\.1\\.(0|[1-9][0-9]?)$'),
    'Release and Workshop workflows must enforce the v0.1.0 through v0.1.99 series.',
  );
}
assert(releaseWorkflow.includes('environment: release-production'));
assert(releaseWorkflow.includes('workflow_dispatch:'));
assert(releaseWorkflow.includes('git rev-list -n 1 $env:RELEASE_TAG'));
assert(releaseWorkflow.includes('gh release upload $env:RELEASE_TAG $env:RELEASE_ARCHIVE --clobber'));
assert(
  releaseWorkflow.includes('runs-on: [self-hosted, Windows, X64, ninjaslayer-release]'),
  'Release packaging must run only on the dedicated ephemeral release runner.',
);
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
for (const forbidden of [
  'STS2_REFERENCE_BUNDLE_URL',
  'STS2_REFERENCE_BUNDLE_TOKEN',
  'UseSts2RefLib',
  'windows-latest',
]) {
  assert(!releaseWorkflow.includes(forbidden), `Release workflow must not contain ${forbidden}.`);
}
for (const required of [
  "[ValidateSet('Contract', 'Release')]",
  "'Contract' { 'ninjaslayer-contract' }",
  "'Release' { 'ninjaslayer-release' }",
  "$env:NINJASLAYER_SPINE_DIR = $spineDirectory",
  '$env:NINJASLAYER_SPINE_DIR = $previousSpineDirectory',
  'Remove-SessionDirectory -Path $sessionRoot',
]) {
  assert(ephemeralRunner.includes(required), `Ephemeral runner launcher is missing: ${required}`);
}
assert(
  ephemeralRunner.includes("if ($RunnerPurpose -eq 'Release')")
    && ephemeralRunner.includes('(Get-Item -LiteralPath $destination).IsReadOnly = $true'),
  'The release runner must isolate its Spine inputs as read-only files.',
);
assert(
  !packagingTargets.includes('BeforeTargets=') && !packagingTargets.includes('AfterTargets='),
  'Delivery targets must remain explicit and must not attach themselves to ordinary builds.',
);
for (const target of [
  'BuildGodotEditorAssembly',
  'PackageMod',
  'InstallLocal',
  'ValidateWorkshopPublish',
  'StageWorkshop',
  'PublishWorkshop',
]) {
  assert(packagingTargets.includes(`<Target Name="${target}"`), `Missing delivery target ${target}.`);
}
assert(
  packagingTargets.includes('BuildGodotEditorAssembly;Build;SyncFmodBankForPackage'),
  'Packaging must refresh the Godot editor assembly before exporting the requested configuration.',
);
const editorBuildTarget = xmlElement(
  packagingTargets,
  'Target',
  'Name="BuildGodotEditorAssembly"',
);
assert(editorBuildTarget.includes(`Condition="'$(Configuration)' != 'Debug'"`));
assert(editorBuildTarget.includes('Targets="Build"'));
assert(editorBuildTarget.includes('Properties="Configuration=Debug"'));
assert(editorBuildTarget.includes('BuildInParallel="false"'));
assert(
  packagingTargets.includes('ValidateWorkshopPublish;PackageMod;StageWorkshop'),
  'Workshop publication must validate before packaging or staging.',
);
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

const sandbox = mkdtempSync(join(tmpdir(), 'ninjaslayer-build-boundaries-'));
try {
  const versionHarnessPath = join(sandbox, 'VersionHarness.proj');
  writeFileSync(versionHarnessPath, `
<Project>
  <Import Project="${xml(versionPropsPath)}" />
  <Import Project="${xml(versionTargetsPath)}" />
  <Target Name="CaptureVersion" DependsOnTargets="ResolveNinjaSlayerVersion">
    <WriteLinesToFile File="$(CaptureFile)" Lines="$(NinjaSlayerVersion)|$(IsExactReleaseTag)|$(IsSupportedReleaseTag)|$(GitTag)" Overwrite="true" />
  </Target>
</Project>
`.trimStart(), 'utf8');
  const versionCases = [
    ['v0.1.0-0-gabcdef', '0.1.0|true|true|v0.1.0'],
    ['v0.1.99-0-gabcdef', '0.1.99|true|true|v0.1.99'],
    ['v0.1.100-0-gabcdef', '0.1.100|true|false|v0.1.100'],
    ['v2.3.4-0-gabcdef', '2.3.4|true|false|v2.3.4'],
    ['v2.3.4-7-gabcdef', '2.3.4-dev.7+gabcdef|false|false|'],
    ['v2.3.4-7-gabcdef-dirty', '2.3.4-dev.7+gabcdef.dirty|false|false|'],
    ['abcdef', '0.1.0-dev.0+gabcdef|false|false|'],
  ];
  for (const [describe, expected] of versionCases) {
    const captureFile = join(sandbox, `version-${describe.replaceAll(/[^a-z0-9]/gi, '-')}.txt`);
    requireSuccess(runMsbuild(versionHarnessPath, 'CaptureVersion', {
      CaptureFile: captureFile,
      GitDescribe: describe,
    }), `version resolution for ${describe}`);
    assert.equal(readFileSync(captureFile, 'utf8').trim(), expected);
  }

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
    <NinjaSlayerArtifactName>NinjaSlayer</NinjaSlayerArtifactName>
    <PostBuildModDir>${xml(packageDir)}</PostBuildModDir>
    <SteamModDir>${xml(installDir)}</SteamModDir>
    <WorkshopContentDir>${xml(workshopDir)}</WorkshopContentDir>
    <WorkshopUploadRoot>${xml(sandbox)}</WorkshopUploadRoot>
    <WorkshopUploaderExe>${xml(join(sandbox, 'must-not-run.exe'))}</WorkshopUploaderExe>
    <PackageModDependsOn>PrepareBuildTestPackage;GeneratePackageChecksums</PackageModDependsOn>
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

  requireSuccess(runMsbuild(harnessPath, 'StageWorkshop'), 'temporary StageWorkshop');
  assert.deepEqual(readdirSync(workshopDir).sort(), artifactNames.sort());
  for (const name of artifactNames) {
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
    GitDescribe: 'v0.1.100-0-gabcdef',
    NinjaSlayerVersion: '0.1.100',
    PublishWorkshopConfirmed: 'true',
    PostBuildModDir: guardPackageDir,
    WorkshopContentDir: guardWorkshopDir,
  });
  assert.notEqual(unsupportedVersion.status, 0, 'PublishWorkshop must reject v0.1.100.');
  assert.match(`${unsupportedVersion.stdout}\n${unsupportedVersion.stderr}`, /requires a v0\.1\.x tag/);
  assert(!existsSync(guardPackageDir), 'Unsupported versions must not package before validation.');
  assert(!existsSync(guardWorkshopDir), 'Unsupported versions must not stage before validation.');
} finally {
  rmSync(sandbox, { recursive: true, force: true });
}

console.log('Build boundary tests passed.');
