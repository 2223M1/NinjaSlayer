const POSTHOG_HOST = 'https://us.i.posthog.com';
const TELEMETRY_MAX_BODY_BYTES = 5 * 1024 * 1024;
const MAX_BATCH_SIZE = 1000;
const REQUEST_TIMEOUT_MS = 9000;
const FEEDBACK_MAX_BODY_BYTES = 64 * 1024 * 1024;
const SCREENSHOT_MAX_BYTES = 5 * 1024 * 1024;
const LOGS_MAX_BYTES = 55 * 1024 * 1024;
const JSON_MAX_BYTES = 64 * 1024;
const JSON_HEADER = { 'Content-Type': 'application/json' };
const FEEDBACK_CATEGORIES = new Set(['bug', 'balance', 'feedback', 'translation']);
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function jsonResponse(status, body, headers = {}) {
  return new Response(JSON.stringify(body), { status, headers: { ...JSON_HEADER, ...headers } });
}

function validateEvent(event, index) {
  if (!event || typeof event !== 'object') return `batch[${index}] is not an object`;
  if (typeof event.event !== 'string' || event.event.length === 0) return `batch[${index}].event is missing or empty`;
  if (event.properties !== undefined && typeof event.properties !== 'object') return `batch[${index}].properties must be an object`;
  if (event.timestamp !== undefined && event.timestamp !== null && typeof event.timestamp !== 'string') {
    return `batch[${index}].timestamp must be a string or null`;
  }
  return null;
}

function injectGeoIp(batch, clientIp) {
  if (!clientIp) return;
  for (const event of batch) {
    if (!event.properties) event.properties = {};
    if (!('$ip' in event.properties)) event.properties.$ip = clientIp;
  }
}

async function handleTelemetry(request, env, ctx) {
  if (request.method !== 'POST') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only POST is accepted' }, { Allow: 'POST' });
  }
  if (!(request.headers.get('content-type') || '').includes('application/json')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be application/json' });
  }
  const contentLength = Number.parseInt(request.headers.get('content-length') || '0', 10);
  if (contentLength > TELEMETRY_MAX_BODY_BYTES) {
    return jsonResponse(413, { error: 'payload_too_large', message: `Body exceeds ${TELEMETRY_MAX_BODY_BYTES} bytes` });
  }

  let body;
  try {
    body = await request.json();
  } catch {
    return jsonResponse(400, { error: 'invalid_json', message: 'Failed to parse body as JSON' });
  }
  if (!body || typeof body !== 'object') return jsonResponse(400, { error: 'invalid_format', message: 'Body must be a JSON object' });
  if (!Array.isArray(body.batch) || body.batch.length === 0) {
    return jsonResponse(400, { error: 'invalid_format', message: 'Missing or empty batch array' });
  }
  if (body.batch.length > MAX_BATCH_SIZE) {
    return jsonResponse(400, { error: 'batch_too_large', message: `Max ${MAX_BATCH_SIZE} events per batch, got ${body.batch.length}` });
  }
  for (let index = 0; index < body.batch.length; index += 1) {
    const error = validateEvent(body.batch[index], index);
    if (error) return jsonResponse(400, { error: 'invalid_format', message: error });
  }

  const clientIp = request.headers.get('CF-Connecting-IP') || '';
  injectGeoIp(body.batch, clientIp);
  const cleanBody = { api_key: env.POSTHOG_API_KEY, batch: body.batch };
  if (typeof body.historical_migration === 'boolean') cleanBody.historical_migration = body.historical_migration;
  if (typeof body.sentAt === 'string') cleanBody.sentAt = body.sentAt;

  const forwardHeaders = { ...JSON_HEADER };
  if (clientIp) forwardHeaders['X-Forwarded-For'] = clientIp;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  let postHogResponse;
  try {
    postHogResponse = await fetch(`${POSTHOG_HOST}/batch/`, {
      method: 'POST',
      headers: forwardHeaders,
      body: JSON.stringify(cleanBody),
      signal: controller.signal,
    });
  } catch (error) {
    return jsonResponse(502, { error: 'upstream_unreachable', message: error.message });
  } finally {
    clearTimeout(timer);
  }

  if (postHogResponse.ok) {
    ctx.waitUntil(Promise.resolve().then(() => console.log(`[ok] ${body.batch.length} events -> PostHog ${postHogResponse.status}`)));
    return jsonResponse(200, { ok: true, accepted: body.batch.length, rejected: 0 });
  }
  const upstreamBody = (await postHogResponse.text()).slice(0, 200);
  ctx.waitUntil(Promise.resolve().then(() => console.error(`[fail] PostHog ${postHogResponse.status}: ${upstreamBody}`)));
  return jsonResponse(502, {
    error: 'upstream_failed',
    message: `PostHog returned ${postHogResponse.status}`,
    upstream_body: upstreamBody,
  });
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

