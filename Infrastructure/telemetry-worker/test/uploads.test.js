import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { Miniflare } from 'miniflare';
import { parseFeedbackIndexMarker } from '../src/feedback-storage.js';
import { publicFeedback } from '../dashboard/publish.mjs';

// Exported by the product DLL and real RitsuLib adapter in VerifyUploadTransport.
const fixturePath = process.env.NINJASLAYER_UPLOAD_FIXTURE
  ?? new URL('fixtures/dotnet-uploads.json', import.meta.url);
const wire = JSON.parse(await readFile(fixturePath, 'utf8'));

test('actual .NET uploads survive Worker parsing, durable storage, retries and native batch routing', async () => {
  const upstream = [];
  const mf = new Miniflare({
    modules: true, scriptPath: fileURLToPath(new URL('../src/index.js', import.meta.url)),
    modulesRules: [{ type: 'ESModule', include: ['**/*.js'] }],
    compatibilityDate: '2026-07-15',
    bindings: { POSTHOG_API_KEY: 'local-test', RATE_LIMIT_SALT: 'local-contract-salt-only' },
    kvNamespaces: ['FEEDBACK_KV'],
    durableObjects: {
      FEEDBACK_SUBMISSIONS: { className: 'FeedbackSubmissionCoordinator', useSQLite: true },
      ANONYMOUS_QUOTAS: { className: 'AnonymousQuotaGuard', useSQLite: true },
    },
    ratelimits: {
      FEEDBACK_RATE_LIMITER: { namespace_id: '1002', simple: { limit: 30, period: 60 } },
      TELEMETRY_RATE_LIMITER: { namespace_id: '1001', simple: { limit: 30, period: 60 } },
    },
    outboundService: async request => {
      assert.equal(request.url, 'https://us.i.posthog.com/batch/');
      upstream.push(await request.json());
      return new Response('{}', { status: 200 });
    },
  });
  const send = (record, body = Buffer.from(record.body, 'base64')) => mf.dispatchFetch(
    `https://worker.test${record.path}`, { method: record.method,
      headers: { 'Content-Type': record.contentType, 'CF-Connecting-IP': '203.0.113.8',
        ...(record.submissionId ? { 'X-NinjaSlayer-Submission-Id': record.submissionId } : {}) }, body });
  try {
    const [first, retry, telemetry] = wire;
    // Keep the formerly failing .NET headers as a negative control for the workerd parser.
    const oldHeaders = Buffer.from(Buffer.from(first.body, 'base64').toString('latin1')
      .replaceAll(/name="([^"]+)"/g, 'name=$1'), 'latin1');
    assert.equal((await send(first, oldHeaders)).status, 400);
    const response = await send(first);
    assert.equal(response.status, 200, await response.clone().text());
    assert.equal((await response.json()).id, first.submissionId);
    const repeated = await send(retry);
    assert.equal(repeated.status, 200);
    assert.equal((await repeated.json()).idempotent, true);
    const kv = await mf.getKVNamespace('FEEDBACK_KV');
    const marker = parseFeedbackIndexMarker(await kv.get(`feedback-index/${first.submissionId}`));
    assert.equal(marker.state, 'completed');
    const text = await kv.get(marker.completion.metadataKey);
    assert.equal(createHash('sha256').update(text).digest('hex'), marker.completion.metadataSha256);
    const metadata = JSON.parse(text);
    assert.equal(metadata.payload.description, '本地反馈契约：中文与附件');
    assert.deepEqual(Buffer.from(await kv.get(metadata.storage.screenshot.key, 'arrayBuffer')),
      Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
    const zip = Buffer.alloc(22); zip.set([80, 75, 5, 6]);
    assert.deepEqual(Buffer.from(await kv.get(metadata.storage.logs.chunks[0], 'arrayBuffer')), zip);
    assert.deepEqual(publicFeedback([{ ...metadata.payload, context: metadata.modContext }]), []);
    assert.equal(telemetry.path, '/batch/');
    assert.equal((await send(telemetry)).status, 200);
    assert.equal(upstream.length, 1);
    assert.equal(upstream[0].batch[0].properties.request_id, 'balance_runs');
  } finally { await mf.dispose(); }
});
