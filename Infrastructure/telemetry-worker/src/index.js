const POSTHOG_HOST = 'https://us.i.posthog.com';
const TELEMETRY_MAX_BODY_BYTES = 5 * 1024 * 1024;
const TELEMETRY_MAX_BATCH_SIZE = 50;
const FEEDBACK_MAX_BODY_BYTES = 24 * 1024 * 1024;
const SCREENSHOT_MAX_BYTES = 5 * 1024 * 1024;
const LOGS_MAX_BYTES = 16 * 1024 * 1024;
const LOG_CHUNK_BYTES = 4 * 1024 * 1024;
const JSON_MAX_BYTES = 64 * 1024;
const REQUEST_TIMEOUT_MS = 9000;
const FEEDBACK_RETENTION_SECONDS = 180 * 24 * 60 * 60;
const JSON_HEADER = { 'Content-Type': 'application/json' };
const FEEDBACK_CATEGORIES = new Set(['bug', 'balance', 'feedback', 'translation']);
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const PNG_SIGNATURE = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const ZIP_SIGNATURES = [
  new Uint8Array([0x50, 0x4b, 0x03, 0x04]),
  new Uint8Array([0x50, 0x4b, 0x05, 0x06]),
  new Uint8Array([0x50, 0x4b, 0x07, 0x08]),
];

function jsonResponse(status, body, headers = {}) {
  return new Response(JSON.stringify(body), { status, headers: { ...JSON_HEADER, ...headers } });
}

class BodyTooLargeError extends Error {}

class FeedbackValidationError extends Error {
  constructor(message, status = 400) {
    super(message);
    this.status = status;
  }
}

