import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
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
      Object.entries(cards).filter(([key]) => key.includes('_REDESIGN_V1.')),
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
      if (typeof value !== 'string' || value.trim().length === 0) {
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
  const englishFields = [...redesignCardsByLanguage.eng[key].matchAll(/\{([A-Za-z_][A-Za-z0-9_]*)(?=[:}])/g)]
    .map((match) => match[1])
    .sort();
  const chineseFields = [...redesignCardsByLanguage.zhs[key].matchAll(/\{([A-Za-z_][A-Za-z0-9_]*)(?=[:}])/g)]
    .map((match) => match[1])
    .sort();
  if (JSON.stringify(englishFields) !== JSON.stringify(chineseFields)) {
    errors.push(`Redesign V1 format fields differ between eng and zhs for ${key}`);
  }
}

for (const language of ['eng', 'zhs']) {
  const characters = readJson(join(root, 'NinjaSlayer', 'localization', language, 'characters.json')) ?? {};
  const visiblePrefix = 'NINJA_SLAYER_CHARACTER_NINJA_SLAYER_CHARACTER.';
  const redesignPrefix = 'NINJA_SLAYER_CHARACTER_NINJA_SLAYER_REDESIGN_CHARACTER.';
  const visible = Object.fromEntries(
    Object.entries(characters)
      .filter(([key]) => key.startsWith(visiblePrefix))
      .map(([key, value]) => [key.slice(visiblePrefix.length), value]),
  );
  const redesign = Object.fromEntries(
    Object.entries(characters)
      .filter(([key]) => key.startsWith(redesignPrefix))
      .map(([key, value]) => [key.slice(redesignPrefix.length), value]),
  );
  if (JSON.stringify(visible) !== JSON.stringify(redesign)) {
    errors.push(`${language}/characters.json must give visible and Redesign Ninja Slayer identical localization`);
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
const redesignStarterRelicSource = readFileSync(
  join(root, 'Relics', 'RedesignV1ChadoBreathingRelic.cs'),
  'utf8',
);
if (!/protected\s+virtual\s+int\s+ChadoCount\s*=>\s*0/.test(redesignStarterRelicSource)
    || !/protected\s+override\s+int\s+ChadoCount\s*=>\s*2/.test(redesignStarterRelicSource)
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
  ['Common', 19, readRedesignCardIds('CommonRewardCardIds')],
  ['Uncommon', 32, readRedesignCardIds('UncommonRewardCardIds')],
  ['Rare', 17, readRedesignCardIds('RareRewardCardIds')],
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
  const missing = ids.filter(id => !implementedIds.includes(id));
  const extra = implementedIds.filter(id => !ids.includes(id));
  if (missing.length > 0 || extra.length > 0) {
    errors.push(
      `Redesign V1 ${rarity} reward list differs from implementations `
      + `(missing: ${missing.join(', ') || 'none'}; extra: ${extra.join(', ') || 'none'})`,
    );
  }
}

if (new Set(redesignRewardIds).size !== 68) {
  errors.push('Redesign V1 rewards must contain 68 unique cards');
}
const excludedRedesignIds = readRedesignCardIds('ExcludedSpecialCardIds');
for (const id of excludedRedesignIds) {
  if (redesignRewardIds.includes(id)) errors.push(`Redesign V1 special card is in rewards: ${id}`);
  if (!new RegExp(`class\\s+${id}\\b`).test(redesignCardSource)
      && !filesUnder(join(root, 'Cards', 'Ancients')).some(path =>
        path.endsWith('.cs') && new RegExp(`class\\s+${id}\\b`).test(readFileSync(path, 'utf8')))) {
    errors.push(`Redesign V1 special card implementation is missing: ${id}`);
  }
}

const redesignBasicSource = readFileSync(
  join(root, 'Cards', 'RedesignV1', 'RedesignV1BasicCards.cs'),
  'utf8',
);
for (const id of ['KarateStraightRedesignV1', 'TurtleShellRedesignV1']) {
  const classPattern = new RegExp(
    `class\\s+${id}\\s*:\\s*NinjaSlayerRedesignCardTemplate\\s*\\{`
      + `[\\s\\S]*?NinjaSlayerCardSpec\\s+Spec\\s*=\\s*new\\([^;]*CardRarity\\.Basic[^;]*\\);`,
  );
  if (!classPattern.test(redesignBasicSource)) {
    errors.push(`${id} must use the Redesign character pool and Basic starter-card rarity`);
  }
}
if (!/AddStartingCard<TurtleShellRedesignV1>\(1,\s*3\)/.test(entrySource)
    || /AddStartingCard<CountermeasureRedesignV1>/.test(entrySource)) {
  errors.push('Redesign V1 starting deck must replace Countermeasure with TurtleShellRedesignV1');
}
if (!redesignRewardIds.includes('CountermeasureRedesignV1')
    || !excludedRedesignIds.includes('TurtleShellRedesignV1')) {
  errors.push('Countermeasure must be a reward and Turtle Shell must remain starter-only');
}
if (!/class\s+TurtleShellRedesignV1\b[\s\S]*?base\(Spec,\s*"BlockCard"\)/.test(redesignBasicSource)) {
  errors.push('TurtleShellRedesignV1 must reuse BlockCard.png');
}
if (!existsSync(join(root, 'NinjaSlayer', 'images', 'cards', 'BlockCard.png'))) {
  errors.push('TurtleShellRedesignV1 portrait BlockCard.png is missing');
}

const tokenPoolRedesignIds = [...redesignCardSource.matchAll(
  /\[RegisterCard\(typeof\(TokenCardPool\)\)\]\s*public\s+sealed\s+class\s+(\w+)/g,
)].map(match => match[1]).sort();
const expectedColorlessRedesignIds = [
  'ChadoEnergyRedesignV1',
  'IyaEchoRedesignV1',
  'PunchRedesignV1',
];
if (tokenPoolRedesignIds.join(',') !== expectedColorlessRedesignIds.join(',')) {
  errors.push('Only Redesign generated tokens may use the colorless card pool');
}

const collapseFistSource = readFileSync(join(root, 'Cards', 'Ancients', 'CollapseFist.cs'), 'utf8');
if (!/\[RegisterCard\(typeof\(NinjaSlayerRedesignCardPool\)\)\]\s*public\s+sealed\s+class\s+CollapseFistRedesignV1/.test(collapseFistSource)) {
  errors.push('CollapseFistRedesignV1 must use the Redesign character pool');
}

const redesignArtOwners = new Map();
for (const id of redesignRewardIds) {
  const artMatch = new RegExp(
    `:\\s*base\\(nameof\\(${id}\\),\\s*(?:nameof\\((\\w+)\\)|"([^"]+)")`,
  ).exec(redesignCardSource);
  if (!artMatch) {
    errors.push(`Unable to resolve Redesign V1 card art: ${id}`);
    continue;
  }

  const artName = artMatch[1] ?? artMatch[2];
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

const shurikenStockPowerSource = readFileSync(join(root, 'Powers', 'ShurikenStockPower.cs'), 'utf8');
if (!/override\s+async\s+Task\s+AfterShuffle\b/.test(shurikenStockPowerSource)
    || /override\s+async\s+Task\s+AfterCardPlayed\b/.test(shurikenStockPowerSource)) {
  errors.push('Shuriken Stock must trigger on shuffle instead of after Attack cards');
}
const redesignPowerSource = readFileSync(join(root, 'Powers', 'RedesignV1Powers.cs'), 'utf8');
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
