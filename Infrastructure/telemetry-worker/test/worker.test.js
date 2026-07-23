import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
  AnonymousQuotaGuard,
  FeedbackSubmissionCoordinator,
  handleRequest,
} from '../src/index.js';
import {
  parseFeedbackIndexMarker,
  validateCompletedFeedbackMetadata,
} from '../src/feedback-storage.js';

const UUID = '9b3d6f32-f6d4-4ca4-9a34-128763c3154b';
const SECOND_UUID = 'c4b7a218-e75c-4c4f-8df4-41bb741b44e2';
const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const ZIP = new Uint8Array([0x50, 0x4b, 0x03, 0x04]);
const RITSU_FIXTURE = JSON.parse(readFileSync(
  new URL('./fixtures/ritsulib-0.4.62-run-history.json', import.meta.url),
  'utf8',
));

class MockStorage {
  objects = new Map();
  alarm = null;
  async get(key) { return this.objects.get(key); }
  async put(key, value) { this.objects.set(key, structuredClone(value)); }
  async delete(key) { this.objects.delete(key); }
  async deleteAll() { this.objects.clear(); }
  async setAlarm(value) { this.alarm = value; }
}

class MockKv {
  objects = new Map();
  options = new Map();
  writes = [];
  writeRecords = [];
  failNextSuffix = null;
  failNextKey = null;
  blockedPut = null;

  async get(key) { return this.objects.get(key) ?? null; }
  async put(key, value, options) {
    if (this.blockedPut && key.endsWith(this.blockedPut.suffix)) {
      const blocked = this.blockedPut;
      this.blockedPut = null;
      blocked.startedResolve();
      await blocked.released;
    }
    if (this.failNextSuffix && key.endsWith(this.failNextSuffix)) {
      this.failNextSuffix = null;
      throw new Error('injected put failure');
    }
    if (this.failNextKey === key) {
      this.failNextKey = null;
      throw new Error('injected marker failure');
    }
    this.writes.push(key);
    this.writeRecords.push({ key, value: structuredClone(value) });
    this.objects.set(key, value);
    this.options.set(key, options);
  }
  async delete(key) {
    this.objects.delete(key);
    this.options.delete(key);
  }

  blockNextSuffix(suffix) {
    let startedResolve;
    let release;
    const started = new Promise((resolve) => { startedResolve = resolve; });
    const released = new Promise((resolve) => { release = resolve; });
    this.blockedPut = { suffix, startedResolve, released };
    return { started, release };
  }
}

class MockRateLimiter {
  constructor(limit = Number.POSITIVE_INFINITY) {
    this.limitValue = limit;
    this.calls = [];
  }
  async limit({ key }) {
    this.calls.push(key);
    return { success: this.calls.length <= this.limitValue };
  }
}

class MockDurableObjectNamespace {
  constructor(Type, env) {
    this.Type = Type;
    this.env = env;
    this.instances = new Map();
  }
  idFromName(name) { return name; }
  get(id) {
    if (!this.instances.has(id)) {
      const storage = new MockStorage();
      const instance = new this.Type({ storage }, this.env);
      this.instances.set(id, {
        instance,
        storage,
        fetch: (request) => instance.fetch(request),
      });
    }
    return this.instances.get(id);
  }
}

function workerEnv(overrides = {}) {
  const env = {
    RATE_LIMIT_SALT: 'test-only-rate-limit-salt',
    POSTHOG_API_KEY: 'posthog-secret',
    TELEMETRY_RATE_LIMITER: new MockRateLimiter(),
    FEEDBACK_RATE_LIMITER: new MockRateLimiter(),
    FEEDBACK_KV: new MockKv(),
    TEST_NOW: '2030-08-09T10:11:12.000Z',
    ...overrides,
  };
  env.ANONYMOUS_QUOTAS ??= new MockDurableObjectNamespace(AnonymousQuotaGuard, env);
  env.FEEDBACK_SUBMISSIONS ??= new MockDurableObjectNamespace(FeedbackSubmissionCoordinator, env);
  return env;
}

