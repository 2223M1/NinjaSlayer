import {
  BodyTooLargeError,
  FEEDBACK_MAX_BODY_BYTES,
  FEEDBACK_RETENTION_SECONDS,
  FEEDBACK_WRITE_LEASE_MS,
  FeedbackValidationError,
  LOG_CHUNK_BYTES,
  jsonResponse,
  readBodyLimited,
} from './limits.js';
import { consumeDailyQuota } from './security.js';
import {
  FEEDBACK_METADATA_SCHEMA_VERSION,
  buildCompletionMarker,
  buildWritingMarker,
  feedbackAttemptPrefix,
  feedbackIndexKey,
  feedbackPrefix,
  feedbackTombstoneKey,
  parseFeedbackIndexMarker,
} from './feedback-storage.js';
import { validateFeedbackForm } from './validation.js';

const SUBMISSION_STATE_SCHEMA_VERSION = 1;
const SUBMISSION_STATE_PREFIX = 'feedback-submission:';

class FeedbackLeaseLostError extends Error {}

function serverNow(request, env) {
  const override = request.headers.get('X-Test-Now') || env.TEST_NOW;
  const value = new Date(override || Date.now());
  return Number.isNaN(value.getTime()) ? new Date() : value;
}

function stateKey(submissionId) {
  return `${SUBMISSION_STATE_PREFIX}${submissionId}`;
}

function leaseIsValid(lease, now) {
  return Date.parse(lease.expiresAtUtc) > now.getTime();
}

function responseForCompletion(marker, idempotent = true) {
  return jsonResponse(200, {
    ok: true,
    id: marker.submissionId,
    prefix: marker.lease.prefix,
    attemptId: marker.lease.attemptId,
    ...(idempotent ? { idempotent: true } : {}),
  });
}

function processingResponse(submissionId, lease, now) {
  const retryAfter = Math.max(1, Math.ceil((Date.parse(lease.expiresAtUtc) - now.getTime()) / 1000));
  return jsonResponse(503, {
    error: 'submission_processing',
    id: submissionId,
    processing: true,
    attemptId: lease.attemptId,
  }, { 'Retry-After': String(retryAfter) });
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

async function sha256Hex(value) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)));
  return [...digest].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

async function readIndexMarker(kv, submissionId) {
  try {
    const value = await kv.get(feedbackIndexKey(submissionId));
    return value ? parseFeedbackIndexMarker(value, submissionId) : null;
  } catch {
    return null;
  }
}

async function deleteKeys(kv, keys) {
  await Promise.allSettled(keys.map((key) => kv.delete(key)));
}

function createLease(data, receivedAt) {
  const { modContext, screenshot, logs } = data;
  const attemptId = crypto.randomUUID();
  const prefix = feedbackPrefix(modContext.submissionId, receivedAt);
  const attemptPrefix = feedbackAttemptPrefix(prefix, attemptId);
  const screenshotKey = `${attemptPrefix}/screenshot.png`;
  const logChunkCount = Math.max(1, Math.ceil(logs.size / LOG_CHUNK_BYTES));
  const logChunkKeys = Array.from(
    { length: logChunkCount },
    (_, index) => `${attemptPrefix}/logs/part-${index.toString().padStart(4, '0')}.bin`,
  );
  return {
    submissionId: modContext.submissionId,
    attemptId,
    prefix,
    attemptPrefix,
    metadataKey: `${attemptPrefix}/metadata.json`,
    screenshotKey,
    logChunkKeys,
    ownedKeys: [screenshotKey, ...logChunkKeys, `${attemptPrefix}/metadata.json`],
    startedAtUtc: receivedAt.toISOString(),
    expiresAtUtc: new Date(receivedAt.getTime() + FEEDBACK_WRITE_LEASE_MS).toISOString(),
  };
}

function createMetadata(data, lease, receivedAt) {
  const { payload, modContext, screenshot, logs } = data;
  return {
    schemaVersion: FEEDBACK_METADATA_SCHEMA_VERSION,
    receivedAtUtc: receivedAt.toISOString(),
    payload,
    modContext,
    storage: {
      provider: 'workers-kv',
      expiresAfterDays: 180,
      attemptId: lease.attemptId,
      metadataKey: lease.metadataKey,
      screenshot: { key: lease.screenshotKey, size: screenshot.size, contentType: screenshot.type },
      logs: { size: logs.size, contentType: logs.type, chunkSize: LOG_CHUNK_BYTES, chunks: lease.logChunkKeys },
    },
  };
}

