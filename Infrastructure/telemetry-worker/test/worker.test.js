import assert from 'node:assert/strict';
import test from 'node:test';
import { handleRequest } from '../src/index.js';

const UUID = '9b3d6f32-f6d4-4ca4-9a34-128763c3154b';

class MockBucket {
  objects = new Map();
  failOnPut = null;
  async put(key, value) {
    if (key.endsWith(this.failOnPut || '__never__')) throw new Error('injected put failure');
    this.objects.set(key, value);
  }
  async delete(key) { this.objects.delete(key); }
}

function feedbackRequest(overrides = {}) {
  const form = new FormData();
  form.set('payload_json', JSON.stringify({
    category: overrides.category ?? 'bug',
    description: 'test feedback',
    gameVersion: 'v0.108.0',
  }));
  form.set('mod_context', JSON.stringify({
    submissionId: overrides.submissionId ?? UUID,
    submittedAtUtc: '2026-07-15T12:34:56.000Z',
    modVersion: '0.1.0',
  }));
  form.set('screenshot', new File(
    [overrides.screenshotBytes ?? new Uint8Array([137, 80, 78, 71])],
    'screenshot.png',
    { type: overrides.screenshotType ?? 'image/png' },
  ));
  form.set('logs', new File([new Uint8Array([80, 75, 3, 4])], 'logs.zip', { type: 'application/zip' }));
  return new Request('https://worker.test/feedback', { method: overrides.method ?? 'PUT', body: form });
}

test('feedback rejects the wrong method, category, and UUID', async () => {
  const env = { FEEDBACK_BUCKET: new MockBucket() };
  assert.equal((await handleRequest(feedbackRequest({ method: 'POST' }), env)).status, 405);
  assert.equal((await handleRequest(feedbackRequest({ category: 'other' }), env)).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ submissionId: 'bad' }), env)).status, 400);
});

test('feedback rejects an invalid attachment type and an oversized screenshot', async () => {
  const env = { FEEDBACK_BUCKET: new MockBucket() };
  assert.equal((await handleRequest(feedbackRequest({ screenshotType: 'image/jpeg' }), env)).status, 400);
  assert.equal((await handleRequest(feedbackRequest({ screenshotBytes: new Uint8Array(5 * 1024 * 1024 + 1) }), env)).status, 413);
});

test('feedback writes stable metadata, screenshot, and logs keys', async () => {
  const bucket = new MockBucket();
  const first = await handleRequest(feedbackRequest(), { FEEDBACK_BUCKET: bucket });
  const firstKeys = [...bucket.objects.keys()].sort();
  const second = await handleRequest(feedbackRequest(), { FEEDBACK_BUCKET: bucket });
  assert.equal(first.status, 200);
  assert.equal(second.status, 200);
  assert.equal(bucket.objects.size, 3);
  assert.deepEqual([...bucket.objects.keys()].sort(), firstKeys);
  assert(firstKeys.every((key) => key.startsWith(`feedback/2026/07/2026-07-15T12-34-56-000Z-${UUID}/`)));
});

test('feedback cleans partial objects when a write fails', async () => {
  const bucket = new MockBucket();
  bucket.failOnPut = 'logs.zip';
  const response = await handleRequest(feedbackRequest(), { FEEDBACK_BUCKET: bucket });
  assert.equal(response.status, 500);
  assert.equal(bucket.objects.size, 0);
});

test('telemetry POST preserves the proxy batch shape', async () => {
  const originalFetch = globalThis.fetch;
  let forwarded;
  globalThis.fetch = async (_url, init) => {
    forwarded = JSON.parse(init.body);
    return new Response('', { status: 200 });
  };
  try {
    const request = new Request('https://worker.test/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ batch: [{ event: 'card_reward', properties: { card: 'IHit' } }], sentAt: 'now' }),
    });
    const response = await handleRequest(request, { POSTHOG_API_KEY: 'secret' });
    assert.equal(response.status, 200);
    assert.equal(forwarded.api_key, 'secret');
    assert.equal(forwarded.batch[0].event, 'card_reward');
    assert.equal(forwarded.sentAt, 'now');
  } finally {
    globalThis.fetch = originalFetch;
  }
});
