import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { feedbackIndexKey, parseFeedbackIndexMarker, validateCompletedFeedbackMetadata } from '../src/feedback-storage.js';
import { feedbackReviewKey, readFeedbackReview, validateFeedbackReview, feedbackExpiresAt } from '../src/feedback-review.js';

const exec = promisify(execFile);
const root = fileURLToPath(new URL('../', import.meta.url));
const wrangler = fileURLToPath(new URL('../node_modules/wrangler/bin/wrangler.js', import.meta.url));
export const RECORDS_BUCKET = 'ninja-slayer-records';

async function command(args) {
  const { stdout } = await exec(process.execPath, [wrangler, ...args], {
    cwd: root, encoding: 'buffer', maxBuffer: 70 * 1024 * 1024, timeout: 90000,
    env: { ...process.env, CI: 'true', WRANGLER_SEND_METRICS: 'false' },
  });
  return stdout;
}

// Wrangler owns credential storage and OAuth renewal; credentials never reach the browser.
// One operation reuses one authenticated HTTP connection instead of spawning a CLI per KV key.
async function feedbackStore() {
  const auth = JSON.parse((await command(['auth', 'token', '--json'])).toString('utf8'));
  const headers = auth.type === 'api_key'
    ? { 'X-Auth-Key': auth.key, 'X-Auth-Email': auth.email }
    : { Authorization: `Bearer ${auth.token}` };
  if (!['api_key', 'api_token', 'oauth'].includes(auth.type)) throw new Error('不支持的 Cloudflare 凭据类型。');
  const config = JSON.parse(await readFile(new URL('../wrangler.jsonc', import.meta.url), 'utf8'));
  const namespace = config.kv_namespaces.find(item => item.binding === 'FEEDBACK_KV')?.id;
  if (!namespace) throw new Error('未配置 FEEDBACK_KV。');
  async function request(path, options = {}, optional = false, timeout = 30000) {
    let response;
    for (let attempt = 0; ; attempt++) {
      try {
        response = await fetch(`https://api.cloudflare.com/client/v4${path}`, {
          ...options, headers: { ...headers, ...options.headers }, redirect: 'error', signal: AbortSignal.timeout(timeout),
        });
        break;
      } catch (error) {
        // A failed read is safe to repeat; an unacknowledged write must remain visible to the author.
        if (attempt || (options.method ?? 'GET') !== 'GET')
          throw new Error(`Cloudflare 连接失败（${error.cause?.code ?? error.name}），请重试。`);
        await new Promise(resolve => setTimeout(resolve, 250));
      }
    }
    if (optional && response.status === 404) { await response.arrayBuffer(); return null; }
    if (!response.ok) { await response.arrayBuffer(); throw new Error(`Cloudflare 请求失败（HTTP ${response.status}）。`); }
    return response;
  }
  let account = process.env.CLOUDFLARE_ACCOUNT_ID || config.account_id;
  if (!account) {
    const accounts = await (await request('/accounts')).json();
    if (!accounts.success || accounts.result.length !== 1)
      throw new Error('请通过 CLOUDFLARE_ACCOUNT_ID 指定反馈所属的 Cloudflare 账户。');
    account = accounts.result[0].id;
  }
  const base = `/accounts/${encodeURIComponent(account)}/storage/kv/namespaces/${encodeURIComponent(namespace)}`;
  return {
    async get(key, optional = false) {
      const response = await request(`${base}/values/${encodeURIComponent(key)}`, {}, optional);
      return response ? Buffer.from(await response.arrayBuffer()) : null;
    },
    async list(prefix) {
      const keys = []; let cursor;
      do {
        const query = new URLSearchParams({ prefix, limit: '1000', ...(cursor ? { cursor } : {}) });
        const page = await (await request(`${base}/keys?${query}`)).json();
        if (!page.success) throw new Error('Cloudflare 来信索引读取失败。');
        keys.push(...page.result); cursor = page.result_info?.cursor;
      } while (cursor);
      return keys;
    },
    async put(key, value, expiration) {
      const result = await (await request(`${base}/values/${encodeURIComponent(key)}?expiration=${expiration}`, {
        method: 'PUT', headers: { 'Content-Type': 'text/plain' }, body: JSON.stringify(value),
      })).json();
      if (!result.success) throw new Error('Cloudflare 未确认保存成功。');
    },
    async object(key) {
      const response = await request(`/accounts/${encodeURIComponent(account)}/r2/buckets/${RECORDS_BUCKET}/objects/${key.split('/').map(encodeURIComponent).join('/')}`, {}, false, 90000);
      return Buffer.from(await response.arrayBuffer());
    },
  };
}