async function persistAttempt(data, lease, request, env, coordinator) {
  const expiration = { expirationTtl: FEEDBACK_RETENTION_SECONDS };
  const ensureOwnership = async () => {
    const renewed = await coordinator.renewLease(lease, serverNow(request, env));
    if (!renewed) throw new FeedbackLeaseLostError();
    Object.assign(lease, renewed);
  };

  await env.FEEDBACK_KV.put(lease.screenshotKey, await data.screenshot.arrayBuffer(), expiration);
  await ensureOwnership();
  for (let index = 0; index < lease.logChunkKeys.length; index += 1) {
    const start = index * LOG_CHUNK_BYTES;
    const chunk = await data.logs.slice(start, Math.min(start + LOG_CHUNK_BYTES, data.logs.size)).arrayBuffer();
    await env.FEEDBACK_KV.put(lease.logChunkKeys[index], chunk, expiration);
    await ensureOwnership();
  }

  const metadataText = JSON.stringify(createMetadata(data, lease, new Date(lease.startedAtUtc)), null, 2);
  await env.FEEDBACK_KV.put(lease.metadataKey, metadataText, expiration);
  await ensureOwnership();
  const metadataSha256 = await sha256Hex(metadataText);
  const marker = await coordinator.completeLease(lease, serverNow(request, env), metadataSha256);
  if (!marker) throw new FeedbackLeaseLostError();
  await env.FEEDBACK_KV.put(feedbackIndexKey(marker.submissionId), JSON.stringify(marker), expiration);
  return marker;
}

export async function processFeedbackRequest(request, env, coordinator) {
  if (!env.FEEDBACK_KV || !env.ANONYMOUS_QUOTAS || !coordinator) {
    return jsonResponse(503, { error: 'service_not_configured' });
  }
  const contentType = request.headers.get('content-type') || '';
  if (!contentType.toLowerCase().startsWith('multipart/form-data;')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be multipart/form-data' });
  }

  const expectedSubmissionId = request.headers.get('X-Submission-Id');
  const initialNow = serverNow(request, env);
  if (expectedSubmissionId) {
    const existing = await coordinator.inspect(expectedSubmissionId, initialNow);
    if (existing?.status === 'completed') {
      try {
        await coordinator.publishCompletion(existing.marker);
        return responseForCompletion(existing.marker);
      } catch (error) {
        console.error(`[feedback] failed to repair completion marker for ${expectedSubmissionId}: ${error}`);
        return jsonResponse(500, { error: 'storage_commit_failed' });
      }
    }
    if (existing?.status === 'writing' && leaseIsValid(existing.lease, initialNow)) {
      return processingResponse(expectedSubmissionId, existing.lease, initialNow);
    }
  }

  let parsedRequest;
  let data;
  try {
    parsedRequest = await parseFeedbackRequest(request);
    data = await validateFeedbackForm(parsedRequest.form, expectedSubmissionId);
  } catch (error) {
    if (error instanceof FeedbackValidationError) {
      return jsonResponse(error.status, { error: 'invalid_feedback', message: error.message });
    }
    throw error;
  }

  const receivedAt = serverNow(request, env);
  if (await env.FEEDBACK_KV.get(feedbackTombstoneKey(data.modContext.submissionId))) {
    return jsonResponse(410, { error: 'submission_deleted' });
  }
  const proposedLease = createLease(data, receivedAt);
  const acquisition = await coordinator.acquireLease(data.modContext.submissionId, proposedLease, receivedAt);
  if (acquisition.status === 'completed') {
    try {
      await coordinator.publishCompletion(acquisition.marker);
      return responseForCompletion(acquisition.marker);
    } catch (error) {
      console.error(`[feedback] failed to repair completion marker for ${data.modContext.submissionId}: ${error}`);
      return jsonResponse(500, { error: 'storage_commit_failed' });
    }
  }
  if (acquisition.status === 'writing') {
    return processingResponse(data.modContext.submissionId, acquisition.lease, receivedAt);
  }

  const lease = acquisition.lease;
  const expiration = { expirationTtl: FEEDBACK_RETENTION_SECONDS };
  try {
    await env.FEEDBACK_KV.put(
      feedbackIndexKey(data.modContext.submissionId),
      JSON.stringify(buildWritingMarker(data.modContext.submissionId, lease)),
      expiration,
    );
    if (acquisition.supersededLease) {
      await deleteKeys(env.FEEDBACK_KV, acquisition.supersededLease.ownedKeys || []);
    }

    const quotaResponse = await consumeDailyQuota(
      env,
      request.headers.get('X-Client-Key'),
      'feedback',
      parsedRequest.bytes.byteLength,
    );
    if (!quotaResponse.ok) {
      await coordinator.abortLease(lease);
      return quotaResponse;
    }
    if (!(await coordinator.renewLease(lease, serverNow(request, env)))) {
      throw new FeedbackLeaseLostError();
    }

    const marker = await persistAttempt(data, lease, request, env, coordinator);
    if (acquisition.supersededLease) {
      await deleteKeys(env.FEEDBACK_KV, acquisition.supersededLease.ownedKeys || []);
    }
    return responseForCompletion(marker, false);
  } catch (error) {
    const cleanup = await coordinator.abortLease(lease);
    if (cleanup.completedMarker) {
      console.error(`[feedback] completion marker publish failed for ${data.modContext.submissionId}: ${error}`);
      return jsonResponse(500, { error: 'storage_commit_failed' });
    }
    if (error instanceof FeedbackLeaseLostError) {
      const current = await coordinator.inspect(data.modContext.submissionId, serverNow(request, env));
      if (current?.status === 'completed') return responseForCompletion(current.marker);
      if (current?.status === 'writing') {
        return processingResponse(data.modContext.submissionId, current.lease, serverNow(request, env));
      }
      return jsonResponse(409, { error: 'submission_superseded' });
    }
    console.error(`[feedback] failed to persist ${data.modContext.submissionId}: ${error}`);
    return jsonResponse(500, { error: 'storage_failed' });
  }
}

