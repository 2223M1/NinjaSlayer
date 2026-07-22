import {
  BodyTooLargeError,
  FEEDBACK_MAX_BODY_BYTES,
  FEEDBACK_RETENTION_SECONDS,
  FeedbackValidationError,
  LOG_CHUNK_BYTES,
  jsonResponse,
  readBodyLimited,
} from './limits.js';
import { consumeDailyQuota } from './security.js';
import { validateFeedbackForm } from './validation.js';

function feedbackPrefix(submissionId, receivedAt) {
  const year = receivedAt.getUTCFullYear().toString().padStart(4, '0');
  const month = (receivedAt.getUTCMonth() + 1).toString().padStart(2, '0');
  const timestamp = receivedAt.toISOString().replace(/[:.]/g, '-');
  return `feedback/${year}/${month}/${timestamp}-${submissionId}`;
}

function serverNow(env) {
  return new Date(env.TEST_NOW || Date.now());
}

async function parseFeedbackRequest(request) {
  let bytes;
  try {
    bytes = await readBodyLimited(request, FEEDBACK_MAX_BODY_BYTES);
  } catch (error) {
    if (error instanceof BodyTooLargeError) throw new FeedbackValidationError('Feedback body is too large', 413);
    throw error;
  }
  let form;
  try {
    form = await new Response(bytes, {
      headers: { 'Content-Type': request.headers.get('content-type') || '' },
    }).formData();
  } catch {
    throw new FeedbackValidationError('Invalid multipart body');
  }
  return { bytes, form };
}

async function readIndex(kv, indexKey) {
  try {
    const value = await kv.get(indexKey);
    if (!value) return null;
    const parsed = JSON.parse(value);
    return typeof parsed.prefix === 'string' && parsed.prefix.startsWith('feedback/') ? parsed : null;
  } catch {
    return null;
  }
}

async function deleteOwnedKeys(kv, indexKey, prefix, keys) {
  const index = await readIndex(kv, indexKey);
  const deletions = keys.map((key) => kv.delete(key));
  if (index?.prefix === prefix) deletions.push(kv.delete(indexKey));
  await Promise.allSettled(deletions);
}

export async function processFeedbackRequest(request, env) {
  if (!env.FEEDBACK_KV || !env.ANONYMOUS_QUOTAS) return jsonResponse(503, { error: 'service_not_configured' });
  const contentType = request.headers.get('content-type') || '';
  if (!contentType.toLowerCase().startsWith('multipart/form-data;')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be multipart/form-data' });
  }

  let parsedRequest;
  let data;
  try {
    parsedRequest = await parseFeedbackRequest(request);
    data = await validateFeedbackForm(parsedRequest.form, request.headers.get('X-Submission-Id'));
  } catch (error) {
    if (error instanceof FeedbackValidationError) {
      return jsonResponse(error.status, { error: 'invalid_feedback', message: error.message });
    }
    throw error;
  }

  const quotaResponse = await consumeDailyQuota(
    env,
    request.headers.get('X-Client-Key'),
    'feedback',
    parsedRequest.bytes.byteLength,
  );
  if (!quotaResponse.ok) return quotaResponse;

  const { payload, modContext, screenshot, logs } = data;
  const receivedAt = serverNow(env);
  const indexKey = `feedback-index/${modContext.submissionId}`;
  const expiration = { expirationTtl: FEEDBACK_RETENTION_SECONDS };
  const existingIndex = await readIndex(env.FEEDBACK_KV, indexKey);
  const prefix = existingIndex?.prefix ?? feedbackPrefix(modContext.submissionId, receivedAt);
  const metadataKey = `${prefix}/metadata.json`;
  const screenshotKey = `${prefix}/screenshot.png`;
  const logChunkCount = Math.max(1, Math.ceil(logs.size / LOG_CHUNK_BYTES));
  const logChunkKeys = Array.from(
    { length: logChunkCount },
    (_, index) => `${prefix}/logs/part-${index.toString().padStart(4, '0')}.bin`,
  );

  if (await env.FEEDBACK_KV.get(metadataKey)) {
    return jsonResponse(200, { ok: true, id: modContext.submissionId, prefix, idempotent: true });
  }

  const metadata = {
    schemaVersion: 4,
    receivedAtUtc: receivedAt.toISOString(),
    payload,
    modContext,
    storage: {
      provider: 'workers-kv',
      expiresAfterDays: 180,
      screenshot: { key: screenshotKey, size: screenshot.size, contentType: screenshot.type },
      logs: { size: logs.size, contentType: logs.type, chunkSize: LOG_CHUNK_BYTES, chunks: logChunkKeys },
    },
  };
  const ownedKeys = [metadataKey, screenshotKey, ...logChunkKeys];
  try {
    if (!existingIndex) {
      await env.FEEDBACK_KV.put(indexKey, JSON.stringify({ prefix, receivedAtUtc: receivedAt.toISOString() }), expiration);
    }
    await env.FEEDBACK_KV.put(screenshotKey, await screenshot.arrayBuffer(), expiration);
    for (let index = 0; index < logChunkKeys.length; index += 1) {
      const start = index * LOG_CHUNK_BYTES;
      const chunk = await logs.slice(start, Math.min(start + LOG_CHUNK_BYTES, logs.size)).arrayBuffer();
      await env.FEEDBACK_KV.put(logChunkKeys[index], chunk, expiration);
    }
    await env.FEEDBACK_KV.put(metadataKey, JSON.stringify(metadata, null, 2), expiration);
  } catch (error) {
    await deleteOwnedKeys(env.FEEDBACK_KV, indexKey, prefix, ownedKeys);
    console.error(`[feedback] failed to persist ${modContext.submissionId}: ${error}`);
    return jsonResponse(500, { error: 'storage_failed' });
  }
  return jsonResponse(200, { ok: true, id: modContext.submissionId, prefix });
}

export class FeedbackSubmissionCoordinator {
  constructor(_state, env) {
    this.env = env;
    this.pending = Promise.resolve();
  }

  fetch(request) {
    const operation = this.pending.then(() => processFeedbackRequest(request, this.env));
    this.pending = operation.catch(() => undefined);
    return operation;
  }
}
