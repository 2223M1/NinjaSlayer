import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const wrangler = join(root, 'node_modules', 'wrangler', 'bin', 'wrangler.js');
const bindingArgs = ['--binding', 'FEEDBACK_KV', '--remote'];
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function runWrangler(args, encoding = 'utf8') {
  const result = spawnSync(process.execPath, [wrangler, ...args], {
    cwd: root,
    encoding,
    maxBuffer: 70 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'inherit'],
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
  return result.stdout;
}

function listKeys(prefix = 'feedback/') {
  const output = runWrangler(['kv', 'key', 'list', ...bindingArgs, '--prefix', prefix]);
  return JSON.parse(output);
}

function readText(key) {
  return runWrangler(['kv', 'key', 'get', key, ...bindingArgs, '--text']);
}

function readBinary(key) {
  return runWrangler(['kv', 'key', 'get', key, ...bindingArgs], null);
}

function metadataEntries() {
  return listKeys().filter((entry) => entry.name.endsWith('/metadata.json'));
}

function findMetadataKey(submissionId) {
  if (!uuidPattern.test(submissionId)) throw new Error('The submission ID must be a UUID.');
  const matches = metadataEntries().filter((entry) => entry.name.includes(submissionId));
  if (matches.length !== 1) throw new Error(`Expected one feedback entry for ${submissionId}, found ${matches.length}.`);
  return matches[0].name;
}

function listFeedback() {
  const entries = metadataEntries();
  if (entries.length === 0) {
    console.log('No feedback is currently stored.');
    return;
  }
  for (const entry of entries) {
    const metadata = JSON.parse(readText(entry.name));
    const description = metadata.payload?.description?.replace(/\s+/g, ' ').slice(0, 100) ?? '';
    console.log(`${metadata.modContext.submissionId}  ${metadata.modContext.submittedAtUtc}  ${metadata.payload.category}  ${description}`);
  }
}

function downloadFeedback(submissionId) {
  const metadataKey = findMetadataKey(submissionId);
  const metadataText = readText(metadataKey);
  const metadata = JSON.parse(metadataText);
  const outputDir = join(root, 'feedback-downloads', submissionId);
  mkdirSync(outputDir, { recursive: true });
  writeFileSync(join(outputDir, 'metadata.json'), metadataText);
  writeFileSync(join(outputDir, 'screenshot.png'), readBinary(metadata.storage.screenshot.key));
  const chunks = metadata.storage.logs.chunks.map((key) => readBinary(key));
  writeFileSync(join(outputDir, 'logs.zip'), Buffer.concat(chunks));
  console.log(outputDir);
}

function deleteFeedback(submissionId) {
  const metadataKey = findMetadataKey(submissionId);
  const metadata = JSON.parse(readText(metadataKey));
  const keys = [metadata.storage.screenshot.key, ...metadata.storage.logs.chunks, metadataKey];
  for (const key of keys) runWrangler(['kv', 'key', 'delete', key, ...bindingArgs]);
  console.log(`Deleted ${submissionId}.`);
}

const [command = 'list', submissionId] = process.argv.slice(2);
try {
  if (command === 'list') listFeedback();
  else if (command === 'download' && submissionId) downloadFeedback(submissionId);
  else if (command === 'delete' && submissionId) deleteFeedback(submissionId);
  else throw new Error('Usage: npm run feedback -- list|download <UUID>|delete <UUID>');
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
