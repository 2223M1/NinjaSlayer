import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const [command, ...rawArgs] = process.argv.slice(2);
const args = parseArgs(rawArgs);

if (command === 'generate') {
  const source = required(args, 'source');
  const destination = required(args, 'destination');
  const manifest = readObject(source);
  applyExpectedValues(manifest, args);
  fs.mkdirSync(path.dirname(path.resolve(destination)), { recursive: true });
  fs.writeFileSync(destination, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  console.log(`Generated ${destination}`);
} else if (command === 'validate-manifest') {
  const manifestPath = required(args, 'manifest');
  const manifest = readObject(manifestPath);
  validateExpectedValues(manifest, args, manifestPath);
  console.log(`Validated ${manifestPath}`);
} else {
  throw new Error(
    'Usage: package-contract.mjs generate --source <file> --destination <file> --version <version> ' +
    '--min-game-version <version> --ritsulib-version <version> | validate-manifest --manifest <file> ...');
}

function parseArgs(values) {
  const parsed = new Map();
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index];
    const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined || value.startsWith('--')) {
      throw new Error(`Invalid argument near '${key ?? '<end>'}'.`);
    }
    const name = key.slice(2);
    if (parsed.has(name)) throw new Error(`Duplicate argument --${name}.`);
    parsed.set(name, value);
  }
  return parsed;
}

function required(values, name) {
  const value = values.get(name);
  if (!value) throw new Error(`Missing --${name}.`);
  return value;
}

function readObject(file) {
  const value = JSON.parse(fs.readFileSync(file, 'utf8'));
  if (!value || Array.isArray(value) || typeof value !== 'object') {
    throw new Error(`Manifest is not a JSON object: ${file}`);
  }
  return value;
}

function applyExpectedValues(manifest, values) {
  manifest.version = required(values, 'version');
  manifest.min_game_version = required(values, 'min-game-version');
  resolveRitsuLibDependency(manifest).min_version = required(values, 'ritsulib-version');
}

function validateExpectedValues(manifest, values, file) {
  const expected = new Map([
    ['version', required(values, 'version')],
    ['min_game_version', required(values, 'min-game-version')],
  ]);
  for (const [key, expectedValue] of expected) {
    if (manifest[key] !== expectedValue) {
      throw new Error(`${file}: ${key} is '${manifest[key] ?? ''}', expected '${expectedValue}'.`);
    }
  }
  const expectedRitsuLib = required(values, 'ritsulib-version');
  const actualRitsuLib = resolveRitsuLibDependency(manifest).min_version;
  if (actualRitsuLib !== expectedRitsuLib) {
    throw new Error(
      `${file}: STS2-RitsuLib min_version is '${actualRitsuLib ?? ''}', expected '${expectedRitsuLib}'.`);
  }
}

function resolveRitsuLibDependency(manifest) {
  if (!Array.isArray(manifest.dependencies)) {
    throw new Error('Manifest has no dependencies array.');
  }
  const matches = manifest.dependencies.filter(
    dependency => dependency && dependency.id === 'STS2-RitsuLib');
  if (matches.length !== 1) {
    throw new Error('Manifest must contain exactly one STS2-RitsuLib dependency.');
  }
  return matches[0];
}
