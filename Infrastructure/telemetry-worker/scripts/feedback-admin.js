import { mkdirSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import {
  feedbackIndexKey,
  feedbackTombstoneKey,
  parseFeedbackIndexMarker,
  validateCompletedFeedbackMetadata,
} from '../src/feedback-storage.js';

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

function indexEntries() {
  return listKeys('feedback-index/');
}

function findIndexKey(submissionId) {
  if (!uuidPattern.test(submissionId)) throw new Error('The submission ID must be a UUID.');
  const expected = feedbackIndexKey(submissionId);
  const matches = indexEntries().filter((entry) => entry.name === expected);
  if (matches.length !== 1) throw new Error(`Expected one feedback entry for ${submissionId}, found ${matches.length}.`);
  return matches[0].name;
}

function readCompletedFeedback(indexKey, expectedSubmissionId = null) {
  const marker = parseFeedbackIndexMarker(readText(indexKey), expectedSubmissionId);
  if (!marker || marker.state !== 'completed') throw new Error(`${indexKey} is not a valid completed submission.`);
  const metadataText = readText(marker.completion.metadataKey);
  const metadataSha256 = createHash('sha256').update(metadataText, 'utf8').digest('hex');
  if (metadataSha256 !== marker.completion.metadataSha256) {
    throw new Error(`${indexKey} metadata hash does not match its completion marker.`);
  }
  const metadata = JSON.parse(metadataText);
  if (!validateCompletedFeedbackMetadata(metadata, marker)) {
    throw new Error(`${indexKey} metadata does not belong to its completed attempt.`);
  }
  return { marker, metadata, metadataText };
}

function listFeedback() {
  const entries = indexEntries();
  if (entries.length === 0) {
    console.log('No feedback is currently stored.');
    return;
  }
  for (const entry of entries) {
    const marker = parseFeedbackIndexMarker(readText(entry.name));
    if (!marker) {
      console.error(`Skipped ${entry.name}: unsupported or invalid index marker.`);
      continue;
    }
    if (marker.state !== 'completed') continue;
    try {
      const { metadata } = readCompletedFeedback(entry.name, marker.submissionId);
      const description = metadata.payload?.description?.replace(/\s+/g, ' ').slice(0, 100) ?? '';
      console.log(`${metadata.modContext.submissionId}  ${metadata.receivedAtUtc}  ${metadata.payload.category}  ${description}`);
    } catch (error) {
      console.error(`Skipped ${entry.name}: ${error.message}`);
    }
  }
}

function downloadFeedback(submissionId) {
  const indexKey = findIndexKey(submissionId);
  const { metadata, metadataText } = readCompletedFeedback(indexKey, submissionId);
  const outputDir = join(root, 'feedback-downloads', submissionId);
  mkdirSync(outputDir, { recursive: true });
  writeFileSync(join(outputDir, 'metadata.json'), metadataText);
  writeFileSync(join(outputDir, 'screenshot.png'), readBinary(metadata.storage.screenshot.key));
  const chunks = metadata.storage.logs.chunks.map((key) => readBinary(key));
  writeFileSync(join(outputDir, 'logs.zip'), Buffer.concat(chunks));
  console.log(outputDir);
}

function deleteFeedback(submissionId) {
  const indexKey = findIndexKey(submissionId);
  const { marker, metadata } = readCompletedFeedback(indexKey, submissionId);
  const keys = [
    metadata.storage.screenshot.key,
    ...metadata.storage.logs.chunks,
    marker.completion.metadataKey,
    indexKey,
  ];
  runWrangler([
    'kv', 'key', 'put', feedbackTombstoneKey(submissionId),
    JSON.stringify({ schemaVersion: 1, submissionId, deletedAtUtc: new Date().toISOString() }),
    ...bindingArgs,
    '--ttl', String(180 * 24 * 60 * 60),
  ]);
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
