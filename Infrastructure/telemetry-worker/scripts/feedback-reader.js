import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { feedbackIndexKey, parseFeedbackIndexMarker, validateCompletedFeedbackMetadata } from '../src/feedback-storage.js';

const exec = promisify(execFile);
const root = fileURLToPath(new URL('../', import.meta.url));
const wrangler = fileURLToPath(new URL('../node_modules/wrangler/bin/wrangler.js', import.meta.url));
const binding = ['--binding', 'FEEDBACK_KV', '--remote'];

async function command(args) {
  const { stdout } = await exec(process.execPath, [wrangler, ...args], {
    cwd: root, encoding: 'buffer', maxBuffer: 70 * 1024 * 1024, timeout: 90000,
    env: { ...process.env, CI: 'true', WRANGLER_SEND_METRICS: 'false' },
  });
  return stdout;
}

export async function readFeedbackObject(key) {
  return command(['kv', 'key', 'get', key, ...binding]);
}

export async function listFeedbackKeys() {
  return JSON.parse((await command(['kv', 'key', 'list', ...binding, '--prefix', 'feedback-index/'])).toString('utf8'));
}

export function verifyFeedbackMetadata(marker, bytes) {
  if (createHash('sha256').update(bytes).digest('hex') !== marker.completion.metadataSha256)
    throw new Error('反馈元数据与完成标记的校验值不一致。');
  const metadata = JSON.parse(bytes.toString('utf8'));
  if (!validateCompletedFeedbackMetadata(metadata, marker)) throw new Error('反馈附件不属于已完成的提交。');
  return metadata;
}

export async function readCompletedFeedback(id) {
  const marker = parseFeedbackIndexMarker((await readFeedbackObject(feedbackIndexKey(id))).toString('utf8'), id);
  if (!marker || marker.state !== 'completed') throw new Error('此反馈尚未完成，或已过期。');
  return verifyFeedbackMetadata(marker, await readFeedbackObject(marker.completion.metadataKey));
}

export async function loadFeedback() {
  const feedback = [], warnings = [];
  for (const entry of await listFeedbackKeys()) {
    try {
      const marker = parseFeedbackIndexMarker((await readFeedbackObject(entry.name)).toString('utf8'));
      if (marker?.state === 'writing') continue;
      if (!marker) throw new Error('无效的完成标记');
      const metadata = verifyFeedbackMetadata(marker, await readFeedbackObject(marker.completion.metadataKey));
      feedback.push({ id: marker.submissionId, at: metadata.receivedAtUtc, ...metadata.payload, context: metadata.modContext });
    } catch (error) { warnings.push(`${entry.name}: ${error.message}`); }
  }
  return { feedback: feedback.sort((a, b) => b.at.localeCompare(a.at)), warnings };
}

export async function loadRemoteFeedback(token) {
  const feedback = [], warnings = [];
  let cursor;
  do {
    const response = await fetch(`https://ninja-slayer-telemetry.theonetrue2223.workers.dev/observatory/feedback${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`, {
      headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(90_000),
    });
    if (!response.ok) throw new Error(`反馈读取失败（HTTP ${response.status}）。`);
    const page = await response.json();
    feedback.push(...page.feedback); warnings.push(...page.warnings); cursor = page.cursor;
  } while (cursor);
  return { feedback: feedback.sort((a, b) => b.at.localeCompare(a.at)), warnings };
}
