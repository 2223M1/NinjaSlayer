export const POSTHOG_HOST = 'https://us.i.posthog.com';
export const TELEMETRY_MAX_BODY_BYTES = 5 * 1024 * 1024;
export const TELEMETRY_MAX_BATCH_SIZE = 50;
export const TELEMETRY_DAILY_BYTES = 25 * 1024 * 1024;
export const FEEDBACK_MAX_BODY_BYTES = 24 * 1024 * 1024;
export const FEEDBACK_DAILY_COUNT = 5;
export const FEEDBACK_DAILY_BYTES = 96 * 1024 * 1024;
export const SCREENSHOT_MAX_BYTES = 5 * 1024 * 1024;
export const LOGS_MAX_BYTES = 16 * 1024 * 1024;
export const LOG_CHUNK_BYTES = 4 * 1024 * 1024;
export const JSON_MAX_BYTES = 64 * 1024;
export const REQUEST_TIMEOUT_MS = 9000;
export const FEEDBACK_RETENTION_SECONDS = 180 * 24 * 60 * 60;

export const JSON_HEADER = { 'Content-Type': 'application/json' };

export function jsonResponse(status, body, headers = {}) {
  return new Response(JSON.stringify(body), { status, headers: { ...JSON_HEADER, ...headers } });
}

export class BodyTooLargeError extends Error {}

export class FeedbackValidationError extends Error {
  constructor(message, status = 400) {
    super(message);
    this.status = status;
  }
}

export async function readBodyLimited(request, maximumBytes) {
  const declaredLength = Number.parseInt(request.headers.get('content-length') || '0', 10);
  if (Number.isFinite(declaredLength) && declaredLength > maximumBytes) throw new BodyTooLargeError();
  if (!request.body) return new Uint8Array();

  const reader = request.body.getReader();
  const chunks = [];
  let totalBytes = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      totalBytes += value.byteLength;
      if (totalBytes > maximumBytes) {
        await reader.cancel('body limit exceeded');
        throw new BodyTooLargeError();
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }

  const body = new Uint8Array(totalBytes);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return body;
}
