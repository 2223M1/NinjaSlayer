const SHA256_PATTERN = /^[0-9a-f]{64}$/;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export const FEEDBACK_INDEX_SCHEMA_VERSION = 2;
export const FEEDBACK_METADATA_SCHEMA_VERSION = 5;

export function feedbackIndexKey(submissionId) {
  return `feedback-index/${submissionId}`;
}

export function feedbackTombstoneKey(submissionId) {
  return `feedback-tombstone/${submissionId}`;
}

export function feedbackPrefix(submissionId, receivedAt) {
  const year = receivedAt.getUTCFullYear().toString().padStart(4, '0');
  const month = (receivedAt.getUTCMonth() + 1).toString().padStart(2, '0');
  const timestamp = receivedAt.toISOString().replace(/[:.]/g, '-');
  return `feedback/${year}/${month}/${timestamp}-${submissionId}`;
}

export function feedbackAttemptPrefix(prefix, attemptId) {
  return `${prefix}/attempts/${attemptId}`;
}

export function buildWritingMarker(submissionId, lease) {
  return {
    schemaVersion: FEEDBACK_INDEX_SCHEMA_VERSION,
    state: 'writing',
    submissionId,
    lease: publicLease(lease),
  };
}

export function buildCompletionMarker(submissionId, lease, completedAtUtc, metadataSha256) {
  return {
    schemaVersion: FEEDBACK_INDEX_SCHEMA_VERSION,
    state: 'completed',
    submissionId,
    lease: publicLease(lease),
    completion: {
      attemptId: lease.attemptId,
      completedAtUtc,
      metadataKey: lease.metadataKey,
      metadataSha256,
    },
  };
}

export function parseFeedbackIndexMarker(value, expectedSubmissionId = null) {
  let marker;
  try {
    marker = typeof value === 'string' ? JSON.parse(value) : value;
  } catch {
    return null;
  }
  if (!marker || typeof marker !== 'object' || Array.isArray(marker)) return null;
  if (marker.schemaVersion !== FEEDBACK_INDEX_SCHEMA_VERSION) return null;
  if (marker.state !== 'writing' && marker.state !== 'completed') return null;
  if (typeof marker.submissionId !== 'string' || !UUID_PATTERN.test(marker.submissionId)) return null;
  if (expectedSubmissionId && marker.submissionId !== expectedSubmissionId) return null;
  if (!isValidLease(marker.lease)) return null;

  const expectedAttemptPrefix = feedbackAttemptPrefix(marker.lease.prefix, marker.lease.attemptId);
  if (marker.lease.attemptPrefix !== expectedAttemptPrefix) return null;
  if (marker.lease.metadataKey !== `${expectedAttemptPrefix}/metadata.json`) return null;
  if (marker.state === 'writing') return marker.completion === undefined ? marker : null;

  const completion = marker.completion;
  if (!completion || typeof completion !== 'object' || Array.isArray(completion)) return null;
  if (completion.attemptId !== marker.lease.attemptId) return null;
  if (completion.metadataKey !== marker.lease.metadataKey) return null;
  if (typeof completion.completedAtUtc !== 'string' || Number.isNaN(Date.parse(completion.completedAtUtc))) return null;
  if (typeof completion.metadataSha256 !== 'string' || !SHA256_PATTERN.test(completion.metadataSha256)) return null;
  return marker;
}

export function validateCompletedFeedbackMetadata(metadata, marker) {
  if (!metadata || typeof metadata !== 'object' || Array.isArray(metadata)) return false;
  if (!marker || marker.state !== 'completed') return false;
  if (metadata.schemaVersion !== FEEDBACK_METADATA_SCHEMA_VERSION) return false;
  if (metadata.modContext?.submissionId !== marker.submissionId) return false;
  if (metadata.storage?.attemptId !== marker.lease.attemptId) return false;
  if (metadata.storage?.metadataKey !== marker.completion.metadataKey) return false;
  if (!isAttemptKey(metadata.storage?.screenshot?.key, marker.lease.attemptPrefix)) return false;
  const chunks = metadata.storage?.logs?.chunks;
  if (!Array.isArray(chunks) || chunks.length === 0) return false;
  return chunks.every((key) => isAttemptKey(key, marker.lease.attemptPrefix));
}

function publicLease(lease) {
  return {
    attemptId: lease.attemptId,
    prefix: lease.prefix,
    attemptPrefix: lease.attemptPrefix,
    metadataKey: lease.metadataKey,
    startedAtUtc: lease.startedAtUtc,
    expiresAtUtc: lease.expiresAtUtc,
  };
}

function isValidLease(lease) {
  if (!lease || typeof lease !== 'object' || Array.isArray(lease)) return false;
  if (typeof lease.attemptId !== 'string' || !UUID_PATTERN.test(lease.attemptId)) return false;
  if (typeof lease.prefix !== 'string' || !lease.prefix.startsWith('feedback/')) return false;
  if (typeof lease.attemptPrefix !== 'string' || typeof lease.metadataKey !== 'string') return false;
  if (typeof lease.startedAtUtc !== 'string' || Number.isNaN(Date.parse(lease.startedAtUtc))) return false;
  return typeof lease.expiresAtUtc === 'string' && !Number.isNaN(Date.parse(lease.expiresAtUtc));
}

function isAttemptKey(key, attemptPrefix) {
  return typeof key === 'string' && key.startsWith(`${attemptPrefix}/`) && !key.includes('..');
}
