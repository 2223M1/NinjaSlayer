import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { readFeedbackReview, validateFeedbackReview, feedbackExpiresAt } from '../src/feedback-review.js';
import { publicFeedback } from '../dashboard/publish.mjs';
import { createDashboardServer } from '../dashboard/server.mjs';
import { publishFeedback, feedbackPublication } from '../dashboard/feedback-publisher.mjs';

const id = '9b3d6f32-f6d4-4ca4-9a34-128763c3154b';
test('legacy feedback defaults unresolved; damaged reviews and expired submissions fail visibly', () => {
  assert.deepEqual(readFeedbackReview(null), { status: 'unresolved', reply: '', updatedAt: null });
  assert.throws(() => readFeedbackReview('{'));
  assert.throws(() => readFeedbackReview({ schemaVersion: 1, status: 'resolved', reply: 'done', updatedAt: null }));
  assert.throws(() => validateFeedbackReview({ status: 'closed', reply: '' }));
  assert.throws(() => validateFeedbackReview({ status: 'resolved', reply: 'a'.repeat(5001) }));
  assert.throws(() => validateFeedbackReview({ status: 'resolved', reply: '', context: 'private' }));
  assert.throws(() => feedbackExpiresAt({ receivedAtUtc: '2020-01-01' }));
  const at = new Date().toISOString();
  assert.equal(feedbackExpiresAt({ receivedAtUtc: at }), Math.floor((Date.parse(at) + 180 * 86400_000) / 1000));
});
test('public projection includes only consented text and author treatment, never private attachments', () => {
  const item = { id, at: new Date().toISOString(), description: 'hello', category: 'bug',
    context: { publishDescription: true, modVersion: '1.0.2', seed: 'secret' },
    screenshot: 'secret-image', logs: 'secret-zip', review: { status: 'resolved', reply: '<script>text</script>', updatedAt: new Date().toISOString() } };
  const output = publicFeedback([item, { ...item, context: { publishDescription: false } }]);
  assert.equal(output.length, 1); assert.deepEqual(output[0].review, item.review);
  assert.doesNotMatch(JSON.stringify(output), /secret/);
});
test('local admin can save, reopen and clear responses; rejected reads do not mutate state', async t => {
  let review = readFeedbackReview(null), revoked = false;
  const server = await createDashboardServer({ feedbackReader: {
    loadFeedback: async () => ({ feedback: [], warnings: [] }),
    loadFeedbackReview: async () => { if (revoked) throw new Error('expired'); return review; },
    saveFeedbackReview: async (requested, value) => {
      assert.equal(requested, id); if (revoked) throw new Error('expired');
      return review = { ...validateFeedbackReview(value), updatedAt: new Date().toISOString() };
    },
  } });
  server.listen(0, '127.0.0.1'); await once(server, 'listening'); t.after(() => server.close());
  const base = `http://127.0.0.1:${server.address().port}`;
  const path = `/api/feedback/${id}/review`;
  const put = (value, headers = {}) => fetch(base + path, { method: 'PUT', headers, body: JSON.stringify(value) });
  assert.equal((await put({ status: 'resolved', reply: 'fixed' }, { Origin: 'https://evil.invalid' })).status, 403);
  assert.equal(review.status, 'unresolved');
  assert.equal((await fetch(base + '/preview' + path)).status, 404);
  assert.equal((await put({ status: 'resolved', reply: 'fixed' })).status, 200);
  assert.equal((await (await fetch(base + path)).json()).reply, 'fixed');
  assert.equal((await put({ status: 'unresolved', reply: '' })).status, 200);
  assert.equal(review.status, 'unresolved'); assert.equal(review.reply, '');
  revoked = true;
  assert.equal((await put({ status: 'resolved', reply: 'overwrite' })).status, 400);
  assert.equal(review.reply, '');
});
test('manual site sync is a distinct workflow receipt, suppresses duplicates and reports failed deployment', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'ninja-feedback-sync-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const path = pathToFileURL(join(dir, 'receipt.json'));
  let requested, dispatches = 0, complete = false;
  const api = async (url, method, body) => {
    if (method === 'POST') { dispatches++; requested = body.inputs.feedback_sync_id; assert.equal(body.ref, 'main'); return null; }
    return { workflow_runs: [{ display_title: 'unrelated scheduled build' },
      { display_title: `Feedback sync ${requested}`, status: complete ? 'completed' : 'in_progress', conclusion: 'failure', html_url: 'https://github.com/test/run' }] };
  };
  assert.equal((await publishFeedback(api, path)).state, 'queued');
  assert.equal((await publishFeedback(api, path)).state, 'running'); assert.equal(dispatches, 1);
  complete = true; assert.equal((await feedbackPublication(api, path)).state, 'failed');
});
