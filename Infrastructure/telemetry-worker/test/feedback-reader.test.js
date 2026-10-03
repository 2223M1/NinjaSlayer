import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { buildCompletionMarker } from '../src/feedback-storage.js';
import { loadFeedbackDetails, saveFeedbackReview } from '../scripts/feedback-reader.js';

const id = '9b3d6f32-f6d4-4ca4-9a34-128763c3154b';
function fixture({ corrupt = false, expired = false, failed = false } = {}) {
  const now = new Date().toISOString(), prefix = 'feedback/test', attemptId = id;
  const attemptPrefix = `${prefix}/attempts/${attemptId}`, metadataKey = `${attemptPrefix}/metadata.json`;
  const metadata = { schemaVersion: 5, receivedAtUtc: expired ? '2020-01-01' : now,
    modContext: { submissionId: id }, payload: { description: 'test' },
    storage: { attemptId, metadataKey, screenshot: { key: `${attemptPrefix}/image.png` }, logs: { chunks: [`${attemptPrefix}/logs.zip`] } } };
  const bytes = Buffer.from(JSON.stringify(metadata));
  const marker = buildCompletionMarker(id, { attemptId, prefix, attemptPrefix, metadataKey, startedAtUtc: now, expiresAtUtc: now }, now,
    createHash('sha256').update(bytes).digest('hex'));
  const gets = [], writes = [];
  const store = {
    async get(key) {
      gets.push(key);
      if (key.startsWith('feedback-review/')) {
        if (failed) throw new Error('network failure');
        return corrupt ? Buffer.from('{broken') : null;
      }
      return key.startsWith('feedback-index/') ? Buffer.from(JSON.stringify(marker)) : bytes;
    },
    async put(...args) { writes.push(args); },
  };
  return { store, gets, writes, metadata };
}

test('detail and save each validate the completion and prior review once, without listing all review keys', async () => {
  const read = fixture();
  const details = await loadFeedbackDetails(id, read.store);
  assert.equal(details.metadata.payload.description, 'test');
  assert.equal(details.review.status, 'unresolved');
  assert.equal(read.gets.length, 3);
  const saved = fixture();
  const review = await saveFeedbackReview(id, { status: 'resolved', reply: 'fixed' }, saved.store);
  assert.equal(review.reply, 'fixed'); assert.equal(saved.gets.length, 3); assert.equal(saved.writes.length, 1);
  assert.equal(saved.writes[0][0], `feedback-review/${id}`);
  assert.equal(saved.writes[0][2], Math.floor((Date.parse(saved.metadata.receivedAtUtc) + 180 * 86400_000) / 1000));
});

test('faster saves still reject expired feedback, corrupted reviews and failed reads without overwriting', async () => {
  for (const options of [{ corrupt: true }, { expired: true }, { failed: true }]) {
    const { store, writes } = fixture(options);
    await assert.rejects(saveFeedbackReview(id, { status: 'resolved', reply: 'new reply' }, store));
    assert.equal(writes.length, 0);
  }
});