function feedbackRequest(overrides = {}) {
  const submissionId = overrides.submissionId ?? UUID;
  const form = new FormData();
  form.set('payload_json', JSON.stringify({
    category: overrides.category ?? 'bug',
    description: 'test feedback',
    gameVersion: 'v0.109.0',
  }));
  form.set('mod_context', JSON.stringify({
    submissionId,
    submittedAtUtc: '2026-07-15T12:34:56.000Z',
    modVersion: '0.1.0',
    ...(overrides.contextFields ?? {}),
  }));
  form.set('screenshot', new File(
    [overrides.screenshotBytes ?? PNG],
    'screenshot.png',
    { type: overrides.screenshotType ?? 'image/png' },
  ));
  form.set('logs', new File(
    [overrides.logsBytes ?? ZIP],
    'logs.zip',
    { type: overrides.logsType ?? 'application/zip' },
  ));
  const headers = { 'CF-Connecting-IP': overrides.ip ?? '203.0.113.8' };
  if (!overrides.legacy) headers['X-NinjaSlayer-Submission-Id'] = overrides.headerId ?? submissionId;
  return new Request('https://worker.test/feedback', {
    method: overrides.method ?? 'PUT',
    headers,
    body: form,
  });
}

function telemetryRequest(body = structuredClone(RITSU_FIXTURE), overrides = {}) {
  return new Request('https://worker.test/', {
    method: overrides.method ?? 'POST',
    headers: {
      'Content-Type': 'application/json',
      'CF-Connecting-IP': overrides.ip ?? '203.0.113.7',
    },
    body: JSON.stringify(body),
  });
}

async function withSuccessfulPostHog(callback) {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response('', { status: 200 });
  try { await callback(); } finally { globalThis.fetch = originalFetch; }
}

test('production endpoints fail closed when secrets or Durable Object bindings are missing', async () => {
  assert.equal((await handleRequest(telemetryRequest(), {})).status, 503);
  assert.equal((await handleRequest(feedbackRequest(), { FEEDBACK_KV: new MockKv() })).status, 503);
});

test('minute rate limiting uses a stable HMAC and never stores the IP', async () => {
  const limiter = new MockRateLimiter(1);
  const env = workerEnv({ TELEMETRY_RATE_LIMITER: limiter });
  await withSuccessfulPostHog(async () => {
    assert.equal((await handleRequest(telemetryRequest(), env)).status, 200);
    assert.equal((await handleRequest(telemetryRequest(), env)).status, 429);
  });
  assert.equal(limiter.calls.length, 2);
  assert.equal(limiter.calls[0], limiter.calls[1]);
  assert.notEqual(limiter.calls[0], '203.0.113.7');
});

test('real RitsuLib 0.4.62 envelope is accepted and camelCase or extra properties are rejected', async () => {
  await withSuccessfulPostHog(async () => {
    assert.equal((await handleRequest(telemetryRequest(), workerEnv())).status, 200);
  });

  const camelCase = structuredClone(RITSU_FIXTURE);
  camelCase.batch[0].properties.applicantId = camelCase.batch[0].properties.applicant_id;
  delete camelCase.batch[0].properties.applicant_id;
  assert.equal((await handleRequest(telemetryRequest(camelCase), workerEnv())).status, 400);

  const extra = structuredClone(RITSU_FIXTURE);
  extra.batch[0].properties.unreviewed = true;
  assert.equal((await handleRequest(telemetryRequest(extra), workerEnv())).status, 400);
});

test('telemetry rejects excessive JSON depth and streaming bodies over the limit', async () => {
  const nested = structuredClone(RITSU_FIXTURE);
  let cursor = nested.batch[0].properties.payload;
  for (let index = 0; index < 40; index += 1) cursor = cursor.next = {};
  assert.equal((await handleRequest(telemetryRequest(nested), workerEnv())).status, 400);

  const stream = new ReadableStream({
    start(controller) {
      controller.enqueue(new Uint8Array(5 * 1024 * 1024));
      controller.enqueue(new Uint8Array(1));
      controller.close();
    },
  });
  const request = new Request('https://worker.test/', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '203.0.113.7' },
    body: stream,
    duplex: 'half',
  });
  assert.equal(request.headers.has('content-length'), false);
  assert.equal((await handleRequest(request, workerEnv())).status, 413);
});

