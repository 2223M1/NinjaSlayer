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
const channelBuildScriptPath = join(root, 'tools', 'release', 'Invoke-NinjaSlayerChannelBuild.ps1');
const workshopQuickReleasePath = join(root, 'tools', 'release', 'Publish-WorkshopQuickRelease.ps1');
const compatibility = JSON.parse(readFileSync(join(root, 'eng', 'compatibility.json'), 'utf8'));
const validSourceRevision = 'a'.repeat(40);

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

function evaluate(project, argument, properties = {}) {
  const args = ['msbuild', project, '-nologo', argument];
  for (const [name, value] of Object.entries(properties)) args.push(`-p:${name}=${value}`);
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8' });
  requireSuccess(result, `${argument} evaluation for ${project}`);
  try {
    return JSON.parse(result.stdout);
  } catch (error) {
    assert.fail(`MSBuild returned invalid JSON for ${project}: ${error.message}`);
  }
}

function evaluateCompileFiles(project, properties) {
  const evaluation = evaluate(project, '-getItem:Compile', properties);
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

function evaluateProperties(project, names, properties) {
  return evaluate(project, `-getProperty:${names.join(',')}`, properties).Properties;
}

function fileHash(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex').toUpperCase();
}

const sandbox = mkdtempSync(join(tmpdir(), 'ninjaslayer-build-boundaries-'));
try {
  for (const [script, scriptArguments] of [
    [channelBuildScriptPath, [
      '-Channel', 'stable',
      '-Version', '0.1.0',
      '-Sts2DataDir', sandbox,
      '-Target', 'PackageMod',
    ]],
    [workshopQuickReleasePath, []],
  ]) {
    const missingSourceRevision = spawnSync(
      'pwsh',
      ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', script, ...scriptArguments],
      { cwd: root, encoding: 'utf8' },
    );
    assert.notEqual(missingSourceRevision.status, 0);
    assert.match(
      `${missingSourceRevision.stdout}\n${missingSourceRevision.stderr}`,
      /SourceRevision/,
      `${script} must reject a missing candidate source revision before building or publishing.`,
    );
  }

  for (const channel of ['stable', 'preview']) {
    assert(!evaluateCompileFiles(projectPath, { NinjaSlayerHostChannel: channel })
      .some(file => file.startsWith('.local-reference/')),
    'Local reference code must not enter the shipping assembly.');
    assert.deepEqual(
      evaluateCompileFiles(privateProjectPath, {
        CandidateRoot: root,
        NinjaSlayerHostChannel: channel,
        NinjaSlayerSourceRevision: validSourceRevision,
      }),
      evaluateCompileFiles(projectPath, { NinjaSlayerHostChannel: channel }),
      `The ${channel} private contract must compile exactly the shipping source set.`,
    );

    const isolatedRoot = join(sandbox, channel);
    const properties = evaluateProperties(
      projectPath,
      [
        'NinjaSlayerHostChannel',
        'NinjaSlayerGameApiVersion',
        'NinjaSlayerRitsuLibPackageId',
        'OutputPath',
        'IntermediateOutputPath',
      ],
      {
        Configuration: 'Release',
        NinjaSlayerHostChannel: channel,
        NinjaSlayerIsolatedOutputRoot: join(isolatedRoot, 'bin'),
        NinjaSlayerIsolatedIntermediateRoot: join(isolatedRoot, 'obj'),
      },
    );
    assert.equal(properties.NinjaSlayerHostChannel, channel);
    assert.equal(properties.NinjaSlayerGameApiVersion, compatibility.channels[channel].gameApiVersion);
    assert.equal(properties.NinjaSlayerRitsuLibPackageId, compatibility.channels[channel].ritsuLibPackageId);
    assert.equal(resolve(properties.OutputPath), resolve(isolatedRoot, 'bin', 'Release'));
    assert.equal(resolve(properties.IntermediateOutputPath), resolve(isolatedRoot, 'obj', 'Release'));
  }

  const privateReferenceDir = join(sandbox, 'private-references');
  mkdirSync(privateReferenceDir);
  writeFileSync(join(privateReferenceDir, 'sts2.dll'), 'contract-fixture', 'utf8');
  writeFileSync(join(privateReferenceDir, '0Harmony.dll'), 'contract-fixture', 'utf8');

  const missingChannel = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    CandidateRoot: root,
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerSourceRevision: validSourceRevision,
  });
  assert.notEqual(missingChannel.status, 0);
  assert.match(
    `${missingChannel.stdout}\n${missingChannel.stderr}`,
    /requires an explicit NinjaSlayerHostChannel=stable\|preview/,
  );

  const emptyCandidate = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerHostChannel: compatibility.defaultBuildChannel,
    NinjaSlayerSourceRevision: validSourceRevision,
  });
  assert.notEqual(emptyCandidate.status, 0);
  const emptyCandidateOutput = `${emptyCandidate.stdout}\n${emptyCandidate.stderr}`;
  assert.match(
    emptyCandidateOutput,
    /CandidateRoot is required and must point to a checked-out NinjaSlayer repository\./,
  );
  assert.doesNotMatch(emptyCandidateOutput, /MSB4184/);

  const invalidCandidate = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    CandidateRoot: sandbox,
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerHostChannel: compatibility.defaultBuildChannel,
    NinjaSlayerSourceRevision: validSourceRevision,
  });
  assert.notEqual(invalidCandidate.status, 0);
  assert.match(
    `${invalidCandidate.stdout}\n${invalidCandidate.stderr}`,
    /does not contain NinjaSlayer\.csproj\./,
  );

  const missingRevision = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    CandidateRoot: root,
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerHostChannel: compatibility.defaultBuildChannel,
  });
  assert.notEqual(missingRevision.status, 0);
  assert.match(
    `${missingRevision.stdout}\n${missingRevision.stderr}`,
    /NinjaSlayerSourceRevision is required and must be the candidate's full 40-character SHA\./,
  );

  const invalidRevision = runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
    CandidateRoot: root,
    Sts2DataDir: privateReferenceDir,
    NinjaSlayerHostChannel: compatibility.defaultBuildChannel,
    NinjaSlayerSourceRevision: 'not-a-full-sha',
  });
  assert.notEqual(invalidRevision.status, 0);
  assert.match(
    `${invalidRevision.stdout}\n${invalidRevision.stderr}`,
    /NinjaSlayerSourceRevision must be the candidate's full 40-character SHA\./,
  );

  requireSuccess(
    runMsbuild(privateProjectPath, 'ValidateTrustedInputs', {
      CandidateRoot: root,
      Sts2DataDir: privateReferenceDir,
      NinjaSlayerHostChannel: compatibility.defaultBuildChannel,
      NinjaSlayerSourceRevision: validSourceRevision,
    }),
    'private Contract input validation',
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
    ['v2.3.4-7-gabcdef', '', '2.3.5-dev.7+gabcdef|false|false|'],
    ['v2.3.4-7-gabcdef-dirty', '', '2.3.5-dev.7+gabcdef.dirty|false|false|'],
    ['v0.1.20-7-gabcdef', 'v0.1.27|v0.1.24|v0.1.20', '0.1.28-dev.7+gabcdef|false|false|'],
    ['v01.2.3-0-gabcdef', '', '0.1.0-dev.0+gabcdef|false|false|'],
  ];
  for (const [describe, releaseTags, expected] of versionCases) {
    const captureFile = join(sandbox, `version-${describe.replaceAll(/[^a-z0-9]/gi, '-')}.txt`);
    const properties = { CaptureFile: captureFile, GitDescribe: describe };
    if (releaseTags) properties.GitReleaseTags = releaseTags;
    requireSuccess(
      runMsbuild(versionHarnessPath, 'CaptureVersion', properties),
      `version resolution for ${describe}`,
    );
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
  assert.equal(readFileSync(localVersionCapture, 'utf8').trim(), '0.1.29+local.gabcdef.dirty');

  const packageDir = join(sandbox, 'package');
  const installDir = join(sandbox, 'installed');
  const harnessPath = join(sandbox, 'BuildBoundaryHarness.proj');
  writeFileSync(harnessPath, `
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
`.trimStart(), 'utf8');

  const fakeGodotPath = join(sandbox, process.platform === 'win32' ? 'fake-godot.cmd' : 'fake-godot.sh');
  const fakeGodot = process.platform === 'win32'
    ? '@echo off\r\necho System.TypeLoadException: simulated editor load failure\r\nexit /b 0\r\n'
    : '#!/bin/sh\nprintf "%s\\n" "System.TypeLoadException: simulated editor load failure"\nexit 0\n';
  writeFileSync(fakeGodotPath, fakeGodot, 'utf8');
  if (process.platform !== 'win32') chmodSync(fakeGodotPath, 0o755);

  for (const target of ['ExportPckForPackage', 'ImportGodotProjectForPackage']) {
    const result = runMsbuild(harnessPath, target, { GodotExe: fakeGodotPath });
    assert.notEqual(result.status, 0, `${target} must reject managed Godot errors.`);
    assert.match(`${result.stdout}\n${result.stderr}`, /System\.TypeLoadException/);
  }

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

} finally {
  rmSync(sandbox, { recursive: true, force: true });
}

console.log('Build boundary tests passed.');
