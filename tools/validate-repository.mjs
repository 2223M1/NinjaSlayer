import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';

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

for (const path of filesUnder(join(root, 'NinjaSlayer', 'localization')).filter((path) => path.endsWith('.json'))) {
  readJson(path);
}
readJson(join(root, 'NinjaSlayer.json'));

for (const name of ['feedback.json', 'enchantments.json']) {
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

const patchSources = filesUnder(join(root, 'Code', 'Patches'))
  .filter((path) => path.endsWith('.cs'))
  .map((path) => readFileSync(path, 'utf8'))
  .join('\n');
const entrySource = readFileSync(join(root, 'Scripts', 'Entry.cs'), 'utf8');
const characterSource = readFileSync(join(root, 'Content', 'NinjaSlayerCharacter.cs'), 'utf8');
const patchGroupSource = readFileSync(join(root, 'Code', 'Patches', 'NinjaSlayerPatchGroups.cs'), 'utf8');

for (const visualClass of ['MerchantVisuals', 'RestSiteVisuals']) {
  const classStart = characterSource.indexOf(`class ${visualClass}`);
  const bodyStyleStart = characterSource.indexOf('BodyStyle()', classStart);
  const bodyStyleEnd = characterSource.indexOf(';', bodyStyleStart);
  const bodyStyle = characterSource.slice(bodyStyleStart, bodyStyleEnd);
  if (!bodyStyle.includes('.WithPosition(') || bodyStyle.includes('.WithOffset(')) {
    errors.push(`${visualClass}.BodyStyle must use idempotent absolute positioning`);
  }
}
const patchClasses = [...patchSources.matchAll(/(?:public|internal)\s+sealed\s+class\s+(\w+)\s*:\s*IPatchMethod/g)]
  .map((match) => match[1]);
const patchRegistrations = [...patchGroupSource.matchAll(/RegisterPatch<(\w+)>/g)]
  .map((match) => match[1]);
for (const patchClass of patchClasses) {
  const count = patchRegistrations.filter((registered) => registered === patchClass).length;
  if (count !== 1) errors.push(`${patchClass} must appear in exactly one typed patch group (found ${count})`);
}
for (const registered of patchRegistrations) {
  if (!patchClasses.includes(registered)) errors.push(`Typed patch group references unknown patch ${registered}`);
}

const patchDeclarations = [...patchSources.matchAll(/(?:public|internal)\s+sealed\s+class\s+(\w+)\s*:\s*IPatchMethod/g)];
const patchBodies = new Map(patchDeclarations.map((match, index) => [
  match[1],
  patchSources.slice(match.index, patchDeclarations[index + 1]?.index ?? patchSources.length),
]));
for (const groupName of [
  'CardResolutionPatchGroup',
  'PreparedPatchGroup',
  'FinisherCorePatchGroup',
  'TransitionCorePatchGroup',
]) {
  const groupStart = patchGroupSource.indexOf(`class ${groupName}`);
  const groupEnd = patchGroupSource.indexOf('\ninternal sealed class ', groupStart + 1);
  const groupBody = patchGroupSource.slice(groupStart, groupEnd < 0 ? undefined : groupEnd);
  for (const match of groupBody.matchAll(/RegisterPatch<(\w+)>/g)) {
    if (!patchBodies.get(match[1])?.includes('IsCritical => true')) {
      errors.push(`${groupName} contains non-critical required patch ${match[1]}`);
    }
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

const patchGroups = [...patchGroupSource.matchAll(/sealed\s+class\s+(\w+PatchGroup)\s*:\s*IModPatches/g)]
  .map((match) => match[1]);
for (const patchGroup of patchGroups) {
  if (!entrySource.includes(`InstallCapability<${patchGroup}>`)) {
    errors.push(`Entry.cs does not install typed patch group ${patchGroup}`);
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
for (const icon of ['OpeningPower.png', 'soar_power.png']) {
  if (!assetManifest.includes(icon)) errors.push(`ASSET_MANIFEST.md is missing power icon ${icon}`);
}

if (errors.length > 0) {
  console.error(errors.map((error) => `- ${error}`).join('\n'));
  process.exit(1);
}

console.log('Repository consistency checks passed.');