async function readBodyLimited(request, maximumBytes) {
  const declaredLength = Number.parseInt(request.headers.get('content-length') || '0', 10);
  if (Number.isFinite(declaredLength) && declaredLength > maximumBytes) {
    throw new BodyTooLargeError();
  }
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

function bytesEqual(actual, expected) {
  if (actual.byteLength < expected.byteLength) return false;
  return expected.every((value, index) => actual[index] === value);
}

async function hasFileSignature(file, signatures) {
  const maximumLength = Math.max(...signatures.map((signature) => signature.byteLength));
  const prefix = new Uint8Array(await file.slice(0, maximumLength).arrayBuffer());
  return signatures.some((signature) => bytesEqual(prefix, signature));
}

async function hashClientIp(request, env) {
  const ip = request.headers.get('CF-Connecting-IP');
  if (!ip || typeof env.RATE_LIMIT_SALT !== 'string' || env.RATE_LIMIT_SALT.length < 16) {
    return null;
  }

  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    'raw',
    encoder.encode(env.RATE_LIMIT_SALT),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const signature = new Uint8Array(await crypto.subtle.sign('HMAC', key, encoder.encode(ip)));
  return [...signature].map((value) => value.toString(16).padStart(2, '0')).join('');
}

async function enforceRateLimit(request, env, bindingName) {
  const limiter = env[bindingName];
  const clientKey = await hashClientIp(request, env);
  if (!limiter || typeof limiter.limit !== 'function' || !clientKey) {
    return jsonResponse(503, { error: 'service_not_configured' });
  }

  const result = await limiter.limit({ key: clientKey });
  return result?.success ? null : jsonResponse(429, { error: 'rate_limited' }, { 'Retry-After': '60' });
}

function validateTelemetryEvent(event, index) {
  if (!event || typeof event !== 'object' || Array.isArray(event)) {
    return `batch[${index}] is not an object`;
  }
  if (event.event !== 'run_history.completed') {
    return `batch[${index}].event is not allowed`;
  }
  if (!event.properties || typeof event.properties !== 'object' || Array.isArray(event.properties)) {
    return `batch[${index}].properties must be an object`;
  }

  const properties = event.properties;
  const expected = {
    schema: 'ritsulib.telemetry.v1',
    applicantId: 'NinjaSlayer',
    requestId: 'run_history',
    category: 'RunHistory',
  };
  for (const [name, value] of Object.entries(expected)) {
    if (properties[name] !== value) return `batch[${index}].properties.${name} is not allowed`;
  }
  if (Object.keys(properties).some((name) => name === '$ip' || name.startsWith('$geoip'))) {
    return `batch[${index}].properties contains a forbidden location field`;
  }
  if (event.distinct_id !== undefined && (typeof event.distinct_id !== 'string' || event.distinct_id.length > 200)) {
    return `batch[${index}].distinct_id is invalid`;
  }
  if (event.timestamp !== undefined && event.timestamp !== null && typeof event.timestamp !== 'string') {
    return `batch[${index}].timestamp must be a string or null`;
  }
  return null;
}

async function handleTelemetry(request, env, ctx) {
  if (request.method !== 'POST') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only POST is accepted' }, { Allow: 'POST' });
  }
  if (!env.POSTHOG_API_KEY) return jsonResponse(503, { error: 'service_not_configured' });

  const rateLimitResponse = await enforceRateLimit(request, env, 'TELEMETRY_RATE_LIMITER');
  if (rateLimitResponse) return rateLimitResponse;
  if (!(request.headers.get('content-type') || '').toLowerCase().includes('application/json')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be application/json' });
  }

  let bodyBytes;
  try {
    bodyBytes = await readBodyLimited(request, TELEMETRY_MAX_BODY_BYTES);
  } catch (error) {
    if (error instanceof BodyTooLargeError) {
      return jsonResponse(413, { error: 'payload_too_large' });
    }
    throw error;
  }

  let body;
  try {
    body = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bodyBytes));
  } catch {
    return jsonResponse(400, { error: 'invalid_json', message: 'Failed to parse body as JSON' });
  }
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    return jsonResponse(400, { error: 'invalid_format', message: 'Body must be a JSON object' });
  }
  if (!Array.isArray(body.batch) || body.batch.length === 0) {
    return jsonResponse(400, { error: 'invalid_format', message: 'Missing or empty batch array' });
  }
  if (body.batch.length > TELEMETRY_MAX_BATCH_SIZE) {
    return jsonResponse(400, { error: 'batch_too_large', message: `Max ${TELEMETRY_MAX_BATCH_SIZE} events per batch` });
  }
  for (let index = 0; index < body.batch.length; index += 1) {
    const error = validateTelemetryEvent(body.batch[index], index);
    if (error) return jsonResponse(400, { error: 'invalid_format', message: error });
  }

  const cleanBody = {
    api_key: env.POSTHOG_API_KEY,
    batch: body.batch.map((event) => ({
      event: event.event,
      properties: event.properties,
      ...(event.distinct_id === undefined ? {} : { distinct_id: event.distinct_id }),
      ...(event.timestamp === undefined ? {} : { timestamp: event.timestamp }),
    })),
  };
  if (typeof body.sentAt === 'string' && body.sentAt.length <= 64) cleanBody.sentAt = body.sentAt;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  let postHogResponse;
  try {
    postHogResponse = await fetch(`${POSTHOG_HOST}/batch/`, {
      method: 'POST',
      headers: JSON_HEADER,
      body: JSON.stringify(cleanBody),
      signal: controller.signal,
    });
  } catch (error) {
    return jsonResponse(502, { error: 'upstream_unreachable', message: error.message });
  } finally {
    clearTimeout(timer);
  }

  if (postHogResponse.ok) {
    ctx.waitUntil(Promise.resolve().then(() => console.log(`[ok] ${body.batch.length} telemetry events accepted`)));
    return jsonResponse(200, { ok: true, accepted: body.batch.length, rejected: 0 });
  }

  const upstreamBody = (await postHogResponse.text()).slice(0, 200);
  ctx.waitUntil(Promise.resolve().then(() => console.error(`[fail] PostHog ${postHogResponse.status}: ${upstreamBody}`)));
  return jsonResponse(502, { error: 'upstream_failed', message: `PostHog returned ${postHogResponse.status}` });
}

