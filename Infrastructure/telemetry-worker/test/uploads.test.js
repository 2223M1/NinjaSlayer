import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { gzipSync, gunzipSync } from 'node:zlib';
import { Miniflare } from 'miniflare';
import { parseFeedbackIndexMarker } from '../src/feedback-storage.js';
import { publicFeedback } from '../dashboard/publish.mjs';
import { publicRunId } from '../src/replays.js';

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
    r2Buckets: ['RECORDS_BUCKET'],
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
        ...(record.contentEncoding ? { 'Content-Encoding': record.contentEncoding } : {}),
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
    const r2 = await mf.getR2Bucket('RECORDS_BUCKET');
    const marker = parseFeedbackIndexMarker(await kv.get(`feedback-index/${first.submissionId}`));
    assert.equal(marker.state, 'completed');
    const text = await kv.get(marker.completion.metadataKey);
    assert.equal(createHash('sha256').update(text).digest('hex'), marker.completion.metadataSha256);
    const metadata = JSON.parse(text);
    assert.equal(metadata.payload.description, '本地反馈契约：中文与附件');
    assert.equal(metadata.storage.provider, 'r2');
    assert.equal(await kv.get(metadata.storage.screenshot.key), null);
    assert.deepEqual(Buffer.from(await (await r2.get(metadata.storage.screenshot.key)).arrayBuffer()),
      Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
    const zip = Buffer.alloc(22); zip.set([80, 75, 5, 6]);
    assert.deepEqual(Buffer.from(await (await r2.get(metadata.storage.logs.chunks[0])).arrayBuffer()), zip);
    assert.deepEqual(publicFeedback([{ ...metadata.payload, context: metadata.modContext }]), []);
    const replayId = 'a'.repeat(64);
    const replayText = JSON.stringify({ id: replayId, frames: [], expires: '2099-01-01T00:00:00.000Z' });
    await r2.put(`replays/${replayId}/0`, gzipSync(replayText), {
      httpMetadata: { contentType: 'application/json', contentEncoding: 'gzip' },
      customMetadata: { expires: '2099-01-01T00:00:00.000Z', hash: 'b'.repeat(64) },
    });
    // Use the HTTP listener so workerd's Content-Encoding behavior is exercised, too.
    const replayUrl = new URL(`/observatory/replays/${replayId}/0`, await mf.ready);
    for (let read = 0; read < 2; read++) {
      const replayResponse = await fetch(replayUrl);
      assert.equal(replayResponse.status, 200);
      assert.equal(await replayResponse.text(), replayText);
    }
    assert.equal(telemetry.path, '/batch/');
    assert.equal((await send(telemetry)).status, 200);
    assert.equal(upstream.length, 1);
    assert.equal(upstream[0].batch[0].properties.request_id, 'balance_runs');

    assert.equal(wire.length, 9, 'Regenerate the candidate DLL upload fixture');
    for (const record of wire.slice(3)) {
      assert.equal(record.contentEncoding, 'gzip');
      const decoded = JSON.parse(gunzipSync(Buffer.from(record.body, 'base64')));
      assert.equal(decoded.batch.length, 1);
      const result = await send(record);
      assert.equal(result.status, 200, await result.clone().text());
    }
    assert.equal(upstream.length, 5); // Two report attempts only go to R2.
    assert.equal(upstream[1].batch[0].uuid, upstream[4].batch[0].uuid);
    assert.equal(upstream[2].batch[0].uuid, upstream[3].batch[0].uuid);
    assert.equal(upstream[1].batch[0].properties.payload.applicant_payload.measurements.length, 2000);
    const reportId = await publicRunId('a'.repeat(64), 'local-contract-salt-only');
    const stored = await r2.get(`replays/${reportId}/0`);
    assert(stored);
    const publicReport = await mf.dispatchFetch(`https://worker.test/observatory/replays/${reportId}/0`);
    assert.equal(publicReport.status, 200);
    assert.equal((await publicReport.json()).frames.length, 2);
    assert.equal(await kv.get(`replay-index/${reportId}/0`), '1');

    // Optional real local AutoSlay capture: measure it without committing its
    // private payload or ever sending it to production PostHog/R2.
    if (process.env.NINJASLAYER_REAL_TELEMETRY_DIR) {
      const directory = process.env.NINJASLAYER_REAL_TELEMETRY_DIR;
      for (const suffix of ['telemetry', 'telemetry.replay']) {
        const events = JSON.parse(await readFile(`${directory}/checkpoints.${suffix}.json`, 'utf8'));
        for (const event of events) {
          const json = Buffer.from(JSON.stringify({ api_key: 'proxy', batch: [event] }));
          const compressed = gzipSync(json);
          const response = await send({ ...telemetry, contentEncoding: 'gzip' }, compressed);
          assert.equal(response.status, 200, await response.clone().text());
          if (event.event === 'run_history.completed')
            assert.deepEqual(upstream.at(-1).batch[0].properties, event.properties);
          console.log(`${suffix}: ${json.byteLength} JSON bytes -> ${compressed.byteLength} gzip bytes; local Worker accepted`);
        }
      }
    }
  } finally { await mf.dispose(); }
});