test('invalid telemetry never consumes daily quota while valid telemetry does', async () => {
  const env = workerEnv();
  const headers = {
    'Content-Type': 'application/json',
    'CF-Connecting-IP': '203.0.113.7',
  };
  const malformed = new Request('https://worker.test/', {
    method: 'POST',
    headers,
    body: '{"batch":',
  });
  assert.equal((await handleRequest(malformed, env)).status, 400);

  const unsupported = structuredClone(RITSU_FIXTURE);
  unsupported.batch[0].properties.unreviewed = 'not allowlisted';
  assert.equal((await handleRequest(telemetryRequest(unsupported), env)).status, 400);
  assert.equal(env.ANONYMOUS_QUOTAS.instances.size, 0);

  await withSuccessfulPostHog(async () => {
    assert.equal((await handleRequest(telemetryRequest(), env)).status, 200);
  });
  assert.equal(env.ANONYMOUS_QUOTAS.instances.size, 1);
  const [quotaCoordinator] = env.ANONYMOUS_QUOTAS.instances.values();
  const quota = quotaCoordinator.storage.objects.get('quota:telemetry');
  assert.equal(quota.count, 1);
  assert.equal(quota.bytes, new TextEncoder().encode(JSON.stringify(RITSU_FIXTURE)).byteLength);
});

test('telemetry forwards only validated fields and no IP header', async () => {
  const originalFetch = globalThis.fetch;
  let forwarded;
  let forwardedHeaders;
  globalThis.fetch = async (_url, init) => {
    forwarded = JSON.parse(init.body);
    forwardedHeaders = init.headers;
    return new Response('', { status: 200 });
  };
  try {
    assert.equal((await handleRequest(telemetryRequest(), workerEnv())).status, 200);
    assert.equal(forwarded.api_key, 'posthog-secret');
    assert.equal(forwarded.batch[0].properties.applicant_id, 'NinjaSlayer');
    assert.equal(forwarded.batch[0].properties.$ip, undefined);
    assert.equal(forwardedHeaders['X-Forwarded-For'], undefined);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test('feedback rejects invalid schemas, MIME types, sizes, signatures, and mismatched headers', async () => {
  assert.equal((await handleRequest(feedbackRequest({ method: 'POST' }), workerEnv())).status, 405);
  assert.equal((await handleRequest(feedbackRequest({ category: 'other' }), workerEnv())).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ submissionId: 'bad' }), workerEnv())).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ contextFields: { unexpected: true } }), workerEnv())).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ headerId: SECOND_UUID }), workerEnv())).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ screenshotType: 'image/jpeg' }), workerEnv())).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ screenshotBytes: new Uint8Array(5 * 1024 * 1024 + 1) }), workerEnv())).status, 413);
  assert.equal((await handleRequest(feedbackRequest({ screenshotBytes: new Uint8Array(8) }), workerEnv())).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ logsBytes: new Uint8Array([1, 2, 3, 4]) }), workerEnv())).status, 400);
});

test('feedback writes an attempt lease first and a hash-bound completion marker last', async () => {
  const kv = new MockKv();
  const logsBytes = new Uint8Array(4 * 1024 * 1024 + 1);
  logsBytes.set(ZIP);
  const response = await handleRequest(feedbackRequest({ logsBytes }), workerEnv({ FEEDBACK_KV: kv }));
  assert.equal(response.status, 200);
  const body = await response.json();
  assert(body.prefix.startsWith(`feedback/2030/08/2030-08-09T10-11-12-000Z-${UUID}`));
  assert.match(body.attemptId, /^[0-9a-f-]{36}$/);
  assert.equal(kv.writes[0], `feedback-index/${UUID}`);
  assert(kv.writes[1].includes(`/attempts/${body.attemptId}/screenshot.png`));
  assert(kv.writes[2].includes(`/attempts/${body.attemptId}/logs/part-0000.bin`));
  assert(kv.writes[3].includes(`/attempts/${body.attemptId}/logs/part-0001.bin`));
  assert(kv.writes[4].includes(`/attempts/${body.attemptId}/metadata.json`));
  assert.equal(kv.writes[5], `feedback-index/${UUID}`);

  const writingMarker = parseFeedbackIndexMarker(kv.writeRecords[0].value, UUID);
  const completionMarker = parseFeedbackIndexMarker(kv.objects.get(`feedback-index/${UUID}`), UUID);
  assert.equal(writingMarker.state, 'writing');
  assert.equal(completionMarker.state, 'completed');
  assert.equal(completionMarker.lease.attemptId, body.attemptId);
  const metadataText = kv.objects.get(completionMarker.completion.metadataKey);
  assert.equal(
    createHash('sha256').update(metadataText, 'utf8').digest('hex'),
    completionMarker.completion.metadataSha256,
  );
  assert(validateCompletedFeedbackMetadata(JSON.parse(metadataText), completionMarker));
  assert([...kv.options.values()].every((options) => options.expirationTtl === 180 * 24 * 60 * 60));
});