export async function readFeedbackObject(key) {
  return (await feedbackStore()).object(key);
}

export async function listFeedbackKeys() {
  return (await feedbackStore()).list('feedback-index/');
}

export function verifyFeedbackMetadata(marker, bytes) {
  if (createHash('sha256').update(bytes).digest('hex') !== marker.completion.metadataSha256)
    throw new Error('反馈元数据与完成标记的校验值不一致。');
  const metadata = JSON.parse(bytes.toString('utf8'));
  if (!validateCompletedFeedbackMetadata(metadata, marker)) throw new Error('反馈附件不属于已完成的提交。');
  return metadata;
}

export async function readCompletedFeedback(id, store = null) {
  store ??= await feedbackStore();
  const marker = parseFeedbackIndexMarker((await store.get(feedbackIndexKey(id))).toString('utf8'), id);
  if (!marker || marker.state !== 'completed') throw new Error('此反馈尚未完成，或已过期。');
  const metadata = verifyFeedbackMetadata(marker, await store.get(marker.completion.metadataKey));
  feedbackExpiresAt(metadata);
  return metadata;
}

export async function loadFeedbackDetails(id, store = null) {
  store ??= await feedbackStore();
  const [metadata, bytes] = await Promise.all([
    readCompletedFeedback(id, store), store.get(feedbackReviewKey(id), true),
  ]);
  return { metadata, review: readFeedbackReview(bytes?.toString('utf8') ?? null) };
}

export async function loadFeedbackReview(id) {
  return (await loadFeedbackDetails(id)).review;
}

export async function saveFeedbackReview(id, value, store = null) {
  const review = { schemaVersion: 1, ...validateFeedbackReview(value), updatedAt: new Date().toISOString() };
  store ??= await feedbackStore();
  // Fresh completion and prior-review validation remain mandatory before writing.
  const { metadata } = await loadFeedbackDetails(id, store);
  await store.put(feedbackReviewKey(id), review, feedbackExpiresAt(metadata));
  return readFeedbackReview(review);
}

export async function loadFeedback() {
  const feedback = [], warnings = [], store = await feedbackStore();
  const entries = await store.list('feedback-index/');
  // Real inbox loading was serial and exceeded 100 seconds for ten letters. Bound remote reads to four letters.
  for (let offset = 0; offset < entries.length; offset += 4) {
    await Promise.all(entries.slice(offset, offset + 4).map(async entry => {
      try {
        const marker = parseFeedbackIndexMarker((await store.get(entry.name)).toString('utf8'));
        if (marker?.state === 'writing') return;
        if (!marker) throw new Error('无效的完成标记');
        const [bytes, reviewBytes] = await Promise.all([
          store.get(marker.completion.metadataKey), store.get(feedbackReviewKey(marker.submissionId), true),
        ]);
        const metadata = verifyFeedbackMetadata(marker, bytes);
        feedbackExpiresAt(metadata);
        const review = readFeedbackReview(reviewBytes?.toString('utf8') ?? null);
        feedback.push({ id: marker.submissionId, at: metadata.receivedAtUtc, ...metadata.payload, context: metadata.modContext, review });
      } catch (error) { warnings.push(`${entry.name}: ${error.message}`); }
    }));
  }
  return { feedback: feedback.sort((a, b) => b.at.localeCompare(a.at)), warnings };
}

export async function loadRemoteFeedback(token) {
  const feedback = [], warnings = [];
  let cursor;
  do {
    const response = await fetch(`https://telemetry.feixingwawa.cn/observatory/feedback${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`, {
      headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(90_000),
    });
    if (!response.ok) throw new Error(`反馈读取失败（HTTP ${response.status}）。`);
    const page = await response.json();
    feedback.push(...page.feedback); warnings.push(...page.warnings); cursor = page.cursor;
  } while (cursor);
  return { feedback: feedback.sort((a, b) => b.at.localeCompare(a.at)), warnings };
}