export class FeedbackSubmissionCoordinator {
  constructor(state, env) {
    this.storage = state.storage;
    this.env = env;
    this.transitionQueue = Promise.resolve();
  }

  fetch(request) {
    return processFeedbackRequest(request, this.env, this);
  }

  inspect(submissionId, now) {
    return this.withStateLock(async () => {
      const state = await this.storage.get(stateKey(submissionId));
      if (!state) return null;
      this.assertState(state, submissionId);
      if (state.status === 'completed') return state;
      return { ...state, expired: !leaseIsValid(state.lease, now) };
    });
  }

  acquireLease(submissionId, proposedLease, now) {
    return this.withStateLock(async () => {
      const key = stateKey(submissionId);
      const state = await this.storage.get(key);
      if (state) {
        this.assertState(state, submissionId);
        if (state.status === 'completed') return state;
        if (leaseIsValid(state.lease, now)) return state;
      }

      const next = {
        schemaVersion: SUBMISSION_STATE_SCHEMA_VERSION,
        status: 'acquired',
        submissionId,
        lease: proposedLease,
        ...(state?.status === 'writing' ? { supersededLease: state.lease } : {}),
      };
      await this.storage.put(key, {
        schemaVersion: SUBMISSION_STATE_SCHEMA_VERSION,
        status: 'writing',
        submissionId,
        lease: proposedLease,
      });
      return next;
    });
  }

  renewLease(lease, now) {
    return this.withStateLock(async () => {
      const key = stateKeyFromLease(lease);
      const state = await this.storage.get(key);
      if (!this.ownsActiveLease(state, lease, now)) return null;
      const renewed = {
        ...state.lease,
        expiresAtUtc: new Date(now.getTime() + FEEDBACK_WRITE_LEASE_MS).toISOString(),
      };
      await this.storage.put(key, { ...state, lease: renewed });
      return renewed;
    });
  }

  completeLease(lease, now, metadataSha256) {
    return this.withStateLock(async () => {
      const key = stateKeyFromLease(lease);
      const state = await this.storage.get(key);
      if (!this.ownsActiveLease(state, lease, now)) return null;
      const marker = buildCompletionMarker(state.submissionId, state.lease, now.toISOString(), metadataSha256);
      await this.storage.put(key, {
        schemaVersion: SUBMISSION_STATE_SCHEMA_VERSION,
        status: 'completed',
        submissionId: state.submissionId,
        marker,
      });
      if (typeof this.storage.setAlarm === 'function') {
        await this.storage.setAlarm(now.getTime() + FEEDBACK_RETENTION_SECONDS * 1000);
      }
      return marker;
    });
  }

  abortLease(lease) {
    return this.withStateLock(async () => {
      const key = stateKeyFromLease(lease);
      const state = await this.storage.get(key);
      if (state?.status === 'completed' && state.marker.lease.attemptId === lease.attemptId) {
        return { completedMarker: state.marker };
      }

      await deleteKeys(this.env.FEEDBACK_KV, lease.ownedKeys || []);
      if (!state || state.status !== 'writing' || state.lease.attemptId !== lease.attemptId) {
        return { completedMarker: null };
      }
      const index = await readIndexMarker(this.env.FEEDBACK_KV, state.submissionId);
      if (index?.state === 'writing' && index.lease.attemptId === lease.attemptId) {
        await Promise.allSettled([this.env.FEEDBACK_KV.delete(feedbackIndexKey(state.submissionId))]);
      }
      await this.storage.delete(key);
      return { completedMarker: null };
    });
  }

  publishCompletion(marker) {
    return this.env.FEEDBACK_KV.put(
      feedbackIndexKey(marker.submissionId),
      JSON.stringify(marker),
      { expirationTtl: FEEDBACK_RETENTION_SECONDS },
    );
  }

  async alarm() {
    if (typeof this.storage.deleteAll === 'function') await this.storage.deleteAll();
  }

  withStateLock(operation) {
    const result = this.transitionQueue.then(operation, operation);
    this.transitionQueue = result.then(() => undefined, () => undefined);
    return result;
  }

  ownsActiveLease(state, lease, now) {
    return state?.status === 'writing'
      && state.lease.attemptId === lease.attemptId
      && leaseIsValid(state.lease, now);
  }

  assertState(state, submissionId) {
    if (state.schemaVersion !== SUBMISSION_STATE_SCHEMA_VERSION || state.submissionId !== submissionId) {
      throw new Error(`Invalid submission coordinator state for ${submissionId}`);
    }
    if (state.status === 'writing' && state.lease?.attemptId) return;
    if (state.status === 'completed' && state.marker?.state === 'completed') return;
    throw new Error(`Unsupported submission coordinator state for ${submissionId}`);
  }
}

function stateKeyFromLease(lease) {
  return stateKey(lease.submissionId);
}