test('a concurrent retry observes the active lease without consuming a second write attempt', async () => {
  const kv = new MockKv();
  const env = workerEnv({ FEEDBACK_KV: kv });
  const barrier = kv.blockNextSuffix('/screenshot.png');
  const firstPromise = handleRequest(feedbackRequest(), env);
  await barrier.started;
  const activeMarker = parseFeedbackIndexMarker(kv.objects.get(`feedback-index/${UUID}`), UUID);
  const second = await handleRequest(feedbackRequest(), env);
  assert.equal(second.status, 503);
  const processing = await second.json();
  assert.equal(processing.processing, true);
  assert.equal(processing.attemptId, activeMarker.lease.attemptId);
  const [quotaCoordinator] = env.ANONYMOUS_QUOTAS.instances.values();
  assert.equal(quotaCoordinator.storage.objects.get('quota:feedback').count, 1);

  barrier.release();
  const first = await firstPromise;
  assert.equal(first.status, 200);
  const completed = await first.json();
  const retry = await handleRequest(feedbackRequest(), env);
  assert.equal(retry.status, 200);
  const idempotent = await retry.json();
  assert.equal(idempotent.idempotent, true);
  assert.equal(idempotent.attemptId, completed.attemptId);
  assert.equal([...kv.objects.keys()].filter((key) => key.endsWith('/metadata.json')).length, 1);
});

test('an expired attempt cannot overwrite a takeover that completes first', async () => {
  const kv = new MockKv();
  const env = workerEnv({ FEEDBACK_KV: kv });
  const barrier = kv.blockNextSuffix('/screenshot.png');
  const stalePromise = handleRequest(feedbackRequest(), env);
  await barrier.started;
  const staleMarker = parseFeedbackIndexMarker(kv.objects.get(`feedback-index/${UUID}`), UUID);

  env.TEST_NOW = '2030-08-09T10:14:12.000Z';
  const takeover = await handleRequest(feedbackRequest(), env);
  assert.equal(takeover.status, 200);
  const takeoverBody = await takeover.json();
  assert.notEqual(takeoverBody.attemptId, staleMarker.lease.attemptId);

  barrier.release();
  const stale = await stalePromise;
  assert.equal(stale.status, 200);
  assert.equal((await stale.json()).attemptId, takeoverBody.attemptId);
  const finalMarker = parseFeedbackIndexMarker(kv.objects.get(`feedback-index/${UUID}`), UUID);
  assert.equal(finalMarker.lease.attemptId, takeoverBody.attemptId);
  assert.equal([...kv.objects.keys()].some((key) => key.includes(staleMarker.lease.attemptId)), false);
});

test('a failed upload cleans only its attempt and the next retry succeeds', async () => {
  const kv = new MockKv();
  kv.failNextSuffix = 'part-0000.bin';
  const env = workerEnv({ FEEDBACK_KV: kv });
  const failed = await handleRequest(feedbackRequest(), env);
  assert.equal(failed.status, 500);
  assert.equal(kv.objects.has(`feedback-index/${UUID}`), false);
  const succeeded = await handleRequest(feedbackRequest(), env);
  assert.equal(succeeded.status, 200);
  assert.equal([...kv.objects.keys()].filter((key) => key.endsWith('/metadata.json')).length, 1);
});

