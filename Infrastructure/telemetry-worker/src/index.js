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

  let bodyBytes;
  try {
    bodyBytes = await readBodyLimited(request, TELEMETRY_MAX_BODY_BYTES);
  } catch (error) {
    if (error instanceof BodyTooLargeError) return jsonResponse(413, { error: 'payload_too_large' });
    throw error;
  }
  const quotaResponse = await consumeDailyQuota(env, rateLimit.clientKey, 'telemetry', bodyBytes.byteLength);
  if (!quotaResponse.ok) return quotaResponse;

  let body;
  try {
    body = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bodyBytes));
  } catch {
    return jsonResponse(400, { error: 'invalid_json', message: 'Failed to parse body as JSON' });
  }
  const validationError = validateTelemetryBody(body, TELEMETRY_MAX_BATCH_SIZE);
  if (validationError) return jsonResponse(400, { error: 'invalid_format', message: validationError });

  const cleanBody = {
    api_key: env.POSTHOG_API_KEY,
    batch: body.batch.map((event) => ({
      event: event.event,
      properties: event.properties,
      distinct_id: event.distinct_id,
      ...(event.timestamp === undefined ? {} : { timestamp: event.timestamp }),
    })),
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

async function handleFeedback(request, env) {
  if (request.method !== 'PUT') {
    return jsonResponse(405, { error: 'method_not_allowed', message: 'Only PUT is accepted for /feedback' }, { Allow: 'PUT' });
  }
  if (!env.FEEDBACK_KV || !env.FEEDBACK_SUBMISSIONS || !env.ANONYMOUS_QUOTAS) {
    return jsonResponse(503, { error: 'service_not_configured' });
  }
  const rateLimit = await enforceMinuteRateLimit(request, env, 'FEEDBACK_RATE_LIMITER');
  if (rateLimit.response) return rateLimit.response;

  const submissionHeader = request.headers.get('X-NinjaSlayer-Submission-Id');
  if (submissionHeader && !UUID_PATTERN.test(submissionHeader)) {
    return jsonResponse(400, { error: 'invalid_feedback', message: 'Submission header must be a UUID' });
  }
  if (submissionHeader && await env.FEEDBACK_KV.get(feedbackTombstoneKey(submissionHeader))) {
    return jsonResponse(410, { error: 'submission_deleted' });
  }
  const coordinatorName = submissionHeader ?? 'legacy-feedback-ingress';
  const id = env.FEEDBACK_SUBMISSIONS.idFromName(coordinatorName);
  const headers = {
    'Content-Type': request.headers.get('content-type') || '',
    'X-Client-Key': rateLimit.clientKey,
  };
  if (submissionHeader) headers['X-Submission-Id'] = submissionHeader;
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
  if (path === '/feedback') return handleFeedback(request, env);
  if (path === '/') return handleTelemetry(request, env, ctx);
  return jsonResponse(404, { error: 'not_found' });
}

export { AnonymousQuotaGuard, FeedbackSubmissionCoordinator };
export default { fetch: handleRequest };
