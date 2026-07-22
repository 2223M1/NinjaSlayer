import assert from 'node:assert/strict';
import test from 'node:test';
import { handleRequest } from '../src/index.js';

const UUID = '9b3d6f32-f6d4-4ca4-9a34-128763c3154b';
const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const ZIP = new Uint8Array([0x50, 0x4b, 0x03, 0x04]);

class MockKv {
  objects = new Map();
  options = new Map();
  writes = [];
  failOnPut = null;

  async get(key) { return this.objects.get(key) ?? null; }
  async put(key, value, options) {
    if (key.endsWith(this.failOnPut || '__never__')) throw new Error('injected put failure');
    this.writes.push(key);
    this.objects.set(key, value);
    this.options.set(key, options);
  }
  async delete(key) {
    this.objects.delete(key);
    this.options.delete(key);
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

function workerEnv(overrides = {}) {
  return {
    RATE_LIMIT_SALT: 'test-only-rate-limit-salt',
    POSTHOG_API_KEY: 'posthog-secret',
    TELEMETRY_RATE_LIMITER: new MockRateLimiter(),
    FEEDBACK_RATE_LIMITER: new MockRateLimiter(),
    FEEDBACK_KV: new MockKv(),
    TEST_NOW: '2030-08-09T10:11:12.000Z',
    ...overrides,
  };
}

function feedbackRequest(overrides = {}) {
  const form = new FormData();
  form.set('payload_json', JSON.stringify({
    category: overrides.category ?? 'bug',
    description: 'test feedback',
    gameVersion: 'v0.109.0',
  }));
  form.set('mod_context', JSON.stringify({
    submissionId: overrides.submissionId ?? UUID,
    submittedAtUtc: '2026-07-15T12:34:56.000Z',
    modVersion: '0.1.0',
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
  return new Request('https://worker.test/feedback', {
    method: overrides.method ?? 'PUT',
    headers: { 'CF-Connecting-IP': overrides.ip ?? '203.0.113.8' },
    body: form,
  });
}

function telemetryBody(overrides = {}) {
  return {
    batch: [{
      event: overrides.event ?? 'run_history.completed',
      distinct_id: 'installation-id',
      properties: {
        schema: overrides.schema ?? 'ritsulib.telemetry.v1',
        applicantId: overrides.applicantId ?? 'NinjaSlayer',
        requestId: overrides.requestId ?? 'run_history',
        category: overrides.category ?? 'RunHistory',
        payload: { victory: true },
        ...overrides.properties,
      },
    }],
  };
}

function telemetryRequest(body = telemetryBody(), overrides = {}) {
  return new Request('https://worker.test/', {
    method: overrides.method ?? 'POST',
    headers: {
      'Content-Type': 'application/json',
      'CF-Connecting-IP': overrides.ip ?? '203.0.113.7',
    },
    body: JSON.stringify(body),
  });
}

test('production endpoints fail closed when secrets or limiter bindings are missing', async () => {
  const telemetry = await handleRequest(telemetryRequest(), {});
  const feedback = await handleRequest(feedbackRequest(), { FEEDBACK_KV: new MockKv() });
  assert.equal(telemetry.status, 503);
  assert.equal(feedback.status, 503);
});

test('rate limiting uses a stable HMAC key and returns 429 when exhausted', async () => {
  const limiter = new MockRateLimiter(1);
  const env = workerEnv({ TELEMETRY_RATE_LIMITER: limiter });
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response('', { status: 200 });
  try {
    assert.equal((await handleRequest(telemetryRequest(), env)).status, 200);
    assert.equal((await handleRequest(telemetryRequest(), env)).status, 429);
    assert.equal(limiter.calls.length, 2);
    assert.equal(limiter.calls[0], limiter.calls[1]);
    assert.notEqual(limiter.calls[0], '203.0.113.7');
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test('telemetry rejects non-whitelisted events, envelopes, and location properties', async () => {
  const env = workerEnv();
  assert.equal((await handleRequest(telemetryRequest(telemetryBody({ event: 'card_reward' })), env)).status, 400);
  assert.equal((await handleRequest(telemetryRequest(telemetryBody({ applicantId: 'OtherMod' })), env)).status, 400);
  assert.equal((await handleRequest(telemetryRequest(telemetryBody({ properties: { $ip: '127.0.0.1' } })), env)).status, 400);
});

test('telemetry enforces the streaming limit without Content-Length', async () => {
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

test('telemetry forwards only the validated batch and never forwards an IP header', async () => {
  const originalFetch = globalThis.fetch;
  let forwarded;
  let forwardedHeaders;
  globalThis.fetch = async (_url, init) => {
    forwarded = JSON.parse(init.body);
    forwardedHeaders = init.headers;
    return new Response('', { status: 200 });
  };
  try {
    const response = await handleRequest(telemetryRequest(), workerEnv());
    assert.equal(response.status, 200);
    assert.equal(forwarded.api_key, 'posthog-secret');
    assert.equal(forwarded.batch[0].event, 'run_history.completed');
    assert.equal(forwarded.batch[0].properties.$ip, undefined);
    assert.equal(forwardedHeaders['X-Forwarded-For'], undefined);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test('feedback rejects invalid metadata, MIME types, sizes, and file signatures', async () => {
  const env = workerEnv();
  assert.equal((await handleRequest(feedbackRequest({ method: 'POST' }), env)).status, 405);
  assert.equal((await handleRequest(feedbackRequest({ category: 'other' }), env)).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ submissionId: 'bad' }), env)).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ screenshotType: 'image/jpeg' }), env)).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ screenshotBytes: new Uint8Array(5 * 1024 * 1024 + 1) }), env)).status, 413);
  assert.equal((await handleRequest(feedbackRequest({ screenshotBytes: new Uint8Array(8) }), env)).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ logsBytes: new Uint8Array([1, 2, 3, 4]) }), env)).status, 400);
});