function parseJsonField(form, name) {
  const value = form.get(name);
  if (typeof value !== 'string' || new TextEncoder().encode(value).length > JSON_MAX_BYTES) {
    throw new FeedbackValidationError(`${name} must be JSON text no larger than ${JSON_MAX_BYTES} bytes`);
  }
  try {
    const parsed = JSON.parse(value);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error();
    return parsed;
  } catch {
    throw new FeedbackValidationError(`${name} is not a valid JSON object`);
  }
}

function requireFile(form, name, type, maximumBytes) {
  const value = form.get(name);
  if (!(value instanceof File)) throw new FeedbackValidationError(`${name} is required`);
  if (value.type !== type) throw new FeedbackValidationError(`${name} must use Content-Type ${type}`);
  if (value.size > maximumBytes) throw new FeedbackValidationError(`${name} exceeds ${maximumBytes} bytes`, 413);
  return value;
}

function feedbackPrefix(submissionId, receivedAt) {
  const year = receivedAt.getUTCFullYear().toString().padStart(4, '0');
  const month = (receivedAt.getUTCMonth() + 1).toString().padStart(2, '0');
  const timestamp = receivedAt.toISOString().replace(/[:.]/g, '-');
  return `feedback/${year}/${month}/${timestamp}-${submissionId}`;
}

function serverNow(env) {
  return new Date(env.TEST_NOW || Date.now());
}

async function parseFeedbackForm(request) {
  let bytes;
  try {
    bytes = await readBodyLimited(request, FEEDBACK_MAX_BODY_BYTES);
  } catch (error) {
    if (error instanceof BodyTooLargeError) throw new FeedbackValidationError('Feedback body is too large', 413);
    throw error;
  }

  try {
    return await new Response(bytes, {
      headers: { 'Content-Type': request.headers.get('content-type') || '' },
    }).formData();
  } catch {
    throw new FeedbackValidationError('Invalid multipart body');
  }
}