function requireFile(form, name, type, maxBytes) {
  const value = form.get(name);
  if (!(value instanceof File)) throw new FeedbackValidationError(`${name} is required`);
  if (value.type !== type) throw new FeedbackValidationError(`${name} must use Content-Type ${type}`);
  if (value.size > maxBytes) throw new FeedbackValidationError(`${name} exceeds ${maxBytes} bytes`, 413);
  return value;
}

function feedbackPrefix(modContext) {
  if (typeof modContext.submissionId !== 'string' || !UUID_PATTERN.test(modContext.submissionId)) {
    throw new FeedbackValidationError('mod_context.submissionId must be a UUID');
  }
  const submittedAt = new Date(modContext.submittedAtUtc);
  if (!Number.isFinite(submittedAt.getTime())) {
    throw new FeedbackValidationError('mod_context.submittedAtUtc must be an ISO timestamp');
  }
  const year = submittedAt.getUTCFullYear().toString().padStart(4, '0');
  const month = (submittedAt.getUTCMonth() + 1).toString().padStart(2, '0');
  const timestamp = submittedAt.toISOString().replace(/[:.]/g, '-');
  return `feedback/${year}/${month}/${timestamp}-${modContext.submissionId}`;
}

class FeedbackValidationError extends Error {
  constructor(message, status = 400) {
    super(message);
    this.status = status;
  }
}

async function handleFeedback(request, env) {
  if (request.method !== 'PUT') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only PUT is accepted for /feedback' }, { Allow: 'PUT' });
  }
  if (!env.FEEDBACK_BUCKET) return jsonResponse(503, { error: 'storage_unavailable' });
  const contentType = request.headers.get('content-type') || '';
  if (!contentType.toLowerCase().startsWith('multipart/form-data;')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be multipart/form-data' });
  }
  const contentLength = Number.parseInt(request.headers.get('content-length') || '0', 10);
  if (contentLength > FEEDBACK_MAX_BODY_BYTES) {
    return jsonResponse(413, { error: 'payload_too_large', message: `Body exceeds ${FEEDBACK_MAX_BODY_BYTES} bytes` });
  }

  let form;
  try {
    form = await request.formData();
  } catch {
    return jsonResponse(400, { error: 'invalid_multipart' });
  }

  let payload;
  let modContext;
  let screenshot;
  let logs;
  let prefix;
  try {
    payload = parseJsonField(form, 'payload_json');
    modContext = parseJsonField(form, 'mod_context');
    if (!FEEDBACK_CATEGORIES.has(payload.category)) throw new FeedbackValidationError('Unsupported feedback category');
    if (typeof payload.description !== 'string' || payload.description.length === 0 || payload.description.length > 8000) {
      throw new FeedbackValidationError('Feedback description must contain 1 to 8000 characters');
    }
    screenshot = requireFile(form, 'screenshot', 'image/png', SCREENSHOT_MAX_BYTES);
    logs = requireFile(form, 'logs', 'application/zip', LOGS_MAX_BYTES);
    prefix = feedbackPrefix(modContext);
  } catch (error) {
    if (error instanceof FeedbackValidationError) {
      return jsonResponse(error.status, { error: 'invalid_feedback', message: error.message });
    }
    throw error;
  }

  const keys = [`${prefix}/metadata.json`, `${prefix}/screenshot.png`, `${prefix}/logs.zip`];
  const metadata = {
    schemaVersion: 1,
    receivedAtUtc: new Date().toISOString(),
    payload,
    modContext,
  };
  try {
    await env.FEEDBACK_BUCKET.put(keys[1], screenshot.stream(), { httpMetadata: { contentType: 'image/png' } });
    await env.FEEDBACK_BUCKET.put(keys[2], logs.stream(), { httpMetadata: { contentType: 'application/zip' } });
    await env.FEEDBACK_BUCKET.put(keys[0], JSON.stringify(metadata, null, 2), { httpMetadata: { contentType: 'application/json' } });
  } catch (error) {
    await Promise.allSettled(keys.map((key) => env.FEEDBACK_BUCKET.delete(key)));
    console.error(`[feedback] failed to persist ${modContext.submissionId}: ${error}`);
    return jsonResponse(500, { error: 'storage_failed' });
  }

  return jsonResponse(200, { ok: true, id: modContext.submissionId, prefix });
}

export async function handleRequest(request, env, ctx = { waitUntil() {} }) {
  const path = new URL(request.url).pathname.replace(/\/+$/, '') || '/';
  if (path === '/feedback') return handleFeedback(request, env);
  return handleTelemetry(request, env, ctx);
}

export default { fetch: handleRequest };
