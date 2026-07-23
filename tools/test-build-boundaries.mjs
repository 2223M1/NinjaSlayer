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
    <WriteLinesToFile File="$(CaptureFile)" Lines="$(NinjaSlayerVersion)|$(IsExactReleaseTag)|$(GitTag)" Overwrite="true" />
  </Target>
</Project>
`.trimStart(), 'utf8');
  const versionCases = [
    ['v2.3.4-0-gabcdef', '2.3.4|true|v2.3.4'],
    ['v2.3.4-7-gabcdef', '2.3.4-dev.7+gabcdef|false|'],
    ['v2.3.4-7-gabcdef-dirty', '2.3.4-dev.7+gabcdef.dirty|false|'],
    ['abcdef', '0.1.0-dev.0+gabcdef|false|'],
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
    <NinjaSlayerVersion>1.2.3</NinjaSlayerVersion>
    <GitDescribe>v1.2.3-0-gabcdef</GitDescribe>
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
} finally {
  rmSync(sandbox, { recursive: true, force: true });
}

console.log('Build boundary tests passed.');