test('a crash after durable completion is repaired by an idempotent retry', async () => {
  const kv = new MockKv();
  const env = workerEnv({ FEEDBACK_KV: kv });
  const barrier = kv.blockNextSuffix('/metadata.json');
  const firstPromise = handleRequest(feedbackRequest(), env);
  await barrier.started;
  kv.failNextKey = `feedback-index/${UUID}`;
  barrier.release();
  const first = await firstPromise;
  assert.equal(first.status, 500);
  assert.equal((await first.json()).error, 'storage_commit_failed');
  assert.equal([...kv.objects.keys()].filter((key) => key.endsWith('/metadata.json')).length, 1);

  const retry = await handleRequest(feedbackRequest(), env);
  assert.equal(retry.status, 200);
  const retryBody = await retry.json();
  assert.equal(retryBody.idempotent, true);
  const marker = parseFeedbackIndexMarker(kv.objects.get(`feedback-index/${UUID}`), UUID);
  assert.equal(marker.state, 'completed');
  assert.equal(marker.lease.attemptId, retryBody.attemptId);
});

test('completion validation rejects mismatched attempts, metadata paths, and hashes', async () => {
  const kv = new MockKv();
  assert.equal((await handleRequest(feedbackRequest(), workerEnv({ FEEDBACK_KV: kv }))).status, 200);
  const marker = parseFeedbackIndexMarker(kv.objects.get(`feedback-index/${UUID}`), UUID);
  const metadata = JSON.parse(kv.objects.get(marker.completion.metadataKey));

  const wrongAttempt = structuredClone(marker);
  wrongAttempt.completion.attemptId = SECOND_UUID;
  assert.equal(parseFeedbackIndexMarker(wrongAttempt, UUID), null);
  const wrongHash = structuredClone(marker);
  wrongHash.completion.metadataSha256 = '0'.repeat(64);
  assert.notEqual(parseFeedbackIndexMarker(wrongHash, UUID), null);
  const wrongMetadata = structuredClone(metadata);
  wrongMetadata.storage.attemptId = SECOND_UUID;
  assert.equal(validateCompletedFeedbackMetadata(wrongMetadata, marker), false);
});

test('an administrative tombstone prevents deleted submissions from being recreated', async () => {
  const kv = new MockKv();
  const env = workerEnv({ FEEDBACK_KV: kv });
  assert.equal((await handleRequest(feedbackRequest(), env)).status, 200);
  await kv.put(`feedback-tombstone/${UUID}`, JSON.stringify({ deletedAtUtc: env.TEST_NOW }), {
    expirationTtl: 180 * 24 * 60 * 60,
  });
  await kv.delete(`feedback-index/${UUID}`);

  const retry = await handleRequest(feedbackRequest(), env);
  assert.equal(retry.status, 410);
  assert.equal((await retry.json()).error, 'submission_deleted');
  assert.equal(kv.objects.has(`feedback-index/${UUID}`), false);
});

test('legacy clients without a submission header remain serialized and supported', async () => {
  const response = await handleRequest(feedbackRequest({ legacy: true }), workerEnv());
  assert.equal(response.status, 200);
});

test('daily feedback quota remains atomic across concurrent submission coordinators', async () => {
  const env = workerEnv();
  const requests = [];
  for (let index = 0; index < 6; index += 1) {
    const id = `00000000-0000-4000-8000-${index.toString().padStart(12, '0')}`;
    requests.push(handleRequest(feedbackRequest({ submissionId: id }), env));
  }
  const responses = await Promise.all(requests);
  assert.deepEqual(responses.map((response) => response.status).sort(), [200, 200, 200, 200, 200, 429]);
  const markers = [...env.FEEDBACK_KV.objects.entries()]
    .filter(([key]) => key.startsWith('feedback-index/'))
    .map(([, value]) => parseFeedbackIndexMarker(value))
    .filter(Boolean);
  assert.equal(markers.length, 5);
  assert(markers.every((marker) => marker.state === 'completed'));
});

test('feedback enforces the streaming limit without Content-Length', async () => {
  const stream = new ReadableStream({
    start(controller) {
      controller.enqueue(new Uint8Array(24 * 1024 * 1024));
      controller.enqueue(new Uint8Array(1));
      controller.close();
    },
  });
  const request = new Request('https://worker.test/feedback', {
    method: 'PUT',
    headers: {
      'Content-Type': 'multipart/form-data; boundary=test',
      'CF-Connecting-IP': '203.0.113.8',
      'X-NinjaSlayer-Submission-Id': UUID,
    },
    body: stream,
    duplex: 'half',
  });
  assert.equal(request.headers.has('content-length'), false);
  assert.equal((await handleRequest(request, workerEnv())).status, 413);
});