test('feedback uses server time, writes sequentially, and commits metadata last', async () => {
  const kv = new MockKv();
  const logsBytes = new Uint8Array(4 * 1024 * 1024 + 1);
  logsBytes.set(ZIP);
  const response = await handleRequest(feedbackRequest({ logsBytes }), workerEnv({ FEEDBACK_KV: kv }));
  assert.equal(response.status, 200);

  const body = await response.json();
  assert(body.prefix.startsWith(`feedback/2030/08/2030-08-09T10-11-12-000Z-${UUID}`));
  assert.equal(kv.writes[0], `feedback-index/${UUID}`);
  assert(kv.writes[1].endsWith('/screenshot.png'));
  assert(kv.writes[2].endsWith('/logs/part-0000.bin'));
  assert(kv.writes[3].endsWith('/logs/part-0001.bin'));
  assert(kv.writes[4].endsWith('/metadata.json'));
  assert([...kv.options.values()].every((options) => options.expirationTtl === 180 * 24 * 60 * 60));
});

test('feedback reuses the submission index and returns the existing commit idempotently', async () => {
  const kv = new MockKv();
  const env = workerEnv({ FEEDBACK_KV: kv });
  const first = await handleRequest(feedbackRequest(), env);
  const keys = [...kv.objects.keys()].sort();
  const writeCount = kv.writes.length;
  const second = await handleRequest(feedbackRequest(), env);
  assert.equal(first.status, 200);
  assert.equal(second.status, 200);
  assert.equal((await second.json()).idempotent, true);
  assert.deepEqual([...kv.objects.keys()].sort(), keys);
  assert.equal(kv.writes.length, writeCount);
});

test('feedback cleans the index and every partial object when a sequential write fails', async () => {
  const kv = new MockKv();
  kv.failOnPut = 'part-0000.bin';
  const response = await handleRequest(feedbackRequest(), workerEnv({ FEEDBACK_KV: kv }));
  assert.equal(response.status, 500);
  assert.equal(kv.objects.size, 0);
});

test('feedback enforces the total streaming limit without Content-Length', async () => {
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
    },
    body: stream,
    duplex: 'half',
  });
  assert.equal(request.headers.has('content-length'), false);
  assert.equal((await handleRequest(request, workerEnv())).status, 413);
});
