import {
  BodyTooLargeError,
  POSTHOG_HOST,
  REQUEST_TIMEOUT_MS,
  TELEMETRY_MAX_BATCH_SIZE,
  TELEMETRY_MAX_BODY_BYTES,
  JSON_HEADER,
  jsonResponse,
  readBodyLimited,
} from './limits.js';
import { FeedbackSubmissionCoordinator } from './feedback.js';
import { feedbackTombstoneKey } from './feedback-storage.js';
import { AnonymousQuotaGuard, consumeDailyQuota, enforceMinuteRateLimit } from './security.js';
import { UUID_PATTERN, validateTelemetryBody } from './validation.js';
import { handleObservatory } from './observatory.js';
import { acceptReplay, readPublicReplay } from './replays.js';

async function handleTelemetry(request, env, ctx) {
  if (request.method !== 'POST') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only POST is accepted' }, { Allow: 'POST' });
  }
  if (!env.POSTHOG_API_KEY || !env.ANONYMOUS_QUOTAS) {
    return jsonResponse(503, { error: 'service_not_configured' });
  }
  const rateLimit = await enforceMinuteRateLimit(request, env, 'TELEMETRY_RATE_LIMITER');
  if (rateLimit.response) return rateLimit.response;
  if (!(request.headers.get('content-type') || '').toLowerCase().includes('application/json')) {
    return jsonResponse(415, { error: 'unsupported_media_type', message: 'Content-Type must be application/json' });
  }
  const encoding = (request.headers.get('content-encoding') || 'identity').toLowerCase();
  if (!['identity', 'gzip'].includes(encoding)) return jsonResponse(415, { error: 'unsupported_content_encoding' });

  let bodyBytes;
  try {
    bodyBytes = await readBodyLimited(request, TELEMETRY_MAX_BODY_BYTES);
    if (encoding === 'gzip') {
      const decoded = new Blob([bodyBytes]).stream().pipeThrough(new DecompressionStream('gzip'));
      bodyBytes = await readBodyLimited(new Response(decoded), TELEMETRY_MAX_BODY_BYTES);
    }
  } catch (error) {
    if (error instanceof BodyTooLargeError) {
      console.warn(`[telemetry] payload_too_large: ${error.actualBytes} bytes; limit ${error.maximumBytes}`);
      return jsonResponse(413, { error: 'payload_too_large', maximumBytes: error.maximumBytes });
    }
    if (encoding === 'gzip') return jsonResponse(400, { error: 'invalid_gzip' });
    throw error;
  }
  let body;
  try {
    body = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bodyBytes));
  } catch {
    return jsonResponse(400, { error: 'invalid_json', message: 'Failed to parse body as JSON' });
  }
  const validationError = validateTelemetryBody(body, TELEMETRY_MAX_BATCH_SIZE);
  if (validationError) return jsonResponse(400, { error: 'invalid_format', message: validationError });

  const quotaResponse = await consumeDailyQuota(env, rateLimit.clientKey, 'telemetry', bodyBytes.byteLength);
  if (!quotaResponse.ok) return quotaResponse;

  for (const event of body.batch.filter(event => event.event === 'battle_report.completed')) {
    const response = await acceptReplay(event, env);
    if (!response.ok) return response;
  }
  const balanceEvents = body.batch.filter(event => event.event === 'run_history.completed');
  if (balanceEvents.length === 0) return jsonResponse(200, { ok: true, accepted: body.batch.length, rejected: 0 });

  const cleanBody = {
    api_key: env.POSTHOG_API_KEY,
    batch: await Promise.all(balanceEvents.map(async (event) => ({
      event: event.event,
      uuid: await telemetryEventId(event),
      properties: event.properties,
      distinct_id: event.distinct_id,
      ...(event.timestamp === undefined ? {} : { timestamp: event.timestamp }),
    }))),
    ...(body.sentAt === undefined ? {} : { sentAt: body.sentAt }),
  };
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

async function telemetryEventId(event) {
  // PostHog deduplicates capture UUIDs. A queue retry (including after restart)
  // must reuse the event's UUID, independent of HTTP encoding or batch position.
  const identity = [event.distinct_id, event.event, event.timestamp, event.properties.request_id, event.properties.payload];
  const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(identity)))).slice(0, 16);
  bytes[6] = (bytes[6] & 15) | 0x80; // UUIDv8: application-defined deterministic hash.
  bytes[8] = (bytes[8] & 63) | 0x80;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

async function handleFeedback(request, env) {
  if (request.method !== 'PUT') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only PUT is accepted for /feedback' }, { Allow: 'PUT' });
  }
  if (!env.FEEDBACK_KV || !env.RECORDS_BUCKET || !env.FEEDBACK_SUBMISSIONS || !env.ANONYMOUS_QUOTAS) {
    return jsonResponse(503, { error: 'service_not_configured' });
  }
  const rateLimit = await enforceMinuteRateLimit(request, env, 'FEEDBACK_RATE_LIMITER');
  if (rateLimit.response) return rateLimit.response;

  const submissionHeader = request.headers.get('X-NinjaSlayer-Submission-Id');
  if (!submissionHeader || !UUID_PATTERN.test(submissionHeader)) {
    return jsonResponse(400, { error: 'invalid_feedback', message: 'Submission header must be a UUID' });
  }
  if (await env.FEEDBACK_KV.get(feedbackTombstoneKey(submissionHeader))) {
    return jsonResponse(410, { error: 'submission_deleted' });
  }
  const id = env.FEEDBACK_SUBMISSIONS.idFromName(submissionHeader);
  const headers = {
    'Content-Type': request.headers.get('content-type') || '',
    'X-Client-Key': rateLimit.clientKey,
    'X-Submission-Id': submissionHeader,
  };
  if (env.TEST_NOW) headers['X-Test-Now'] = String(env.TEST_NOW);
  const forwarded = new Request('https://feedback.internal/', {
    method: 'PUT',
    headers,
    body: request.body,
    duplex: 'half',
  });
  return env.FEEDBACK_SUBMISSIONS.get(id).fetch(forwarded);
}

export async function handleRequest(request, env, ctx = { waitUntil() {} }) {
  const path = new URL(request.url).pathname.replace(/\/+$/, '') || '/';
  if (path === '/observatory/replays' || path.startsWith('/observatory/replays/')) {
    if (request.method !== 'GET') return readPublicReplay(request, env);
    const cached = await caches.default.match(request);
    if (cached) return cached;
    const response = await readPublicReplay(request, env);
    if (response.ok) ctx.waitUntil(caches.default.put(request, response.clone()));
    return response;
  }
  if (path.startsWith('/observatory/')) return handleObservatory(request, env);
  if (path === '/feedback') return handleFeedback(request, env);
  if (path === '/' || path === '/batch') return handleTelemetry(request, env, ctx);
  return jsonResponse(404, { error: 'not_found' });
}

export { AnonymousQuotaGuard, FeedbackSubmissionCoordinator };
export default { fetch: handleRequest };
