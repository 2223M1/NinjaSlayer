import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { dirname, join, relative, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const errors = [];
const skipDirectories = new Set(['.git', '.godot', 'bin', 'build', 'node_modules', 'obj']);

function filesUnder(directory) {
  const result = [];
  if (!existsSync(directory)) return result;
  for (const name of readdirSync(directory)) {
    if (skipDirectories.has(name)) continue;
    const path = join(directory, name);
    if (statSync(path).isDirectory()) result.push(...filesUnder(path));
    else result.push(path);
  }
  return result;
}

function readJson(path) {
  try {
    return JSON.parse(readFileSync(path, 'utf8'));
  } catch (error) {
    errors.push(`${relative(root, path)} is not valid JSON: ${error.message}`);
    return null;
  }
}

function readPngSize(path) {
  if (!existsSync(path)) return null;
  const png = readFileSync(path);
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  if (png.length < 24 || !png.subarray(0, 8).equals(signature)) {
    errors.push(`${relative(root, path)} is not a readable PNG`);
    return null;
  }
  return [png.readUInt32BE(16), png.readUInt32BE(20)];
}

const compatibilityPath = join(root, 'eng', 'compatibility.json');
const compatibility = readJson(compatibilityPath);
const activeChannels = compatibility
  ? Object.entries(compatibility.channels ?? {})
  : [];
if (activeChannels.map(([name]) => name).join(',') !== 'stable,preview') {
  errors.push('eng/compatibility.json must contain exactly stable and preview, in that order');
}

const compatibilitySync = spawnSync(
  process.execPath,
  [join(root, 'tools', 'sync-compatibility.mjs'), '--check'],
  { cwd: root, encoding: 'utf8' },
);
if (compatibilitySync.status !== 0) {
  errors.push(
    `Compatibility-derived files are stale: ${compatibilitySync.stderr || compatibilitySync.stdout}`.trim(),
  );
}

const powerShellHeader = '#Requires -Version 7.0\n#Requires -PSEdition Core\n';
for (const directory of [
  join(root, '.github', 'scripts'),
  join(root, 'tools'),
  join(root, 'Docs'),
  join(root, 'skills'),
]) {
  for (const path of filesUnder(directory).filter(path => path.endsWith('.ps1'))) {
    const source = readFileSync(path, 'utf8').replaceAll('\r\n', '\n');
    if (!source.startsWith(powerShellHeader)) {
      errors.push(`${relative(root, path)} must require PowerShell 7 Core`);
    }
  }
}

for (const workflow of filesUnder(join(root, '.github', 'workflows')).filter(
  path => path.endsWith('.yml'),
)) {
  const source = readFileSync(workflow, 'utf8');
  if (/^\s*shell:\s*powershell\s*$/m.test(source)) {
    errors.push(`${relative(root, workflow)} must use pwsh instead of Windows PowerShell`);
  }
}

const retiredCardArtTools = [
  'tools/build-card-art-manifest.mjs',
  'tools/process-card-art.py',
  'tools/record-card-art-review.mjs',
  'tools/generate-card-art-contact-sheets.py',
];
for (const relativePath of retiredCardArtTools) {
  if (existsSync(join(root, ...relativePath.split('/')))) {
    errors.push(`${relativePath} belongs in the external art-production workspace`);
  }
}

function validateReadme(relativePath, counterpart, language) {
  const path = join(root, relativePath);
  if (!existsSync(path)) {
    errors.push(`Missing ${relativePath}`);
    return;
  }

  const source = readFileSync(path, 'utf8');
  if (!source.includes('src="Workshop/image.png" width="256"')) {
    errors.push(`${relativePath} must use the 256px Workshop project image`);
  }
  if (!source.includes(`href="${counterpart}"`)) {
    errors.push(`${relativePath} must link to ${counterpart}`);
  }
  const badgeFragments = ['C%23', '.NET-9.0', 'Godot-4.5.1', 'github/v/release'];
  if (compatibility) {
    badgeFragments.push(
      `Spire%202-${compatibility.channels.stable.gameApiVersion}%20%7C%20${compatibility.channels.preview.gameApiVersion}`,
      `RitsuLib-${compatibility.ritsuLibVersion}`,
    );
  }
  for (const badge of badgeFragments) {
    if (!source.includes(badge)) errors.push(`${relativePath} is missing the ${badge} badge`);
  }

  const prohibitedHeadings = language === 'zhs'
    ? ['角色概览', '卡牌列表', 'Power 列表', '遗物列表']
    : ['Character Overview', 'Card List', 'Power List', 'Relic List'];
  const headings = new Set(
    [...source.matchAll(/^#{1,6}\s+(.+?)\s*$/gm)].map((match) => match[1]),
  );
  for (const heading of prohibitedHeadings) {
    if (headings.has(heading)) errors.push(`${relativePath} must not include the ${heading} section`);
  }

  const localTargets = new Set();
  for (const match of source.matchAll(/!?\[[^\]]*\]\(([^)]+)\)/g)) localTargets.add(match[1]);
  for (const match of source.matchAll(/(?:href|src)="([^"]+)"/g)) localTargets.add(match[1]);
  for (const rawTarget of localTargets) {
    const target = rawTarget.replace(/^<|>$/g, '').split(/[?#]/, 1)[0];
    if (!target || target.startsWith('#') || /^[a-z][a-z0-9+.-]*:/i.test(target)) continue;
    const localPath = resolve(dirname(path), decodeURIComponent(target));
    if (!existsSync(localPath)) {
      errors.push(`${relativePath} references missing local path ${target}`);
    }
  }
}

validateReadme('README.md', 'README_EN.md', 'zhs');
validateReadme('README_EN.md', 'README.md', 'eng');

for (const path of filesUnder(join(root, 'NinjaSlayer', 'localization')).filter((path) => path.endsWith('.json'))) {
  readJson(path);
}

const runtimeAudioBankPath = join(root, 'NinjaSlayer', 'audio', 'fmod', 'NinjaSlayer.bank');
const desktopAudioBankPath = join(
  root,
  'NinjaSlayer',
  'audio',
  'fmod',
  'desktop',
  'NinjaSlayer.bank',
);
if (!existsSync(runtimeAudioBankPath) || !existsSync(desktopAudioBankPath)) {
  errors.push('Runtime and desktop NinjaSlayer FMOD banks must both exist');
} else if (!readFileSync(runtimeAudioBankPath).equals(readFileSync(desktopAudioBankPath))) {
  errors.push('Runtime NinjaSlayer FMOD bank must match the latest desktop bank byte-for-byte');
}

const fmodGuidMappingsPath = join(root, 'NinjaSlayer', 'audio', 'fmod', 'GUIDs.txt');
if (!existsSync(fmodGuidMappingsPath)) {
  errors.push('NinjaSlayer/audio/fmod/GUIDs.txt must exist');
} else {
  const fmodGuidMappings = readFileSync(fmodGuidMappingsPath, 'utf8');
  for (const eventPath of [
    'event:/NinjaSlayerAudio/sfx/dark_ninja/dark_ninja_death',
    'event:/NinjaSlayerAudio/sfx/dark_ninja/dark_ninja_death_kiri',
    'event:/NinjaSlayerAudio/sfx/dark_ninja/dark_ninja_fast_attack',
    'event:/NinjaSlayerAudio/sfx/dark_ninja/dark_ninja_kirisute_goumen',
    'event:/NinjaSlayerAudio/sfx/dark_ninja/dark_ninja_slow_attack',
  ]) {
    if (!fmodGuidMappings.includes(eventPath)) {
      errors.push(`NinjaSlayer/audio/fmod/GUIDs.txt is missing ${eventPath}`);
    }
  }
}

const manifest = readJson(join(root, 'NinjaSlayer.json'));
if (manifest) {
  if (manifest.version !== 'VERSION_PLACEHOLDER'
      || manifest.min_game_version !== 'MIN_GAME_VERSION_PLACEHOLDER') {
    errors.push('NinjaSlayer.json must retain build-time version placeholders');
  }
  const manifestRitsuDependencies = manifest.dependencies?.filter(
    (dependency) => dependency.id === 'STS2-RitsuLib',
  ) ?? [];
  if (manifestRitsuDependencies.length !== 1
      || manifestRitsuDependencies[0].min_version !== 'RITSULIB_VERSION_PLACEHOLDER') {
    errors.push('NinjaSlayer.json must retain one build-time RitsuLib dependency placeholder');
  }

  const dependencyProps = readFileSync(
    join(root, 'eng', 'NinjaSlayer.Dependencies.props'),
    'utf8',
  );
  const smartFormatVersion = dependencyProps.match(
    /<NinjaSlayerSmartFormatVersion>([^<]+)<\/NinjaSlayerSmartFormatVersion>/,
  )?.[1];
  const project = readFileSync(join(root, 'NinjaSlayer.csproj'), 'utf8');
  if (!dependencyProps.includes('<Import Project="NinjaSlayer.Compatibility.g.props" />')) {
    errors.push('NinjaSlayer.Dependencies.props must import the generated compatibility props');
  }
  for (const [dependency, property] of [
    ['$(NinjaSlayerRitsuLibPackageId)', 'NinjaSlayerRitsuLibVersion'],
    ['SmartFormat', 'NinjaSlayerSmartFormatVersion'],
  ]) {
    if (!project.includes(`PackageReference Include="${dependency}"`)
        || !project.includes(`Version="$(${property})"`)) {
      errors.push(`NinjaSlayer.csproj must source ${dependency} from ${property}`);
    }
  }
  if (!smartFormatVersion) errors.push('NinjaSlayer.Dependencies.props is missing SmartFormat');
  if (project.includes('Book.StS2.RefLib') || project.includes('UseSts2RefLib')) {
    errors.push('NinjaSlayer.csproj must not retain the retired RefLib build target');
  }
  for (const metadataName of [
    'NinjaSlayerHostChannel',
    'NinjaSlayerGameApiVersion',
    'NinjaSlayerRitsuLibPackageId',
    'NinjaSlayerRitsuLibVersion',
  ]) {
    if (!project.includes(`<AssemblyMetadata Include="${metadataName}"`)) {
      errors.push(`NinjaSlayer.csproj is missing assembly metadata ${metadataName}`);
    }
  }
}

if (compatibility) {
  const generatedProps = readFileSync(join(root, 'eng', 'NinjaSlayer.Compatibility.g.props'), 'utf8');
  for (const [channelName, channel] of activeChannels) {
    for (const expected of [
      `<NinjaSlayerGameApiVersion>${channel.gameApiVersion}</NinjaSlayerGameApiVersion>`,
      `<NinjaSlayerRitsuLibPackageId>${channel.ritsuLibPackageId}</NinjaSlayerRitsuLibPackageId>`,
      `<NinjaSlayerHostModuleMvid>${channel.hostContract.moduleMvid}</NinjaSlayerHostModuleMvid>`,
    ]) {
      if (!generatedProps.includes(expected)) {
        errors.push(`Generated compatibility props are missing ${channelName}: ${expected}`);
      }
    }
  }

  const smokeManifest = readJson(join(
    root,
    'tools',
    'smoke-harness',
    'NinjaSlayer.SmokeDriver',
    'NinjaSlayer-SmokeDriver.json',
  ));
  if (smokeManifest) {
    const smokeRitsu = smokeManifest.dependencies?.find(
      dependency => dependency.id === 'STS2-RitsuLib',
    );
    if (smokeManifest.min_game_version !== compatibility.channels.stable.gameApiVersion
        || smokeRitsu?.min_version !== compatibility.ritsuLibVersion) {
      errors.push('SmokeDriver manifest must be generated from compatibility.json');
    }
  }

  const hardcodeFiles = [
    ...filesUnder(join(root, '.github', 'workflows')).filter(path => path.endsWith('.yml')),
    ...filesUnder(join(root, '.github', 'scripts')).filter(path => path.endsWith('.ps1')),
    ...filesUnder(join(root, 'tools', 'private-contract')).filter(path => /\.(?:ps1|csproj)$/.test(path)),
    ...filesUnder(join(root, 'tools', 'smoke-harness')).filter(path => /\.(?:ps1|cs|csproj)$/.test(path)),
    ...filesUnder(join(root, 'Tests', 'NinjaSlayer.RitsuLibContractTests')).filter(
      path => /\.(?:cs|csproj)$/.test(path),
    ),
    ...filesUnder(join(root, 'Tests', 'NinjaSlayer.ProductPreparedContractTests')).filter(
      path => /\.(?:cs|csproj)$/.test(path),
    ),
  ];
  const activeLiterals = new Set([
    compatibility.ritsuLibVersion,
    ...activeChannels.flatMap(([, channel]) => [
      channel.gameApiVersion,
      channel.ritsuLibPackageId === 'STS2.RitsuLib' ? null : channel.ritsuLibPackageId,
    ]).filter(Boolean),
  ]);
  for (const path of hardcodeFiles) {
    const source = readFileSync(path, 'utf8');
    for (const literal of activeLiterals) {
      if (source.includes(literal)) {
        errors.push(`${relative(root, path)} hardcodes active compatibility value ${literal}`);
      }
    }
  }
}

const warningAllowlist = readJson(join(root, 'Docs', 'warning-allowlist.json'));
if (warningAllowlist) {
  const entries = Array.isArray(warningAllowlist.entries) ? warningAllowlist.entries : [];
  const codes = new Set();
  for (const entry of entries) {
    if (typeof entry.code !== 'string' || !/^[A-Z]+\d+$/.test(entry.code) || codes.has(entry.code)) {
      errors.push(`Docs/warning-allowlist.json contains an invalid or duplicate code: ${entry.code ?? '<missing>'}`);
    }
    codes.add(entry.code);
    if (typeof entry.owner !== 'string' || typeof entry.reason !== 'string' || typeof entry.suppressedIn !== 'string') {
      errors.push(`Warning allowlist entry ${entry.code ?? '<missing>'} is incomplete`);
    }
  }
}

for (const workflow of filesUnder(join(root, '.github', 'workflows')).filter((path) => path.endsWith('.yml'))) {
  const source = readFileSync(workflow, 'utf8');
  for (const match of source.matchAll(/^\s*uses:\s*([^\s#]+)\s*$/gm)) {
    const action = match[1];
    if (action.startsWith('./')) continue;
    const revision = action.slice(action.lastIndexOf('@') + 1);
    if (!/^[0-9a-f]{40}$/.test(revision)) {
      errors.push(`${relative(root, workflow)} must pin ${action} to a full commit SHA`);
    }
  }
}

const workshopWorkflow = readFileSync(join(root, '.github', 'workflows', 'workshop.yml'), 'utf8');
const workshopPublisher = readFileSync(join(root, '.github', 'scripts', 'publish-workshop.sh'), 'utf8');
for (const required of [
  'compatibility.workshop.itemId',
  'compatibility.workshop.visibility',
  'PUBLISH_NINJASLAYER_WORKSHOP_',
  'verify-contract-attestation.ps1',
  'verify-smoke-attestation.ps1',
  'verify-release-attestation.ps1',
  'FirstCombatRestart',
  'protected-release',
  'public GitHub Release asset does not match the protected Release artifact',
  'workshop-universal',
  'validate-workshop-bundle',
  'WORKSHOP_ITEM_ID',
  'WORKSHOP_VISIBILITY',
]) {
  if (!workshopWorkflow.includes(required)) {
    errors.push(`Workshop workflow is missing channel isolation guard: ${required}`);
  }
}
for (const required of ['$WORKSHOP_ITEM_ID', '$WORKSHOP_VISIBILITY']) {
  if (!workshopPublisher.includes(required)) {
    errors.push(`Workshop publisher is missing manifest-derived value ${required}`);
  }
}
if (compatibility?.workshop?.itemId
    && workshopPublisher.includes(compatibility.workshop.itemId)) {
  errors.push('Workshop publisher must not hardcode the Workshop item id');
}
const localWorkshopManifest = readJson(join(root, 'Workshop', 'workshop.json'));
if (localWorkshopManifest?.visibility !== compatibility?.workshop?.visibility) {
  errors.push('Workshop/workshop.json visibility must match the universal compatibility target');
}

const retiredHostVersion = ['0', '109', '0'].join('.');
for (const directory of ['.github', 'Code', 'Tests', 'tools', 'eng']) {
  for (const path of filesUnder(join(root, directory)).filter(path => /\.(?:cs|csproj|json|mjs|ps1|sh|yml)$/.test(path))) {
    const repositoryPath = relative(root, path).replaceAll('\\', '/');
    if (repositoryPath.startsWith('Infrastructure/telemetry-worker/test/fixtures/')) continue;
    if (readFileSync(path, 'utf8').includes(retiredHostVersion)) {
      errors.push(`${repositoryPath} retains the retired intermediate host ${retiredHostVersion}`);
    }
  }
}

for (const name of ['settings_ui.json', 'enchantments.json']) {
  const englishPath = join(root, 'NinjaSlayer', 'localization', 'eng', name);
  const chinesePath = join(root, 'NinjaSlayer', 'localization', 'zhs', name);
  const english = readJson(englishPath);
  const chinese = readJson(chinesePath);
  if (english && chinese) {
    const englishKeys = Object.keys(english).sort();
    const chineseKeys = Object.keys(chinese).sort();
    if (JSON.stringify(englishKeys) !== JSON.stringify(chineseKeys)) {
      errors.push(`Localization keys differ between eng/${name} and zhs/${name}`);
    }
  }
}

const redesignCardsByLanguage = Object.fromEntries(
  ['eng', 'zhs'].map((language) => {
    const cards = readJson(join(root, 'NinjaSlayer', 'localization', language, 'cards.json')) ?? {};
    return [language, Object.fromEntries(
      Object.entries(cards).filter(([key]) =>
        key.includes('_REDESIGN_V1.') || key.startsWith('NINJA_SLAYER_CARD_BUSY_LINE.')),
    )];
  }),
);
const englishRedesignKeys = Object.keys(redesignCardsByLanguage.eng).sort();
const chineseRedesignKeys = Object.keys(redesignCardsByLanguage.zhs).sort();
if (JSON.stringify(englishRedesignKeys) !== JSON.stringify(chineseRedesignKeys)) {
  errors.push('Redesign V1 card localization keys differ between eng/cards.json and zhs/cards.json');
}
const redesignSelectionPromptStems = [
  'NINJA_SLAYER_CARD_TRUMP_CARD_REDESIGN_V1',
  'NINJA_SLAYER_CARD_EXECUTION_MOVE_REDESIGN_V1',
  'NINJA_SLAYER_CARD_CHADO_FURIN_KAZAN_REDESIGN_V1',
];
for (const language of ['eng', 'zhs']) {
  const cards = redesignCardsByLanguage[language];
  const stems = new Set(Object.keys(cards).map((key) => key.replace(/\.(?:title|description|selectionScreenPrompt)$/, '')));
  for (const stem of stems) {
    for (const suffix of ['title', 'description']) {
      const value = cards[`${stem}.${suffix}`];
      const intentionallyBlankBusyLineDescription =
        stem === 'NINJA_SLAYER_CARD_BUSY_LINE' && suffix === 'description' && value === '';
      if ((typeof value !== 'string' || value.trim().length === 0)
          && !intentionallyBlankBusyLineDescription) {
        errors.push(`${language}/cards.json is missing non-empty ${stem}.${suffix}`);
      }
    }
  }
  for (const stem of redesignSelectionPromptStems) {
    const value = cards[`${stem}.selectionScreenPrompt`];
    if (typeof value !== 'string' || value.trim().length === 0) {
      errors.push(`${language}/cards.json is missing non-empty ${stem}.selectionScreenPrompt`);
    }
  }
}
for (const key of englishRedesignKeys.filter((key) => key.endsWith('.description'))) {
  const englishFields = [...new Set(
    [...redesignCardsByLanguage.eng[key].matchAll(/\{([A-Za-z_][A-Za-z0-9_]*)(?=[:}])/g)]
      .map((match) => match[1]),
  )].sort();
  const chineseFields = [...new Set(
    [...redesignCardsByLanguage.zhs[key].matchAll(/\{([A-Za-z_][A-Za-z0-9_]*)(?=[:}])/g)]
      .map((match) => match[1]),
  )].sort();
  if (JSON.stringify(englishFields) !== JSON.stringify(chineseFields)) {
    errors.push(`Redesign V1 format fields differ between eng and zhs for ${key}`);
  }
}

for (const language of ['eng', 'zhs']) {
  const characters = readJson(join(root, 'NinjaSlayer', 'localization', language, 'characters.json')) ?? {};
  if (Object.keys(characters).some(key =>
    key.startsWith('NINJA_SLAYER_CHARACTER_NINJA_SLAYER_REDESIGN_CHARACTER.'))) {
    errors.push(`${language}/characters.json contains localization for the retired duplicate Ninja Slayer character`);
  }
}

function validateFrames(directory, prefix, count) {
  const actual = readdirSync(directory)
    .filter((name) => name.endsWith('.png'))
    .sort();
  const expected = Array.from(
    { length: count },
    (_, index) => `${prefix}${(index + 1).toString().padStart(4, '0')}.png`,
  );
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    errors.push(`${relative(root, directory)} must contain exactly ${expected[0]} through ${expected.at(-1)}`);
  }
}

validateFrames(
  join(root, 'NinjaSlayer', 'images', 'characters', 'ninja_slayer', 'idle'),
  'NinjaSlayer_idle_',
  22,
);
validateFrames(
  join(root, 'NinjaSlayer', 'images', 'characters', 'ninja_slayer', 'naraku_idle'),
  'NinjaSlayer_naraku_idle_',
  22,
);

const sourceFiles = filesUnder(root).filter((path) =>
  /\.(cs|gd|tscn|tres)$/.test(path) && relative(root, path).split(/[\\/]/)[0] !== 'tools');
const resourcePattern = /res:\/\/NinjaSlayer\/[^"'\s)\]}]+/g;
for (const path of sourceFiles) {
  const source = readFileSync(path, 'utf8');
  for (const match of source.matchAll(resourcePattern)) {
    const resourcePath = match[0];
    if (resourcePath.includes('{') || resourcePath.includes('*') || resourcePath.endsWith('_')) continue;
    const localPath = join(root, ...resourcePath.slice('res://'.length).split('/'));
    if (!existsSync(localPath)) {
      errors.push(`${relative(root, path)} references missing resource ${resourcePath}`);
    }
  }
}

const patchSources = [
  ...filesUnder(join(root, 'Code', 'Patches')),
  ...filesUnder(join(root, 'Powers')),
]
  .filter((path) => path.endsWith('.cs'))
  .map((path) => readFileSync(path, 'utf8'))
  .join('\n');
const entrySource = readFileSync(join(root, 'Scripts', 'Entry.cs'), 'utf8');
const patchGroupSource = readFileSync(join(root, 'Code', 'Patches', 'NinjaSlayerPatchGroups.cs'), 'utf8');
const patchClasses = [...patchSources.matchAll(/(?:public|internal)\s+sealed\s+(?:partial\s+)?class\s+(\w+)\s*:\s*IPatchMethod/g)]
  .map((match) => match[1]);
const patchRegistrationSource = `${entrySource}\n${patchGroupSource}`;
const patchRegistrations = [...patchRegistrationSource.matchAll(/RegisterPatch<([\w.]+)>/g)]
  .map((match) => match[1].split('.').at(-1));
for (const patchClass of patchClasses) {
  const count = patchRegistrations.filter((registered) => registered === patchClass).length;
  if (count !== 1) errors.push(`${patchClass} must appear in exactly one production patch registration (found ${count})`);
}
for (const registered of patchRegistrations) {
  if (!patchClasses.includes(registered)) errors.push(`Production patch registration references unknown patch ${registered}`);
}

const patchDeclarations = [...patchSources.matchAll(/(?:public|internal)\s+sealed\s+(?:partial\s+)?class\s+(\w+)\s*:\s*IPatchMethod/g)];
const patchBodies = new Map(patchDeclarations.map((match, index) => [
  match[1],
  patchSources.slice(match.index, patchDeclarations[index + 1]?.index ?? patchSources.length),
]));
const retainedPatchGroups = new Set([
  'BossBurstPresentationPatchGroup',
  'TransitionCorePatchGroup',
]);
const declaredPatchGroups = [...patchGroupSource.matchAll(/internal\s+sealed\s+class\s+(\w+PatchGroup)\s*:\s*IModPatches/g)]
  .map((match) => match[1]);
if (declaredPatchGroups.length !== retainedPatchGroups.size
    || declaredPatchGroups.some((groupName) => !retainedPatchGroups.has(groupName))) {
  errors.push(`Patch groups must be exactly: ${[...retainedPatchGroups].join(', ')}`);
}
for (const groupName of retainedPatchGroups) {
  const groupStart = patchGroupSource.indexOf(`class ${groupName}`);
  if (groupStart < 0) continue;
  const groupEnd = patchGroupSource.indexOf('\ninternal sealed class ', groupStart + 1);
  const groupBody = patchGroupSource.slice(groupStart, groupEnd < 0 ? undefined : groupEnd);
  const groupRegistrations = [...groupBody.matchAll(/RegisterPatch<(\w+)>/g)];
  if (groupRegistrations.length < 2) {
    errors.push(`${groupName} must contain at least two patches`);
  }
  for (const match of groupRegistrations) {
    if (!patchBodies.get(match[1])?.includes('IsCritical => true')) {
      errors.push(`${groupName} contains non-critical required patch ${match[1]}`);
    }
  }
}

const requiredPatcherSource = entrySource.slice(
  entrySource.indexOf('ModPatcher requiredPatcher'),
  entrySource.indexOf('bool requiredPatchFailure'),
);
if (/RegisterPatches<\w+PatchGroup>/.test(requiredPatcherSource)) {
  errors.push('Required patches must be registered directly on the single required patcher');
}
const optionalPresentationSource = entrySource.slice(
  entrySource.indexOf('private static void InstallOptionalPresentations'),
  entrySource.indexOf('private static void TryInstallOptionalPatches'),
);
for (const groupName of retainedPatchGroups) {
  const registrationCount = [...entrySource.matchAll(new RegExp(`RegisterPatches<${groupName}>`, 'g'))].length;
  if (registrationCount !== 1
      || !optionalPresentationSource.includes(`nameof(${groupName})`)
      || !optionalPresentationSource.includes(`RegisterPatches<${groupName}>`)) {
    errors.push(`${groupName} must be owned by exactly one independent optional patcher transaction`);
  }
}

const patchIds = [...patchSources.matchAll(/PatchId\s*=>\s*"([^"]+)"/g)].map((match) => match[1]);
for (const patchId of new Set(patchIds)) {
  const count = patchIds.filter((candidate) => candidate === patchId).length;
  if (count !== 1) errors.push(`Patch id ${patchId} is declared ${count} times`);
}
if (patchIds.length !== patchClasses.length) {
  errors.push(`Expected one PatchId per IPatchMethod (${patchClasses.length} classes, ${patchIds.length} ids)`);
}

const localizedPrivateMemberContracts = new Map([
  ['Code/ExternalAnimations/AttackEvasionFeedbackContext.cs', ['"_singleTarget"']],
  ['Code/ExternalAnimations/BossBurstMusicSession.cs', ['"_currentTrack"', '"_failedTrack"']],
  ['Code/ExternalAnimations/CombatDodgeAnimation.cs', [
    '"_attackerAnimName"',
    '"_visualAttacker"',
    '"_waitBeforeHit"',
    '"_singleTarget"',
  ]],
  ['Code/ExternalAnimations/FinisherAttackCommandAdapter.cs', [
    '"_damagePerHit"',
    '"_calculatedDamageVar"',
    '"_hitCount"',
    '"_singleTarget"',
  ]],
  ['Code/Patches/KarateHealthBarPreviewPatch.cs', ['"_creature"', '"_hpLabel"']],
  ['Code/Patches/NarakuLifeHealthBarLayoutPatch.cs', [
    '"_creature"',
    '"_expectedMaxFgWidth"',
    '"_originalBlockPosition"',
  ]],
  ['Code/Patches/NinjaSlayerFeedbackPatches.cs', ['"SendButtonSelected"']],
  ['Code/Patches/NinjaSlayerTransitionLoadSmoothingPatch.cs', [
    '"_loading"',
    '"_finalizing"',
    '"AddToCache"',
    '"FinalizeLoading"',
    '"ProcessLoadingQueue"',
  ]],
  ['Code/Patches/NinjaSlayerTransitionPresentationPatch.cs', ['"PlayHealVfxAfterFadeIn"']],
  ['Code/Patches/NinjaSlayerTypographyPatch.cs', ['"_relics"', '"_index"']],
  ['Code/Patches/PreparedCardPatches.cs', ['"ShuffleFtueCheck"', '"_grid"']],
  ['Code/Patches/ReporterPassEventOptionPatch.cs', ['"SetEventFinished"']],
  ['Code/Transition/TransitionViewAdapter.cs', ['nameof(NTransition.InTransition)', '"_tween"']],
  ['Events/SawatariEvent.cs', [
    '"_combatStateForCombatLayout"',
    '"_combatSynchronizer"',
    '"CombatStateForLayout"',
  ]],
  ['Code/Patches/SawatariEventPatches.cs', ['"_rooms"']],
  ['Powers/NarakuPower.cs', ['"_powerNodes"', '"UpdatePositions"']],
]);
for (const [relativePath, members] of localizedPrivateMemberContracts) {
  const source = readFileSync(join(root, ...relativePath.split('/')), 'utf8');
  if (members.some(member => !source.includes(member))) {
    errors.push(`${relativePath} must own its localized private game members: ${members.join(', ')}`);
  }
}

const removedStage4AFiles = [
  'GameCompatibility.ArchitectVictory.cs',
  'GameCompatibility.CardPlays.cs',
  'GameCompatibility.Damage.cs',
  'GameCompatibility.Feedback.cs',
  'GameCompatibility.KarateHealthBar.cs',
  'GameCompatibility.MapHistory.cs',
  'GameCompatibility.NarakuHealthBar.cs',
  'GameCompatibility.OrobasSeaGlass.cs',
  'GameCompatibility.Prepared.cs',
  'GameCompatibility.ReporterPass.cs',
  'GameCompatibility.Transition.cs',
  'GameCompatibility.TransitionPresentation.cs',
  'GameCompatibility.Typography.cs',
  'HostBlackFlameDamagePatch.cs',
  'HostDamageApiAdapters.cs',
  'LegacyAttackCommandExtensions.cs',
  'OrobasSeaGlassCandidatePolicy.cs',
  'RedesignCardDestinationAdapter.cs',
  'GameCompatibility.cs',
  'GameCompatibility.AssetLoading.cs',
  'GameCompatibility.BossBurst.cs',
  'GameCompatibility.CreaturePresentation.cs',
  'GameCompatibility.EnemyAttackDodge.cs',
  'GameCompatibility.EventCombat.cs',
  'GameCompatibility.Finisher.cs',
  'GameCompatibility.NarakuPowerUi.cs',
  'PreparedQueueCompatibility.cs',
];
for (const file of removedStage4AFiles) {
  if (existsSync(join(root, 'Code', 'Compatibility', file))) {
    errors.push(`Code/Compatibility/${file} is a retired Stage 4A facade`);
  }
}

const retiredStage4AFacadePattern =
  /\b(?:GameCompatibility|HostCompatibility|GameApiFacade|CompatibilityManager|CompatibilityService|GameApiRegistry|LegacyAttackCommandExtensions)\b|\bAssociate(?:Player|CardPlay)\b/;
for (const path of filesUnder(root).filter(path => path.endsWith('.cs'))) {
  const repositoryPath = relative(root, path).replaceAll('\\', '/');
  if (repositoryPath.startsWith('Tests/') || repositoryPath.startsWith('tools/')) continue;
  if (retiredStage4AFacadePattern.test(readFileSync(path, 'utf8'))) {
    errors.push(`${repositoryPath} contains a retired Stage 4A facade or replacement`);
  }
}

const retiredRuntimeCapabilityPattern =
  /\b(?:CapabilityState|CapabilityProbe|CapabilityStatus|NinjaSlayerCapabilityRegistry|NinjaSlayerCapabilityIds|NinjaSlayerPatchCapabilities|NinjaSlayerRuntimeHealth|MethodBodyFingerprint|StableMethodBodyContract|GameHostContractProfile)\b/;
for (const path of filesUnder(root).filter(path => path.endsWith('.cs'))) {
  const repositoryPath = relative(root, path).replaceAll('\\', '/');
  if (repositoryPath.startsWith('Tests/') || repositoryPath.startsWith('tools/')) continue;
  if (retiredRuntimeCapabilityPattern.test(readFileSync(path, 'utf8'))) {
    errors.push(`${repositoryPath} contains a retired runtime capability symbol`);
  }
}

const retiredGcControlPattern = /\b(?:System\.)?GC\.(?:TryStartNoGCRegion|Collect)\s*\(/;
for (const path of filesUnder(root).filter(path => path.endsWith('.cs'))) {
  const repositoryPath = relative(root, path).replaceAll('\\', '/');
  if (repositoryPath.startsWith('Tests/') || repositoryPath.startsWith('tools/')) continue;
  if (retiredGcControlPattern.test(readFileSync(path, 'utf8'))) {
    errors.push(`${repositoryPath} contains retired explicit GC control`);
  }
}

const concreteCardSources = filesUnder(join(root, 'Cards'))
  .filter((path) => path.endsWith('.cs')
    && !path.includes(`${join('Cards', 'Base')}`)
    && !path.includes(`${join('Cards', 'RedesignV1')}`))
  .map((path) => ({ path, source: readFileSync(path, 'utf8') }))
  .filter(({ source }) => /public\s+sealed\s+class\s+\w+/.test(source));
for (const { path, source } of concreteCardSources) {
  const className = /public\s+sealed\s+class\s+(\w+)/.exec(source)?.[1];
  if (!className) continue;

  const portraitPath = join(root, 'NinjaSlayer', 'images', 'cards', `${className}.png`);
  const portraitSize = readPngSize(portraitPath);
  if (!portraitSize) {
    if (!existsSync(portraitPath)) errors.push(`Missing dedicated card portrait: ${className}.png`);
    continue;
  }
  const expectedSize = /CardRarity\.Ancient/.test(source) ? [606, 852] : [1000, 760];
  if (portraitSize[0] !== expectedSize[0] || portraitSize[1] !== expectedSize[1]) {
    errors.push(
      `${className}.png must be ${expectedSize[0]}x${expectedSize[1]}, found ${portraitSize[0]}x${portraitSize[1]}`,
    );
  }
}

const redesignRulesSource = readFileSync(join(root, 'Content', 'RedesignV1Rules.cs'), 'utf8');
const redesignCardSource = filesUnder(join(root, 'Cards', 'RedesignV1'))
  .filter(path => path.endsWith('.cs'))
  .map(path => readFileSync(path, 'utf8'))
  .join('\n');
const busyLineSource = readFileSync(join(root, 'Cards', 'Statuses', 'BusyLine.cs'), 'utf8');
const currentRedesignCardSource = `${redesignCardSource}\n${busyLineSource}`;
const redesignStarterRelicSource = readFileSync(
  join(root, 'Relics', 'ChadoBreathingRelic.cs'),
  'utf8',
);
const redesignAncientStarterRelicSource = readFileSync(
  join(root, 'Relics', 'DeepChadoBreathingRelic.cs'),
  'utf8',
);
if (!/protected\s+virtual\s+int\s+ChadoCount\s*=>\s*0/.test(redesignStarterRelicSource)
    || !/protected\s+override\s+int\s+ChadoCount\s*=>\s*2/.test(redesignAncientStarterRelicSource)
    || !/ChadoBreathCmd\.Apply\(Owner,\s*2,\s*this\)/.test(redesignStarterRelicSource)) {
  errors.push('Redesign starter relics must apply Chado Breathing 2, with two Chado added by the Ancient version');
}

function readRedesignCardIds(propertyName) {
  const match = new RegExp(`${propertyName}\\s*\\{[^}]*\\}\\s*=\\s*\\[([\\s\\S]*?)\\];`)
    .exec(redesignRulesSource);
  if (!match) {
    errors.push(`RedesignV1Rules.${propertyName} is missing`);
    return [];
  }
  return [...match[1].matchAll(/"([^"]+)"/g)].map(candidate => candidate[1]);
}

const redesignRewardGroups = [
  ['Common', 20, readRedesignCardIds('CommonRewardCardIds')],
  ['Uncommon', 31, readRedesignCardIds('UncommonRewardCardIds')],
  ['Rare', 23, readRedesignCardIds('RareRewardCardIds')],
];
const redesignRewardIds = redesignRewardGroups.flatMap(([, , ids]) => ids);
for (const [rarity, expectedCount, ids] of redesignRewardGroups) {
  const declaredCount = Number(new RegExp(`const\\s+int\\s+${rarity}RewardCount\\s*=\\s*(\\d+)`)
    .exec(redesignRulesSource)?.[1]);
  if (declaredCount !== expectedCount || ids.length !== expectedCount) {
    errors.push(`Redesign V1 ${rarity} rewards must contain exactly ${expectedCount} cards`);
  }

  const implementedIds = [...redesignCardSource.matchAll(
    new RegExp(`public\\s+sealed\\s+(?:partial\\s+)?class\\s+(\\w+)\\s*:\\s*RedesignV1${rarity}Card`, 'g'),
  )].map(match => match[1]);
  if (rarity === 'Rare') implementedIds.push('TurtleShellRedesignV1');
  const missing = ids.filter(id => !implementedIds.includes(id));
  const extra = implementedIds.filter(id => !ids.includes(id));
  if (missing.length > 0 || extra.length > 0) {
    errors.push(
      `Redesign V1 ${rarity} reward list differs from implementations `
      + `(missing: ${missing.join(', ') || 'none'}; extra: ${extra.join(', ') || 'none'})`,
    );
  }
}

if (new Set(redesignRewardIds).size !== 74) {
  errors.push('Redesign V1 rewards must contain 74 unique cards');
}
const excludedRedesignIds = readRedesignCardIds('ExcludedSpecialCardIds');
const expectedExcludedRedesignIds = [
  'BlackFlameRedesignV1',
  'BusyLine',
  'ChadoEnergyRedesignV1',
  'CollapseFistRedesignV1',
  'DefendNinjaSlayerRedesignV1',
  'FinisherRedesignV1',
  'KarateStraightRedesignV1',
  'StraightKiRedesignV1',
  'StrikeNinjaSlayerRedesignV1',
  'StrongShurikenTokenRedesignV1',
];
if (excludedRedesignIds.toSorted().join(',') !== expectedExcludedRedesignIds.join(',')) {
  errors.push('Redesign V1 special-card exclusions differ from the locked ten-card set');
}
for (const id of excludedRedesignIds) {
  if (redesignRewardIds.includes(id)) errors.push(`Redesign V1 special card is in rewards: ${id}`);
  if (!new RegExp(`class\\s+${id}\\b`).test(currentRedesignCardSource)
      && !filesUnder(join(root, 'Cards', 'Ancients')).some(path =>
        path.endsWith('.cs') && new RegExp(`class\\s+${id}\\b`).test(readFileSync(path, 'utf8')))) {
    errors.push(`Redesign V1 special card implementation is missing: ${id}`);
  }
}
if (new Set([...redesignRewardIds, ...excludedRedesignIds, 'OneBodyOneSoul', 'ZazenDrink']).size !== 86) {
  errors.push('The final Ninja Slayer catalog must contain 86 distinct models');
}

const redesignBasicSource = readFileSync(
  join(root, 'Cards', 'RedesignV1', 'RedesignV1BasicCards.cs'),
  'utf8',
);
for (const id of [
  'StrikeNinjaSlayerRedesignV1',
  'DefendNinjaSlayerRedesignV1',
  'KarateStraightRedesignV1',
]) {
  const classPattern = new RegExp(
    `class\\s+${id}\\s*:\\s*NinjaSlayerRedesignCardTemplate\\s*\\{`
      + `[\\s\\S]*?NinjaSlayerCardSpec\\s+Spec\\s*=\\s*new\\([^;]*CardRarity\\.Basic[^;]*\\);`,
  );
  if (!classPattern.test(redesignBasicSource)) {
    errors.push(`${id} must use the Ninja Slayer card pool and Basic starter-card rarity`);
  }
}
if (!/class\s+TurtleShellRedesignV1\s*:\s*NinjaSlayerRedesignCardTemplate[\s\S]*?CardRarity\.Rare/.test(redesignBasicSource)) {
  errors.push('TurtleShellRedesignV1 must be a Rare reward card');
}
if (!/StartingStrikeCount\s*=\s*4/.test(redesignRulesSource)
    || !/StartingDefendCount\s*=\s*5/.test(redesignRulesSource)
    || !/StartingSignatureCardCount\s*=\s*1/.test(redesignRulesSource)
    || !/AddStartingCard<StrikeNinjaSlayerRedesignV1>\(RedesignV1Rules\.StartingStrikeCount,\s*0\)/.test(entrySource)
    || !/AddStartingCard<DefendNinjaSlayerRedesignV1>\(RedesignV1Rules\.StartingDefendCount,\s*1\)/.test(entrySource)
    || !/AddStartingCard<KarateStraightRedesignV1>\(1,\s*2\)/.test(entrySource)
    || /AddStartingCard<(?:Countermeasure|TurtleShell)RedesignV1>/.test(entrySource)) {
  errors.push('The Ninja Slayer starting deck must contain four Strikes, five Defends and one Karate Straight Punch');
}
const archivedForThisRelease = [
  'CountermeasureRedesignV1',
  'ReflexGuardRedesignV1',
  'TrumpCardRedesignV1',
  'ObserverGuardRedesignV1',
  'OverexertRedesignV1',
  'ChadoSecretRedesignV1',
  'BloodTearsRedesignV1',
  'ChopChainRedesignV1',
  'DoubleForceRedesignV1',
  'EnduranceRedesignV1',
  'ExecutionMoveRedesignV1',
  'GauntletRedesignV1',
  'KarateFormRedesignV1',
  'ObserveBattleRedesignV1',
  'PunchRedesignV1',
  'ReadAndStrikeRedesignV1',
];
for (const id of archivedForThisRelease) {
  if (id === 'PunchRedesignV1') continue;
  if (!new RegExp(`class\\s+${id}\\s*:\\s*ArchivedRedesignV1Card`).test(redesignCardSource)
      || redesignRewardIds.includes(id)
      || excludedRedesignIds.includes(id)) {
    errors.push(`${id} must remain implemented but archived outside the current card catalog`);
  }
}
if (!/class\s+PunchRedesignV1\s*:\s*NinjaSlayerStandaloneCardTemplate/.test(redesignCardSource)
    || /\[RegisterCard\(typeof\(TokenCardPool\)\)\]\s*public\s+sealed\s+class\s+PunchRedesignV1/.test(redesignCardSource)
    || redesignRewardIds.includes('PunchRedesignV1')
    || excludedRedesignIds.includes('PunchRedesignV1')) {
  errors.push('PunchRedesignV1 must remain implemented but unregistered outside the current card catalog');
}
if (!redesignRewardIds.includes('TurtleShellRedesignV1')) {
  errors.push('Turtle Shell must remain a Rare reward card');
}
if (!/class\s+TurtleShellRedesignV1\b[\s\S]*?base\(Spec,\s*"BlockCard"\)/.test(redesignBasicSource)) {
  errors.push('TurtleShellRedesignV1 must reuse BlockCard.png');
}
if (!existsSync(join(root, 'NinjaSlayer', 'images', 'cards', 'BlockCard.png'))) {
  errors.push('TurtleShellRedesignV1 portrait BlockCard.png is missing');
}

if (!/class\s+SatsubatsuRedesignV1\s*:\s*RedesignV1CommonCard[\s\S]*?new\s+DamageVar\(27,\s*ValueProp\.Move\)[\s\S]*?base\(nameof\(SatsubatsuRedesignV1\),\s*nameof\(RedBlackFlame\),\s*3,\s*CardType\.Attack,\s*TargetType\.AnyEnemy\)[\s\S]*?AddGeneratedCard<BlackFlameRedesignV1>\(Owner,\s*PileType\.Hand\)[\s\S]*?UpgradeValueBy\(6\)/.test(redesignCardSource)) {
  errors.push('Satsubatsu must be a 3-cost Common 27/33 Attack that adds Black Flame to Hand and uses RedBlackFlame.png');
}
if (!/class\s+ChadoStillnessRedesignV1\s*:\s*RedesignV1CommonCard[\s\S]*?new\s+DynamicVar\("Breath",\s*1\)[\s\S]*?base\(nameof\(ChadoStillnessRedesignV1\),\s*nameof\(Meditation\),\s*1,\s*CardType\.Skill,\s*TargetType\.Self\)[\s\S]*?ChadoBreathCmd\.Apply\([\s\S]*?PowerCmd\.Apply<ChadoRetainPower>[\s\S]*?UpgradeValueBy\(1\)/.test(redesignCardSource)) {
  errors.push('Chado Stillness must be a 1-cost Common Skill with Chado Breathing 1/2 followed by Chado retention and Meditation.png');
}
if (!/class\s+BladeReserveRedesignV1\s*:\s*RedesignV1CommonCard[\s\S]*?new\s+DynamicVar\("Stock",\s*2\)[\s\S]*?new\s+CardsVar\(2\)[\s\S]*?base\(nameof\(BladeReserveRedesignV1\),\s*nameof\(ShurikenCard\),\s*1,\s*CardType\.Skill,\s*TargetType\.Self\)[\s\S]*?ShurikenOrb\.AddStock\([\s\S]*?CardPileCmd\.Draw\([\s\S]*?UpgradeValueBy\(1\)/.test(redesignCardSource)) {
  errors.push('Flying Blade must be a 1-cost Common Skill that grants 2/3 Shuriken, then draws two cards');
}

function readRedesignClassSource(id) {
  const start = new RegExp(`public\\s+sealed\\s+(?:partial\\s+)?class\\s+${id}\\b`)
    .exec(redesignCardSource)?.index;
  if (start === undefined) return '';
  const tail = redesignCardSource.slice(start);
  const next = /\npublic\s+sealed\s+(?:partial\s+)?class\s+\w+\b/.exec(tail);
  return next ? tail.slice(0, next.index) : tail;
}

const releaseCardContracts = [
  ['FlyingBladesComeRedesignV1', /DynamicVar\("Discard",\s*2\)[\s\S]*?DynamicVar\("Stock",\s*3\)[\s\S]*?ChooseAndDiscard\([\s\S]*?DynamicVars\["Discard"\]\.IntValue[\s\S]*?"Stock"\]\.UpgradeValueBy\(1\)/, 'Flying Blades Come must display and use its fixed two-card discard count, then grant 3/4 Shuriken'],
  ['KarateStraightRedesignV1', /new\s+DamageVar\(8,[\s\S]*?new\s+KarateVar\(4\)[\s\S]*?UpgradeValueBy\(2\)[\s\S]*?Karate\(\)\.UpgradeValueBy\(2\)/, 'Karate Straight Punch must deal 8/10 and grant 4/6 Karate'],
  ['ReadyStanceRedesignV1', /CardKeyword\.Exhaust[\s\S]*?new\s+KarateVar\(3\)[\s\S]*?base\([^;]*,\s*1,\s*CardType\.Skill[\s\S]*?ChooseAndDiscardOne\([\s\S]*?Karate\(\)\.UpgradeValueBy\(2\)/, 'Stance must cost 1, grant 3/5 Karate, discard one card and Exhaust'],
  ['CommonChopRedesignV1', /new\s+DamageVar\(5,[\s\S]*?new\s+KarateVar\(1\)[\s\S]*?,\s*0,\s*CardType\.Attack[\s\S]*?Apply<KaratePower>[\s\S]*?CardPileCmd\.Add\(this,\s*PileType\.Draw,\s*CardPilePosition\.Top\)[\s\S]*?Damage\.UpgradeValueBy\(1\)[\s\S]*?Karate\(\)\.UpgradeValueBy\(1\)/, 'Chop must deal 5/6, grant 1/2 Karate and move itself to the draw-pile top'],
  ['LeftHeavyPunchRedesignV1', /new\s+DamageVar\(7,[\s\S]*?new\s+KarateVar\(2\)[\s\S]*?Apply<ChopStrikeNextTurnPower>[\s\S]*?Damage\.UpgradeValueBy\(3\)[\s\S]*?Karate\(\)\.UpgradeValueBy\(1\)/, 'Chop Strike must deal 7/10 and grant 2/3 Karate next turn'],
  ['RightHeavyPunchRedesignV1', /new\s+DamageVar\(8,[\s\S]*?PowerVar<VulnerablePower>\(1\)[\s\S]*?PreviousFinishedCardWasAttack\(Owner\)[\s\S]*?Apply<VulnerablePower>[\s\S]*?Damage\.UpgradeValueBy\(2\)[\s\S]*?Vulnerable\.UpgradeValueBy\(1\)/, 'Left Heavy Punch must deal 8/10 and apply 1/2 Vulnerable after an Attack'],
  ['RightHeavyPunchAfterSkillRedesignV1', /new\s+DamageVar\(8,[\s\S]*?PowerVar<WeakPower>\(1\)[\s\S]*?PreviousFinishedCardWasSkill\(Owner\)[\s\S]*?Apply<WeakPower>[\s\S]*?Damage\.UpgradeValueBy\(2\)[\s\S]*?Weak\.UpgradeValueBy\(1\)/, 'Right Heavy Punch must deal 8/10 and apply 1/2 Weak after a Skill'],
  ['PalmThrustRedesignV1', /^(?![\s\S]*CardKeyword\.Sly)[\s\S]*new\s+DamageVar\(5,[\s\S]*?new\s+RepeatVar\(2\)[\s\S]*?TargetType\.RandomEnemy[\s\S]*?CombatTargets\.NextItem[\s\S]*?Repeat\.UpgradeValueBy\(1\)/, 'Palm Thrust must make 2/3 random 5-damage hits without Sly'],
  ['IronBodyRedesignV1', /new\s+BlockVar\(15,[\s\S]*?new\s+KarateVar\(4\)[\s\S]*?CombatTargets\.NextItem[\s\S]*?Apply<KaratePower>[\s\S]*?Block\.UpgradeValueBy\(3\)/, 'Taunt must grant 15/18 Block and give 4 Karate to a random enemy'],
  ['SpiralRoundhouseJumpRedesignV1', /new\s+DamageVar\(6,[\s\S]*?DynamicVar\("Stock",\s*1\)[\s\S]*?TargetType\.AllEnemies[\s\S]*?Damage\.UpgradeValueBy\(2\)[\s\S]*?"Stock"\]\.UpgradeValueBy\(1\)/, 'Spiral Roundhouse Jump must deal 6/8 to all and grant 1/2 Shuriken'],
  ['HiddenEdgeRedesignV1', /DynamicVar\("Stock",\s*2\)[\s\S]*?PowerVar<FocusPower>\(3\)[\s\S]*?Apply<HiddenEdgeTemporaryFocusPower>[\s\S]*?FocusPower\)\]\.UpgradeValueBy\(1\)/, 'Hidden Edge must grant 2 Shuriken and 3/4 temporary Focus'],
  ['ShurikenGenerationRedesignV1', /CardKeyword\.Exhaust[\s\S]*?,\s*2,\s*CardType\.Skill[\s\S]*?Apply<ShurikenGuardRedesignPower>[\s\S]*?EnergyCost\.UpgradeBy\(-1\)/, 'Shuriken Guard must be an Exhausting 2/1-cost Skill'],
  ['BladeCycleRedesignV1', /,\s*2,\s*CardType\.Power[\s\S]*?Apply<BladeCyclePower>[\s\S]*?EnergyCost\.UpgradeBy\(-1\)/, 'Blade Cycle must be a 2/1-cost Power'],
  ['PourTeaRedesignV1', /new\s+BlockVar\(6,[\s\S]*?DynamicVar\("Breath",\s*1\)[\s\S]*?Apply<PourTeaNextTurnPower>[\s\S]*?Block\.UpgradeValueBy\(3\)/, 'Pouring Guard must grant 6/9 Block and Chado Breathing 1 next turn'],
  ['GuidingFlameRedesignV1', /new\s+BlockVar\(16,[\s\S]*?,\s*2,\s*CardType\.Skill[\s\S]*?AddGeneratedCard<BlackFlameRedesignV1>\(Owner,\s*PileType\.Draw\)[\s\S]*?Block\.UpgradeValueBy\(3\)/, 'Black Flame Guard must cost 2, grant 16/19 Block and add Black Flame to Draw'],
  ['GuwaaRedesignV1', /IsPlayable[\s\S]*?PileType\.Hand[\s\S]*?OfType<ChadoEnergyRedesignV1>[\s\S]*?new\s+BlockVar\(4,[\s\S]*?DynamicVar\("Breath",\s*1\)[\s\S]*?ChadoBreathCmd\.Apply[\s\S]*?Block\.UpgradeValueBy\(3\)/, 'Breath Control must require Chado in hand, grant 4/7 Block and apply Chado Breathing 1'],
  ['AdversityCarapaceRedesignV1', /PowerVar<VigorPower>\(6\)[\s\S]*?Apply<VitalityTeaPower>[\s\S]*?VigorPower\)\]\.UpgradeValueBy\(2\)/, 'Vitality Tea must grant 6/8 Vigor whenever Chado Exhausts'],
  ['RedBlackFlameAttackRedesignV1', /new\s+CardsVar\(3\)[\s\S]*?DynamicVar\("BlackFlames",\s*2\)[\s\S]*?,\s*0,\s*CardType\.Skill[\s\S]*?CardPileCmd\.Draw[\s\S]*?AddGeneratedCard<BlackFlameRedesignV1>\(Owner,\s*PileType\.Draw\)[\s\S]*?Cards\.UpgradeValueBy\(1\)/, 'Burn Out must cost 0, draw 3/4 and add two Black Flames to Draw'],
  ['ThrowKunaiRedesignV1', /new\s+DamageVar\(9,[\s\S]*?new\s+CardsVar\(2\)[\s\S]*?ScryCmd\.Execute[\s\S]*?ChooseAndDiscardOne[\s\S]*?Damage\.UpgradeValueBy\(2\)[\s\S]*?Cards\.UpgradeValueBy\(1\)/, 'Throw Kunai must deal 9/11, Scry 2/3 and discard one card'],
  ['LuckyStrikeRedesignV1', /new\s+CardsVar\(1\)[\s\S]*?DynamicVar\("Draw",\s*1\)[\s\S]*?,\s*0,\s*CardType\.Skill[\s\S]*?ScryCmd\.Execute[\s\S]*?CardPileCmd\.Draw[\s\S]*?Cards\.UpgradeValueBy\(1\)[\s\S]*?"Draw"\]\.UpgradeValueBy\(1\)/, 'Ninja Sense must cost 0, Scry 1/2 and draw 1/2 cards'],
  ['AbandonThoughtRedesignV1', /DynamicVar\("Breath",\s*2\)[\s\S]*?PileType\.Draw[\s\S]*?CardCmd\.Exhaust[\s\S]*?ChadoBreathCmd\.Apply/, 'Abandon Thought must Exhaust the draw-pile top and apply Chado Breathing 2'],
  ['HookRopeRedesignV1', /PowerVar<WeakPower>\(1\)[\s\S]*?GetPowerAmount<KaratePower>[\s\S]*?Apply<HookRopeStrengthDownPower>[\s\S]*?Apply<WeakPower>[\s\S]*?Weak\.UpgradeValueBy\(1\)/, 'Hook-Rope Bind must temporarily remove current Karate as Strength and apply 1/2 Weak'],
  ['CounteroffensiveGuardRedesignV1', /new\s+BlockVar\(12,[\s\S]*?Apply<IBlockPower>[\s\S]*?Block\.UpgradeValueBy\(3\)/, 'Counteroffensive Guard must grant 12/15 Block and reuse the archived remaining-Block-to-Vigor power'],
  ['TornadoFistRedesignV1', /HasEnergyCostX\s*=>\s*true[\s\S]*?new\s+DamageVar\(4,[\s\S]*?PowerVar<VulnerablePower>\(1\)[\s\S]*?DynamicVar\("Threshold",\s*4\)[\s\S]*?hits\s*>=\s*DynamicVars\["Threshold"\][\s\S]*?Apply<VulnerablePower>[\s\S]*?Damage\.UpgradeValueBy\(2\)/, 'Tornado Fist must deal 4/6 to all X times and apply Vulnerable after each hit when X is at least 4'],
  ['BackBridgeRedesignV1', /new\s+BlockVar\(8,[\s\S]*?DynamicVar\("BlockPerKarate",\s*2\)[\s\S]*?,\s*2,\s*CardType\.Skill[\s\S]*?GetPowerAmount<KaratePower>[\s\S]*?Block\.UpgradeValueBy\(2\)[\s\S]*?"BlockPerKarate"\]\.UpgradeValueBy\(1\)/, 'Back Bridge must cost 2 and grant 8/10 Block plus 2/3 per Karate without consuming it'],
  ['ChopRedesignV1', /new\s+DamageVar\(7,[\s\S]*?new\s+KarateVar\(2\)[\s\S]*?new\s+CardsVar\(3\)[\s\S]*?,\s*1,\s*CardType\.Attack[\s\S]*?CardType\.Skill[\s\S]*?skills\s*%\s*DynamicVars\.Cards\.IntValue\s*==\s*0[\s\S]*?Damage\.UpgradeValueBy\(2\)[\s\S]*?Karate\(\)\.UpgradeValueBy\(1\)/, 'Strong Chop must cost 1, deal 7/9, grant 2/3 Karate and return every third Skill'],
  ['TurtleShellRedesignV1', /,\s*1,\s*CardType\.Skill,\s*CardRarity\.Rare[\s\S]*?CardKeyword\.Exhaust[\s\S]*?ResolveTurtleShellPlating[\s\S]*?Remove\(karate\)[\s\S]*?Apply<PlatingPower>[\s\S]*?RemoveKeyword\(CardKeyword\.Exhaust\)/, 'Turtle Shell must consume all Karate for equal Plating and lose Exhaust when upgraded'],
  ['AlabamaDropRedesignV1', /ExtraDamageVar\(6\)[\s\S]*?DynamicVar\("Dazed",\s*3\)[\s\S]*?,\s*3,\s*CardType\.Attack[\s\S]*?karate\s*\*\s*DynamicVars\.ExtraDamage\.BaseValue[\s\S]*?ValueProp\.Move\s*\|\s*ValueProp\.Unpowered[\s\S]*?Remove<KaratePower>[\s\S]*?AddGeneratedCard<Dazed>\(Owner,\s*PileType\.Draw\)[\s\S]*?ExtraDamage\.UpgradeValueBy\(2\)/, 'Alabama Drop must deal 6N/8N, clear Karate and add three Dazed to Draw'],
  ['WhiskTeaFlashRedesignV1', /new\s+DamageVar\(7,[\s\S]*?new\s+CardsVar\(2\)[\s\S]*?OfType<ChadoEnergyRedesignV1>[\s\S]*?CardPileCmd\.Draw[\s\S]*?Damage\.UpgradeValueBy\(3\)[\s\S]*?Cards\.UpgradeValueBy\(1\)/, 'Whisk Tea Flash must deal 7/10 and draw 2/3 when Chado is in hand'],
  ['OneDrinkOneStrikeRedesignV1', /new\s+DamageVar\(12,[\s\S]*?DynamicVar\("Breath",\s*2\)[\s\S]*?DiscardedCardThisTurn\(Owner\)[\s\S]*?ChadoBreathCmd\.Apply[\s\S]*?Damage\.UpgradeValueBy\(4\)/, 'One Drink One Strike must deal 12/16 and apply Chado Breathing 2 after a discard'],
  ['PreparedShurikenRedesignV1', /new\s+BlockVar\(7,[\s\S]*?DynamicVar\("Stock",\s*1\)[\s\S]*?GainBlock[\s\S]*?ShurikenOrb\.AddStock[\s\S]*?Block\.UpgradeValueBy\(2\)[\s\S]*?"Stock"\]\.UpgradeValueBy\(1\)/, 'Prepared Shuriken must grant 7/9 Block and 1/2 Shuriken'],
  ['ChopDefenseRedesignV1', /new\s+BlockVar\(6,[\s\S]*?,\s*1,\s*CardType\.Skill[\s\S]*?Apply<ReboundPower>[\s\S]*?Block\.UpgradeValueBy\(3\)/, 'Chop Defense must grant 6/9 Block and apply vanilla Rebound'],
  ['FocusedMindRedesignV1', /new\s+BlockVar\(15,[\s\S]*?PowerVar<FocusPower>\(2\)[\s\S]*?,\s*3,\s*CardType\.Skill[\s\S]*?Apply<FocusedMindNextTurnPower>[\s\S]*?Block\.UpgradeValueBy\(3\)[\s\S]*?FocusPower\)\]\.UpgradeValueBy\(1\)/, 'Focused Mind must grant 15/18 Block and 2/3 Focus next turn'],
  ['KarateTeaRedesignV1', /new\s+KarateVar\(3\)[\s\S]*?,\s*2,\s*CardType\.Power[\s\S]*?Apply<KarateTeaPower>[\s\S]*?Karate\(\)\.UpgradeValueBy\(1\)/, 'Karate Tea must be a 2-cost Power granting 3/4 Karate per generated Chado'],
  ['StrongShurikenTokenRedesignV1', /CardRarity\.Token[\s\S]*?CardKeyword\.Exhaust[\s\S]*?new\s+DamageVar\(6,[\s\S]*?Damage\.UpgradeValueBy\(3\)/, 'Strong Shuriken must be a 0-cost Exhausting 6/9 Token'],
  ['MomentumRedesignV1', /PowerVar<FocusPower>\(1\)[\s\S]*?,\s*1,\s*CardType\.Power[\s\S]*?Apply<MomentumRedesignPower>[\s\S]*?FocusPower\)\]\.UpgradeValueBy\(1\)/, 'Flying Blades Come must grant 1/2 temporary Focus per Skill'],
  ['EmptyShurikenRedesignV1', /,\s*2,\s*CardType\.Power[\s\S]*?Apply<EmptyShurikenPower>[\s\S]*?EnergyCost\.UpgradeBy\(-1\)/, 'Empty Shuriken must be a 2/1-cost Power'],
  ['TeaTeaRedesignV1', /,\s*1,\s*CardType\.Power[\s\S]*?Apply<TeaTeaPower>/, 'Tea Tea must be a 1-cost Power'],
  ['BurnBurnBurnRedesignV1', /DynamicVar\("EnemyHpLoss",\s*8\)[\s\S]*?,\s*1,\s*CardType\.Power[\s\S]*?AddGeneratedCard<BlackFlameRedesignV1>\(Owner,\s*PileType\.Hand\)[\s\S]*?Apply<BurnBurnBurnPower>[\s\S]*?UpgradeValueBy\(4\)/, 'Burn Burn Burn must add Black Flame to Hand and increase enemy HP loss by 8/12'],
  ['ReturnReturnReturnRedesignV1', /new\s+DamageVar\(5,[\s\S]*?DynamicVar\("NarakuLife",\s*3\)[\s\S]*?Apply<ReturnReturnReturnPower>[\s\S]*?Damage\.UpgradeValueBy\(1\)[\s\S]*?"NarakuLife"\]\.UpgradeValueBy\(1\)/, 'Return Return Return must deal 5/6 and grant 3/4 Naraku Life per enemy Black Flame hit this turn'],
  ['FinisherRedesignV1', /CalculationBaseVar\(4\)[\s\S]*?ExtraDamageVar\(4\)[\s\S]*?RedesignChadoInExhaustPileMultiplier[\s\S]*?RepeatVar\(4\)[\s\S]*?CalculationBase\.UpgradeValueBy\(2\)[\s\S]*?ExtraDamage\.UpgradeValueBy\(2\)/, 'Finisher must deal 4/6 four times plus 4/6 per exhausted Chado'],
];
for (const [id, pattern, message] of releaseCardContracts) {
  const source = readRedesignClassSource(id);
  if (!source || !pattern.test(source)) errors.push(message);
}

const ninjaSlayerActionsSource = readFileSync(
  join(root, 'Content', 'NinjaSlayerActions.cs'),
  'utf8',
);
if (!/ChooseAndDiscard[\s\S]*?CardSelectCmd\.FromHandForDiscard\([\s\S]*?CardSelectorPrefs\.DiscardSelectionPrompt[\s\S]*?foreach\s*\(CardModel\s+card\s+in\s+selected\)[\s\S]*?CardCmd\.Discard\(choiceContext,\s*card\)/.test(ninjaSlayerActionsSource)) {
  errors.push('Redesign discard choices must use the vanilla Survivor selection followed by CardCmd.Discard');
}

const combatMetricsSource = readFileSync(
  join(root, 'Content', 'NinjaSlayerCombatMetrics.cs'),
  'utf8',
);
const combatMetricsSnapshotSource = readFileSync(
  join(root, 'Code', 'Combat', 'CombatMetricsSnapshot.cs'),
  'utf8',
);
const resetTurnSource = /void\s+ResetTurn\(\)([\s\S]*?)\n\s*}/.exec(combatMetricsSnapshotSource)?.[1] ?? '';
if (!/AfterCardDiscarded[\s\S]*?MarkCardDiscarded\(card\.Owner\)/.test(combatMetricsSource)
    || !/AfterCardPlayed[\s\S]*?metrics\.AddFinishedCard\(\s*player,/.test(combatMetricsSource)
    || !/Dictionary<TPlayer,\s*PlayerMetrics>[\s\S]*?ReferenceEqualityComparer\.Instance/.test(combatMetricsSnapshotSource)
    || !/PreviousFinishedWasAttack\s*=\s*isAttack[\s\S]*?PreviousFinishedWasSkill\s*=\s*isSkill/.test(combatMetricsSnapshotSource)
    || /PreviousFinishedWas(?:Attack|Skill)/.test(resetTurnSource)) {
  errors.push('Discard and previous-finished-card combat metrics must be actual-hook driven, player-scoped and preserved across turns');
}

const redesignReworkPowerSource = readFileSync(
  join(root, 'Powers', 'RedesignV1ReworkPowers.cs'),
  'utf8',
);
if (!/class\s+KarateTeaPower[\s\S]*?AfterCardGeneratedForCombat\(CardModel\s+card,\s*Player\?\s+creator\)[\s\S]*?creator\?\.Creature\s*!=\s*Owner[\s\S]*?card\s+is\s+not\s+ChadoEnergyRedesignV1[\s\S]*?Apply<KaratePower>/.test(redesignReworkPowerSource)) {
  errors.push('Karate Tea must react only to newly generated redesign Chado owned by its power holder');
}
if (/class\s+CounteroffensiveGuardPower\b/.test(redesignReworkPowerSource)) {
  errors.push('Counteroffensive Guard must reuse archived IBlockPower instead of duplicating its delayed Vigor implementation');
}
if (!/class\s+ShurikenGuardRedesignPower[\s\S]*?TotalDamage\s*\+\s*result\.OverkillDamage/.test(redesignReworkPowerSource)) {
  errors.push('Shuriken Guard must count TotalDamage plus OverkillDamage for every result in a Shuriken wave');
}
if (!/class\s+StarlessNightRedesignPower[\s\S]*?PowerStackType\.Single[\s\S]*?GenerateStrongShuriken/.test(redesignReworkPowerSource)) {
  errors.push('Starless Night must be single-stack and generate at most one Strong Shuriken per resolution chain');
}

const karateDamageWavePatchSource = readFileSync(
  join(root, 'Code', 'Patches', 'KarateDamageWavePatch.cs'),
  'utf8',
);
if (!/bool\s+__runOriginal[\s\S]*?if\s*\(\s*!__runOriginal/.test(karateDamageWavePatchSource)) {
  errors.push('Karate damage waves must ignore Harmony calls whose original damage method did not run');
}

const roundhouseSource = /class\s+RoundhouseKickRedesignV1\s*:\s*RedesignV1UncommonCard([\s\S]*?)(?=\npublic\s+sealed\s+class\s+)/
  .exec(redesignCardSource)?.[1] ?? '';
if (!/AfterAutoPostPlayPhaseEntered\s*\(/.test(roundhouseSource)
    || !/PileType\.Draw\.GetPile\(Owner\)\.Cards[\s\S]*?Count\s*>\s*0\s*&&\s*drawPile\[0\]\s*==\s*this/.test(roundhouseSource)
    || !/CardPileCmd\.AutoPlayFromDrawPile\([\s\S]*?CardPilePosition\.Top,[\s\S]*?forceExhaust:\s*false\)/.test(roundhouseSource)
    || /BeforeSideTurnEnd\s*\(/.test(roundhouseSource)) {
  errors.push('Roundhouse Kick must be Uncommon and use the vanilla I Am Invincible draw-pile auto-play route');
}

const redesignPowerSource = readFileSync(join(root, 'Powers', 'RedesignV1Powers.cs'), 'utf8');
const chadoRetainPowerSource = /class\s+ChadoRetainPower\s*:\s*RedesignV1CounterPower([\s\S]*?)(?=\n\[RegisterPower\]|\npublic\s+sealed\s+class\s+)/
  .exec(redesignPowerSource)?.[1] ?? '';
if (!/NinjaSlayerPowerAssets\.Named\("EndTurnRetainPower"\)/.test(chadoRetainPowerSource)
    || !/BeforeFlush\s*\([\s\S]*?OfType<ChadoEnergyRedesignV1>\(\)[\s\S]*?GiveSingleTurnRetain\(\)/.test(chadoRetainPowerSource)
    || !/AfterSideTurnEnd\s*\([\s\S]*?PowerCmd\.Decrement\(this\)/.test(chadoRetainPowerSource)) {
  errors.push('Chado retention must mark only Chado before hand flush and decrement once after each owner turn');
}

if (!/class\s+BlackFlameRedesignV1\b[\s\S]*?nameof\(BurningCard\)/.test(redesignCardSource)
    || !/class\s+RedBlackFlameAttackRedesignV1\b[\s\S]*?base\(nameof\(RedBlackFlameAttackRedesignV1\),\s*nameof\(ImpureFlame\)/.test(redesignCardSource)) {
  errors.push('Black Flame must use BurningCard.png and Red and Black Flame must use ImpureFlame.png');
}
const blackFlameSource = readRedesignClassSource('BlackFlameRedesignV1');
if (!/ValueProp\.Unblockable\s*\|\s*ValueProp\.Unpowered/.test(blackFlameSource)
    || !/AfterCardPlayed[\s\S]*?Pile\?\.Type\s*==\s*PileType\.Hand[\s\S]*?CardType\.Attack[\s\S]*?DamageEnemies/.test(blackFlameSource)
    || !/OnTurnEndInHand[\s\S]*?creature\.Side\s*==\s*Owner\.Creature\.Side[\s\S]*?DamageEnemies[\s\S]*?CreatureCmd\.Damage[\s\S]*?CardCmd\.Exhaust/.test(blackFlameSource)
    || !/creature\.IsAlive\s*&&\s*creature\.Side\s*!=\s*Owner\.Creature\.Side/.test(blackFlameSource)) {
  errors.push('Black Flame must deal real unpowered, unblockable HP loss after each Attack and at turn end to its owner and living enemies only');
}

const hellTornadoPowerSource = readFileSync(join(root, 'Powers', 'HellTornadoPower.cs'), 'utf8');
const productionCSharpSource = sourceFiles
  .filter(path => path.endsWith('.cs'))
  .map(path => readFileSync(path, 'utf8'))
  .join('\n');
if (!/class\s+HellTornadoRedesignV1[\s\S]*?HoverTipFactory\.FromPower<SoarPower>\(\)[\s\S]*?PowerCmd\.Apply<SoarPower>/.test(redesignCardSource)
    || !/class\s+HellTornadoRedesignPower[\s\S]*?PowerCmd\.Remove<SoarPower>/.test(hellTornadoPowerSource)
    || /class\s+SoarPower\b/.test(productionCSharpSource)) {
  errors.push('Hell Tornado must apply and remove the host SoarPower directly; the Mod must not define its own SoarPower');
}
for (const obsoleteBlackFlameAsset of ['BlackFlame.png', 'BlackFlame.png.import']) {
  if (existsSync(join(root, 'NinjaSlayer', 'images', 'cards', obsoleteBlackFlameAsset))) {
    errors.push(`Obsolete One Body One Soul portrait must remain deleted: ${obsoleteBlackFlameAsset}`);
  }
}

const tokenPoolRedesignIds = [...redesignCardSource.matchAll(
  /\[RegisterCard\(typeof\(TokenCardPool\)\)\]\s*public\s+sealed\s+class\s+(\w+)/g,
)].map(match => match[1]).sort();
const expectedColorlessRedesignIds = [
  'ChadoEnergyRedesignV1',
  'FinisherRedesignV1',
  'StraightKiRedesignV1',
  'StrongShurikenTokenRedesignV1',
];
if (tokenPoolRedesignIds.join(',') !== expectedColorlessRedesignIds.join(',')) {
  errors.push('Only Redesign generated tokens may use the colorless card pool');
}
const statusPoolRedesignIds = [...redesignCardSource.matchAll(
  /\[RegisterCard\(typeof\(StatusCardPool\)\)\]\s*public\s+sealed\s+class\s+(\w+)/g,
)].map(match => match[1]).sort();
if (statusPoolRedesignIds.join(',') !== 'BlackFlameRedesignV1') {
  errors.push('Black Flame must be the only Redesign status-pool card');
}

const collapseFistSource = readFileSync(join(root, 'Cards', 'Ancients', 'CollapseFist.cs'), 'utf8');
if (!/\[RegisterCard\(typeof\(NinjaSlayerCardPool\)\)\]\s*public\s+sealed\s+class\s+CollapseFistRedesignV1/.test(collapseFistSource)) {
  errors.push('CollapseFistRedesignV1 must use the Ninja Slayer card pool');
}
if (!/\.Character<NinjaSlayerCharacter>\(ConfigureStartingDeck\)/.test(entrySource)
    || /NinjaSlayerRedesignCharacter|NinjaSlayerRedesignCardPool/.test(entrySource + redesignCardSource)) {
  errors.push('Ninja Slayer must use one character and one card pool');
}
if (!/\.Card<NinjaSlayerCardPool,\s*OneBodyOneSoul>\(\)/.test(entrySource)
    || !/\.Card<NinjaSlayerCardPool,\s*ZazenDrink>\(\)/.test(entrySource)) {
  errors.push('One Body One Soul and Zazen Drink must remain in the Ninja Slayer card pool');
}

const redesignArtOwners = new Map();
for (const id of redesignRewardIds) {
  const artMatch = id === 'TurtleShellRedesignV1'
    ? null
    : new RegExp(
      `:\\s*base\\(nameof\\(${id}\\),\\s*(?:nameof\\((\\w+)\\)|"([^"]+)")`,
    ).exec(redesignCardSource);
  const artName = id === 'TurtleShellRedesignV1'
    ? 'BlockCard'
    : artMatch?.[1] ?? artMatch?.[2];
  if (!artName) {
    errors.push(`Unable to resolve Redesign V1 card art: ${id}`);
    continue;
  }
  const previousOwner = redesignArtOwners.get(artName);
  if (previousOwner) errors.push(`Redesign V1 reward art ${artName}.png is shared by ${previousOwner} and ${id}`);
  else redesignArtOwners.set(artName, id);

  const portraitPath = join(root, 'NinjaSlayer', 'images', 'cards', `${artName}.png`);
  if (!readPngSize(portraitPath)) {
    if (!existsSync(portraitPath)) errors.push(`Missing Redesign V1 reward portrait: ${artName}.png`);
  }

  if (id === 'BladeCycleRedesignV1' && artName !== 'ShurikenBarrage') {
    errors.push('BladeCycleRedesignV1 must use ShurikenBarrage.png');
  }
}

const expectedRedesignLocalizationStems = new Set(
  [...redesignRewardIds, ...excludedRedesignIds].map(id =>
    `NINJA_SLAYER_CARD_${id.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toUpperCase()}`),
);
const archivedRedesignLocalizationStems = new Set(
  archivedForThisRelease.map(id =>
    `NINJA_SLAYER_CARD_${id.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toUpperCase()}`),
);
for (const language of ['eng', 'zhs']) {
  const actualStems = new Set(Object.keys(redesignCardsByLanguage[language])
    .map(key => key.replace(/\.(?:title|description|selectionScreenPrompt)$/, '')));
  const missing = [...expectedRedesignLocalizationStems].filter(stem => !actualStems.has(stem));
  const extra = [...actualStems].filter(stem =>
    !expectedRedesignLocalizationStems.has(stem)
      && !archivedRedesignLocalizationStems.has(stem));
  if (missing.length > 0 || extra.length > 0) {
    errors.push(
      `${language}/cards.json Redesign localization differs from the active 87-card set `
      + `(missing: ${missing.join(', ') || 'none'}; extra: ${extra.join(', ') || 'none'})`,
    );
  }
}

const obsoleteShurikenPowerPaths = [
  join(root, 'Powers', 'ShurikenStockPower.cs'),
  join(root, 'NinjaSlayer', 'images', 'powers', 'ShurikenStockPower.png'),
  join(root, 'NinjaSlayer', 'images', 'powers', 'ShurikenStockPower.png.import'),
];
for (const path of obsoleteShurikenPowerPaths) {
  if (existsSync(path)) errors.push(`${relative(root, path)} must be deleted after Shuriken becomes an Orb`);
}

const shurikenOrbSource = readFileSync(join(root, 'Orbs', 'ShurikenOrb.cs'), 'utf8');
if (!/\[RegisterOrb\][\s\S]*?class\s+ShurikenOrb\s*:\s*ModOrbTemplate/.test(shurikenOrbSource)
    || !/ModOrbValueDisplayMode\.Both/.test(shurikenOrbSource)
    || !/EvokeVal\s*=>[\s\S]*?ModifyOrbValue\(ShurikenCombat\.GetStockBaseDamage/.test(shurikenOrbSource)
    || !/int\s+StackCount\s*\{\s*get;\s*private set;\s*\}/.test(shurikenOrbSource)
    || !/bool\s+OwnsTransientSlot\s*\{\s*get;\s*private set;\s*\}/.test(shurikenOrbSource)
    || !/SavedDataSlot\s*=\s*"shuriken_orb_state"/.test(shurikenOrbSource)
    || !/RegisterComputed<ShurikenOrb,\s*ShurikenOrbState>/.test(shurikenOrbSource)) {
  errors.push('Shuriken must be a saved, stackable Orb with the vanilla Dark Orb value layout');
}
if (!/override\s+(?:async\s+)?Task\s+AfterCardDiscarded\b/.test(shurikenOrbSource)
    || !/override\s+Task\s+AfterShuffle\b/.test(shurikenOrbSource)
    || /override\s+(?:async\s+)?Task\s+AfterCardPlayed\b/.test(shurikenOrbSource)
    || !/ResolveBladeCycleShuffle[\s\S]*?HasPower<BladeCyclePower>/.test(shurikenOrbSource)) {
  errors.push('The Shuriken Orb must spend one stack on discard and reserve shuffle firing for Blade Cycle');
}
if (!/bool\s+_fireAllStockOnNextEvoke/.test(shurikenOrbSource)
    || !/bool\s+_consumeOneStockOnNextEvoke/.test(shurikenOrbSource)
    || !/bool\s+_generatedStrongShurikenInEvokeChain/.test(shurikenOrbSource)
    || !/IsPreparedForReplacementEvoke\s*=>\s*_fireAllStockOnNextEvoke/.test(shurikenOrbSource)
    || !/PrepareForReplacementEvoke\(\)[\s\S]*?_fireAllStockOnNextEvoke\s*=\s*true[\s\S]*?_completeEvokeChainOnNextEvoke\s*=\s*true/.test(shurikenOrbSource)
    || !/PrepareForSingleStockEvoke\(\)[\s\S]*?StackCount\s*<=\s*0[\s\S]*?_consumeOneStockOnNextEvoke\s*=\s*true[\s\S]*?_completeEvokeChainOnNextEvoke\s*=\s*true/.test(shurikenOrbSource)
    || !/override\s+async\s+Task<IEnumerable<Creature>>\s+Evoke[\s\S]*?bool\s+fireAllStock\s*=\s*_fireAllStockOnNextEvoke[\s\S]*?int\s+shots\s*=\s*fireAllStock\s*\?\s*StackCount\s*:\s*1[\s\S]*?bool\s+consumeOneStock\s*=\s*_consumeOneStockOnNextEvoke[\s\S]*?_fireAllStockOnNextEvoke\s*=\s*false[\s\S]*?_consumeOneStockOnNextEvoke\s*=\s*false[\s\S]*?index\s*<\s*shots[\s\S]*?FireOne\([^;]*evoke:\s*true\)[\s\S]*?if\s*\(consumeOneStock\s*&&\s*fired\)[\s\S]*?StackCount--[\s\S]*?RemoveDepletedOrb\(\)[\s\S]*?RefreshVisuals\(\)[\s\S]*?ReleaseTransientSlotIfRemoved/.test(shurikenOrbSource)
    || !/RemoveDepletedOrb[\s\S]*?OrbQueue\.Remove\(this\)[\s\S]*?EvokeOrbAnim\(this\)[\s\S]*?RemoveInternal\(\)/.test(shurikenOrbSource)) {
  errors.push('Shuriken ordinary and multi-evoke effects must spend one stock total, while displacement fires all stock');
}
if (!/ShurikenOrb\.RegisterSavedData\(NinjaSlayerIds\.ModId\)/.test(entrySource)
    || !/RegisterPatch<ShurikenOrbChannelPatch>/.test(entrySource)
    || !/RegisterPatch<ShurikenOrbEvokePatch>/.test(entrySource)
    || !/RegisterPatch<ShurikenOrbLayoutPatch>/.test(entrySource)
    || !/typeof\(ShurikenOrbVisual\)/.test(entrySource)) {
  errors.push('Entry must register Shuriken Orb saved data, channel, evoke and layout Patches, and its Godot visual script');
}
const ninjaSlayerCharacterSource = readFileSync(join(root, 'Content', 'NinjaSlayerCharacter.cs'), 'utf8');
if (!/override\s+int\s+BaseOrbSlotCount\s*=>\s*0/.test(ninjaSlayerCharacterSource)) {
  errors.push('Ninja Slayer must have zero base Orb slots');
}
const shurikenChannelPatchSource = readFileSync(
  join(root, 'Code', 'Patches', 'ShurikenOrbChannelPatch.cs'),
  'utf8',
);
if (!/nameof\(OrbCmd\.Channel\)/.test(shurikenChannelPatchSource)
    || !/queue\.Orbs\[0\]\s+is\s+ShurikenOrb\s+shuriken/.test(shurikenChannelPatchSource)
    || !/PrepareForReplacementEvoke\(\)/.test(shurikenChannelPatchSource)
    || !/OwnsTransientSlot[\s\S]*?TransferTransientSlot\(\)/.test(shurikenChannelPatchSource)) {
  errors.push('Channeling into a full queue must fire all Shuriken stock and transfer its transient slot');
}
if (!/class\s+ShurikenOrbEvokePatch\s*:\s*IPatchMethod/.test(shurikenChannelPatchSource)
    || !/typeof\(OrbCmd\)[\s\S]*?"Evoke"[\s\S]*?typeof\(PlayerChoiceContext\)[\s\S]*?typeof\(Player\)[\s\S]*?typeof\(OrbModel\)[\s\S]*?typeof\(bool\)/.test(shurikenChannelPatchSource)
    || !/Prefix\(OrbModel\s+evokedOrb,\s*ref\s+bool\s+dequeue\)/.test(shurikenChannelPatchSource)
    || !/evokedOrb\s+is\s+not\s+ShurikenOrb\s+shuriken[\s\S]*?shuriken\.IsPreparedForReplacementEvoke[\s\S]*?if\s*\(!dequeue\)[\s\S]*?PrepareForContinuingEvoke\(\)[\s\S]*?return[\s\S]*?dequeue\s*=\s*false[\s\S]*?PrepareForSingleStockEvoke\(\)/.test(shurikenChannelPatchSource)) {
  errors.push('Ordinary and multi-evoke effects must preserve the Shuriken Orb and consume one stock on their final evoke');
}
if (!/class\s+ShurikenOrbLayoutPatch\s*:\s*IPatchMethod/.test(shurikenChannelPatchSource)
    || !/typeof\(NOrbManager\),\s*"TweenLayout"/.test(shurikenChannelPatchSource)
    || !/Player\?\.Character\s+is\s+not\s+INinjaSlayerCharacter/.test(shurikenChannelPatchSource)
    || !/FirstOrDefault\(orb\s*=>\s*orb\.Model\s+is\s+ShurikenOrb\)/.test(shurikenChannelPatchSource)
    || !/Where\(orb\s*=>\s*!ReferenceEquals\(orb,\s*shuriken\)\)/.test(shurikenChannelPatchSource)
    || !/ShurikenOrbLayoutMath\.GetStandardPosition\([\s\S]*?__instance\.IsLocal/.test(shurikenChannelPatchSource)
    || !/LayoutSeconds\s*=\s*0\.45/.test(shurikenChannelPatchSource)) {
  errors.push('Ninja Slayer Shuriken must be excluded from the vanilla-indexed 0.45s Orb layout');
}
const portableIrcSource = readFileSync(join(root, 'Relics', 'PortableIrcTerminalRelic.cs'), 'utf8');
if (/AddGeneratedShuriken/.test(portableIrcSource)
    || !/ShurikenOrb\.AddStock\(choiceContext,\s*Owner,\s*1\)/.test(portableIrcSource)) {
  errors.push('Portable IRC Terminal must grant one Shuriken stack without generating a Token card');
}
for (const tokenFile of ['ShurikenCard.cs', 'GiantShurikenCard.cs']) {
  const tokenSource = readFileSync(join(root, 'Cards', 'Tokens', tokenFile), 'utf8');
  if (/\[RegisterCard\b/.test(tokenSource)) {
    errors.push(`${tokenFile} must not be registered for old-save compatibility`);
  }
}
const shurikenOrbVisualSource = readFileSync(
  join(root, 'Code', 'Nodes', 'ShurikenOrbVisual.cs'),
  'utf8',
);
if (!/GlowColorHex\s*=\s*"#FFB300"/.test(shurikenOrbVisualSource)
    || !/\.Triggered\s*\+=\s*Pulse/.test(shurikenOrbVisualSource)
    || !/\.PassiveActivated\s*\+=\s*Pulse/.test(shurikenOrbVisualSource)
    || !/\.EvokeActivated\s*\+=\s*PulseAfterEvoke/.test(shurikenOrbVisualSource)
    || !/Duplicate\(\)[\s\S]*?TweenProperty\(pulse,\s*"scale"[\s\S]*?_sparks\.Restart\(\)/.test(shurikenOrbVisualSource)) {
  errors.push('Shuriken Orb highlights must be model-event-driven and use the approved #FFB300 pulse');
}
if (!/RenderingServer\.FramePreDraw\s*\+=\s*SyncNow/.test(shurikenOrbVisualSource)
    || !/RenderingServer\.FramePreDraw\s*-=\s*SyncNow/.test(shurikenOrbVisualSource)
    || !/_labelContainer\.ZIndex\s*=\s*4/.test(shurikenOrbVisualSource)
    || !/GetGlobalTransformWithCanvas\(\)[\s\S]*?AffineInverse\(\)/.test(shurikenOrbVisualSource)
    || !/_deformedVisuals\.Transform\s*=\s*new\s+Transform2D\(x,\s*y,\s*Vector2\.Zero\)/.test(shurikenOrbVisualSource)
    || !/StackCount:\s*>\s*0/.test(shurikenOrbVisualSource)) {
  errors.push('Shuriken Orb slot 0 must follow the live body transform while labels remain upright and on top');
}
const ninjaSlayerVisualSceneSource = readFileSync(
  join(root, 'NinjaSlayer', 'scenes', 'creature_visuals', 'ninja_slayer.tscn'),
  'utf8',
);
if (/HeldShurikenVisual|NinjaSlayerHeldShurikenVisual|ninja_slayer_shuriken/.test(ninjaSlayerVisualSceneSource)) {
  errors.push('The creature scene must not retain the retired body-mounted Shuriken visual');
}
if (!/\[node name="OrbPos" type="Marker2D" parent="\."\][\s\S]*?unique_name_in_owner\s*=\s*true[\s\S]*?position\s*=\s*Vector2\(0,\s*-328\)/.test(ninjaSlayerVisualSceneSource)) {
  errors.push('Ninja Slayer must use the vanilla-compatible OrbPos (0, -328) for all non-Shuriken slots');
}
const shurikenOrbSceneSource = readFileSync(
  join(root, 'NinjaSlayer', 'scenes', 'orbs', 'shuriken_orb.tscn'),
  'utf8',
);
if (!/\[node name="EdgeGlow"[\s\S]*?material\s*=\s*SubResource\("CanvasItemMaterial_additive"\)/.test(shurikenOrbSceneSource)
    || !/blend_mode\s*=\s*1/.test(shurikenOrbSceneSource)
    || !/\[node name="DeformedVisuals" type="Node2D" parent="\."\]/.test(shurikenOrbSceneSource)
    || !/\[node name="Body"[\s\S]*?scale\s*=\s*Vector2\(0\.175,\s*0\.175\)/.test(shurikenOrbSceneSource)
    || !/\[node name="Sparks"[\s\S]*?one_shot\s*=\s*true/.test(shurikenOrbSceneSource)
    || /\nrotation\s*=/.test(shurikenOrbSceneSource)) {
  errors.push('The Shuriken Orb scene must keep a 60px still body with additive edge glow and burst particles');
}
const shurikenCombatSource = readFileSync(join(root, 'Cards', 'Base', 'ShurikenCombat.cs'), 'utf8');
if (!/NShivThrowVfx\.Create\(origin,\s*targetPosition,\s*TrailTint\)/.test(shurikenCombatSource)
    || !/TrailEnabled\s*=\s*false/.test(shurikenCombatSource)
    || !/VisibleDiameter\s*=\s*60f[\s\S]*?ProjectileScale\s*=\s*VisibleDiameter\s*\/\s*SourceVisibleDiameter/.test(shurikenCombatSource)
    || !/ScaleMin\s*=\s*ProjectileScale[\s\S]*?ScaleMax\s*=\s*ProjectileScale/.test(shurikenCombatSource)
    || !/HeadAngularVelocity\s*=\s*360f\s*\/\s*FlightSeconds/.test(shurikenCombatSource)
    || !/AngularVelocityMin\s*=\s*HeadAngularVelocity[\s\S]*?AngularVelocityMax\s*=\s*HeadAngularVelocity/.test(shurikenCombatSource)
    || !/CreatureCmd\.Damage\([\s\S]*?originOrb\.EvokeVal/.test(shurikenCombatSource)
    || !/NOrb\s*\{\s*Model:\s*ShurikenOrb\s+model\s*\}[\s\S]*?FindShurikenVisual\(node\)\?\.SyncNow\(\)[\s\S]*?node\.GlobalPosition/.test(shurikenCombatSource)
    || !/beforeThrow\(\)[\s\S]*?daggerThrow[\s\S]*?CreateThrowVfx/.test(shurikenCombatSource)
    || /trail\.Texture\s*=/.test(shurikenCombatSource)
    || /NShivThrowVfx\.Create\([^;]*Colors\.Green/.test(shurikenCombatSource)) {
  errors.push('Shuriken throws must flash at the Orb origin while preserving the vanilla Shiv trail and neutral head');
}
const shurikenProjectilePath = join(
  root,
  'NinjaSlayer',
  'images',
  'projectiles',
  'ninja_slayer_shuriken.png',
);
const shurikenProjectileSize = readPngSize(shurikenProjectilePath);
if (!shurikenProjectileSize
    || shurikenProjectileSize[0] !== 355
    || shurikenProjectileSize[1] !== 355) {
  errors.push('ninja_slayer_shuriken.png must be the authored 355x355 source image');
} else {
  const shurikenHash = createHash('sha256')
    .update(readFileSync(shurikenProjectilePath))
    .digest('hex')
    .toUpperCase();
  if (shurikenHash !== '64E3C2B46E765EFE5E2FD4A957DFD2C44BB538B4EE2EFAD933C666BC45A4AAA0') {
    errors.push('ninja_slayer_shuriken.png differs from the approved source asset');
  }
}
for (const [language, obsoleteName] of [['eng', 'Shuriken Stock'], ['zhs', '手里剑库存']]) {
  const localizationRoot = join(root, 'NinjaSlayer', 'localization', language);
  for (const file of ['cards.json', 'powers.json']) {
    const values = Object.values(readJson(join(localizationRoot, file)) ?? {});
    if (values.some(value => typeof value === 'string' && value.includes(obsoleteName))) {
      errors.push(`${language}/${file} still uses the retired ${obsoleteName} display name`);
    }
  }
  const orbLocalization = readJson(join(localizationRoot, 'orbs.json')) ?? {};
  const orbStem = 'NINJA_SLAYER_ORB_SHURIKEN_ORB';
  for (const suffix of ['title', 'description', 'smartDescription']) {
    if (typeof orbLocalization[`${orbStem}.${suffix}`] !== 'string') {
      errors.push(`${language}/orbs.json is missing ${orbStem}.${suffix}`);
    }
  }
  const powerLocalization = readJson(join(localizationRoot, 'powers.json')) ?? {};
  if (Object.keys(powerLocalization).some(key => key.startsWith('NINJA_SLAYER_POWER_SHURIKEN_STOCK_POWER.'))) {
    errors.push(`${language}/powers.json still localizes the deleted Shuriken Stock Power`);
  }
}
const bladeCyclePowerSource = /class\s+BladeCyclePower\b([\s\S]*?)class\s+HardItOutPower\b/
  .exec(redesignPowerSource)?.[1] ?? '';
if (!/PowerStackType\.Single/.test(bladeCyclePowerSource)
    || /AfterShuffle\s*\(/.test(bladeCyclePowerSource)) {
  errors.push('Blade Cycle must be a single non-dispatching preservation marker');
}

const placeholderInventoryPath = join(root, 'Docs', 'placeholder-assets.json');
const placeholderInventory = readJson(placeholderInventoryPath);
if (placeholderInventory) {
  const items = Array.isArray(placeholderInventory.items) ? placeholderInventory.items : [];
  const ids = new Set();
  for (const item of items) {
    if (typeof item.id !== 'string' || ids.has(item.id)) {
      errors.push(`Docs/placeholder-assets.json contains a missing or duplicate id: ${item.id ?? '<missing>'}`);
    }
    ids.add(item.id);
    if (typeof item.source !== 'string' || !existsSync(join(root, ...item.source.split('/')))) {
      errors.push(`Placeholder ${item.id ?? '<missing>'} references missing source ${item.source ?? '<missing>'}`);
    }
    for (const field of ['currentAsset', 'targetAsset']) {
      if (typeof item[field] !== 'string' || item[field].length === 0) {
        errors.push(`Placeholder ${item.id ?? '<missing>'} must define ${field}`);
      }
    }
    if (typeof item.releaseBlocking !== 'boolean') {
      errors.push(`Placeholder ${item.id ?? '<missing>'} must define boolean releaseBlocking`);
    }
  }
}

for (const language of ['zhs', 'eng']) {
  const localizedCards = readJson(join(root, 'NinjaSlayer', 'localization', language, 'cards.json')) ?? {};
  const chainedValueFormatter = /\{[A-Za-z][A-Za-z0-9]*(?::(?:abs|diff|inverseDiff|energyIcons|starIcons|percentMore|percentLess)(?:\([^{}]*\))?){2,}\}/;
  for (const [key, description] of Object.entries(localizedCards)) {
    if (key.includes('_REDESIGN_V1.description')
        && typeof description === 'string'
        && chainedValueFormatter.test(description)) {
      errors.push(`${language} ${key} uses unsupported chained value formatters`);
    }
  }
}

const cardLocalization = readJson(join(root, 'NinjaSlayer', 'localization', 'zhs', 'cards.json')) ?? {};
const catalog = readFileSync(join(root, 'Docs', 'card-catalog.md'), 'utf8');
for (const [key, title] of Object.entries(cardLocalization)) {
  if (key.endsWith('.title') && typeof title === 'string' && !catalog.includes(title)) {
    errors.push(`Card catalog is missing localized title: ${title}`);
  }
}

const assetManifest = readFileSync(join(root, 'ASSET_MANIFEST.md'), 'utf8');
if (!assetManifest.includes('NinjaSlayer_idle_0022.png') || assetManifest.includes('NinjaSlayer_idle_0030.png')) {
  errors.push('ASSET_MANIFEST.md does not describe the 22-frame idle animation');
}
if (!assetManifest.includes('ninja_slayer_shuriken.png')
    || !assetManifest.includes('64E3C2B46E765EFE5E2FD4A957DFD2C44BB538B4EE2EFAD933C666BC45A4AAA0')) {
  errors.push('ASSET_MANIFEST.md does not record the approved Shuriken projectile source');
}
const powerClasses = filesUnder(join(root, 'Powers'))
  .filter((path) => path.endsWith('.cs'))
  .flatMap((path) => {
    const source = readFileSync(path, 'utf8');
    const matches = [...source.matchAll(/public\s+sealed\s+(?:partial\s+)?class\s+(\w+Power)\b/g)];
    return matches.map((match, index) => ({
      name: match[1],
      source: source.slice(match.index, matches[index + 1]?.index ?? source.length),
    }));
  });
for (const powerClass of powerClasses) {
  const reusedIconMatch = powerClass.source.match(
    /NinjaSlayerPowerAssets\.Named\((?:nameof\((\w+Power)\)|"(\w+Power)")\)/,
  );
  const reusedIcon = reusedIconMatch?.[1] ?? reusedIconMatch?.[2];
  const iconName = reusedIcon ?? powerClass.name;
  const iconPath = join(root, 'NinjaSlayer', 'images', 'powers', `${iconName}.png`);
  const iconSize = readPngSize(iconPath);
  if (!iconSize) {
    if (!existsSync(iconPath)) errors.push(`Missing power icon: ${iconName}.png`);
  } else if (iconSize[0] !== 256 || iconSize[1] !== 256) {
    errors.push(`${iconName}.png must be 256x256, found ${iconSize[0]}x${iconSize[1]}`);
  }
}
if (existsSync(join(root, 'NinjaSlayer', 'images', 'powers', 'soar_power.png'))) {
  errors.push('Shared soar_power.png fallback should not remain after dedicated power art is complete');
}

const ancillaryArt = [
  ['NinjaSlayer/images/ancients/NancyLeeMapIcon.png', 278, 278],
  ['NinjaSlayer/images/ancients/NancyLeeMapIcon_outline.png', 278, 278],
  ['NinjaSlayer/images/ancients/NancyLeeRunHistoryIcon.png', 128, 128],
  ['NinjaSlayer/images/ancients/NancyLeeRunHistoryIcon_outline.png', 128, 128],
  ['NinjaSlayer/images/potions/ZbrAmpoulePotion.png', 256, 256],
  ['NinjaSlayer/images/potions/ZbrAmpoulePotion_outline.png', 256, 256],
  ['NinjaSlayer/images/enchantments/BlackFlameEnchantment.png', 64, 64],
];
for (const [assetPath, expectedWidth, expectedHeight] of ancillaryArt) {
  const path = join(root, ...assetPath.split('/'));
  const size = readPngSize(path);
  if (!size) {
    if (!existsSync(path)) errors.push(`Missing dedicated ancillary art: ${assetPath}`);
    continue;
  }
  if (size[0] !== expectedWidth || size[1] !== expectedHeight) {
    errors.push(`${assetPath} must be ${expectedWidth}x${expectedHeight}, found ${size[0]}x${size[1]}`);
  }
}
const nancyLeeSource = readFileSync(join(root, 'Ancients', 'NancyLee.cs'), 'utf8');
if (/res:\/\/icon\.svg/.test(nancyLeeSource)) {
  errors.push('NancyLee must not use the project icon.svg as an Ancient presentation asset');
}
if (!assetManifest.includes('BlackFlameEnchantment.png') || !assetManifest.includes('NancyLeeMapIcon.png')) {
  errors.push('ASSET_MANIFEST.md does not describe the dedicated ancillary art set');
}

if (errors.length > 0) {
  console.error(errors.map((error) => `- ${error}`).join('\n'));
  process.exit(1);
}

console.log('Repository consistency checks passed.');