async function handleFeedback(request, env) {
  if (request.method !== 'PUT') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only PUT is accepted for /feedback' }, { Allow: 'PUT' });
  }
  if (!env.FEEDBACK_KV) return jsonResponse(503, { error: 'service_not_configured' });

  const rateLimitResponse = await enforceRateLimit(request, env, 'FEEDBACK_RATE_LIMITER');
  if (rateLimitResponse) return rateLimitResponse;
  const contentType = request.headers.get('content-type') || '';
  if (!contentType.toLowerCase().startsWith('multipart/form-data;')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be multipart/form-data' });
  }

  let form;
  let payload;
  let modContext;
  let screenshot;
  let logs;
  try {
    form = await parseFeedbackForm(request);
    payload = parseJsonField(form, 'payload_json');
    modContext = parseJsonField(form, 'mod_context');
    if (!FEEDBACK_CATEGORIES.has(payload.category)) throw new FeedbackValidationError('Unsupported feedback category');
    if (typeof payload.description !== 'string' || payload.description.length === 0 || payload.description.length > 8000) {
      throw new FeedbackValidationError('Feedback description must contain 1 to 8000 characters');
    }
    if (typeof modContext.submissionId !== 'string' || !UUID_PATTERN.test(modContext.submissionId)) {
      throw new FeedbackValidationError('mod_context.submissionId must be a UUID');
    }
    screenshot = requireFile(form, 'screenshot', 'image/png', SCREENSHOT_MAX_BYTES);
    logs = requireFile(form, 'logs', 'application/zip', LOGS_MAX_BYTES);
    if (!(await hasFileSignature(screenshot, [PNG_SIGNATURE]))) {
      throw new FeedbackValidationError('screenshot does not have a valid PNG signature');
    }
    if (!(await hasFileSignature(logs, ZIP_SIGNATURES))) {
      throw new FeedbackValidationError('logs does not have a valid ZIP signature');
    }
  } catch (error) {
    if (error instanceof FeedbackValidationError) {
      return jsonResponse(error.status, { error: 'invalid_feedback', message: error.message });
    }
    throw error;
  }

  const receivedAt = serverNow(env);
  const indexKey = `feedback-index/${modContext.submissionId}`;
  const expiration = { expirationTtl: FEEDBACK_RETENTION_SECONDS };
  let prefix;
  try {
    const existingIndex = await env.FEEDBACK_KV.get(indexKey);
    if (existingIndex) {
      const parsed = JSON.parse(existingIndex.value ?? existingIndex);
      prefix = parsed.prefix;
    }
  } catch (error) {
    console.warn(`[feedback] invalid idempotency index for ${modContext.submissionId}: ${error}`);
  }
  if (typeof prefix !== 'string' || !prefix.startsWith('feedback/')) {
    prefix = feedbackPrefix(modContext.submissionId, receivedAt);
  }

  const metadataKey = `${prefix}/metadata.json`;
  const screenshotKey = `${prefix}/screenshot.png`;
  const logChunkCount = Math.max(1, Math.ceil(logs.size / LOG_CHUNK_BYTES));
  const logChunkKeys = Array.from(
    { length: logChunkCount },
    (_, index) => `${prefix}/logs/part-${index.toString().padStart(4, '0')}.bin`,
  );

  try {
    if (await env.FEEDBACK_KV.get(metadataKey)) {
      return jsonResponse(200, { ok: true, id: modContext.submissionId, prefix, idempotent: true });
    }
  } catch (error) {
    console.warn(`[feedback] commit marker check failed for ${modContext.submissionId}: ${error}`);
  }

  const metadata = {
    schemaVersion: 3,
    receivedAtUtc: receivedAt.toISOString(),
    payload,
    modContext,
    storage: {
      provider: 'workers-kv',
      expiresAfterDays: 180,
      screenshot: { key: screenshotKey, size: screenshot.size, contentType: screenshot.type },
      logs: {
        size: logs.size,
        contentType: logs.type,
        chunkSize: LOG_CHUNK_BYTES,
        chunks: logChunkKeys,
      },
    },
  };
  const writtenKeys = [];
  try {
    await env.FEEDBACK_KV.put(indexKey, JSON.stringify({ prefix, receivedAtUtc: receivedAt.toISOString() }), expiration);
    writtenKeys.push(indexKey);

    await env.FEEDBACK_KV.put(screenshotKey, await screenshot.arrayBuffer(), expiration);
    writtenKeys.push(screenshotKey);
    for (let index = 0; index < logChunkKeys.length; index += 1) {
      const start = index * LOG_CHUNK_BYTES;
      const chunk = await logs.slice(start, Math.min(start + LOG_CHUNK_BYTES, logs.size)).arrayBuffer();
      await env.FEEDBACK_KV.put(logChunkKeys[index], chunk, expiration);
      writtenKeys.push(logChunkKeys[index]);
    }

    // Metadata is the commit marker and is written only after every attachment succeeds.
    await env.FEEDBACK_KV.put(metadataKey, JSON.stringify(metadata, null, 2), expiration);
    writtenKeys.push(metadataKey);
  } catch (error) {
    const cleanupKeys = new Set([indexKey, metadataKey, screenshotKey, ...logChunkKeys, ...writtenKeys]);
    await Promise.allSettled([...cleanupKeys].map((key) => env.FEEDBACK_KV.delete(key)));
    console.error(`[feedback] failed to persist ${modContext.submissionId}: ${error}`);
    return jsonResponse(500, { error: 'storage_failed' });
  }

  return jsonResponse(200, { ok: true, id: modContext.submissionId, prefix });
}

export async function handleRequest(request, env, ctx = { waitUntil() {} }) {
  const path = new URL(request.url).pathname.replace(/\/+$/, '') || '/';
  if (path === '/feedback') return handleFeedback(request, env);
  if (path === '/') return handleTelemetry(request, env, ctx);
  return jsonResponse(404, { error: 'not_found' });
}

export default { fetch: handleRequest };
