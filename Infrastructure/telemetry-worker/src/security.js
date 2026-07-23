import {
  FEEDBACK_DAILY_BYTES,
  FEEDBACK_DAILY_COUNT,
  JSON_HEADER,
  TELEMETRY_DAILY_BYTES,
  jsonResponse,
} from './limits.js';

const DAILY_LIMITS = {
  telemetry: { count: Number.MAX_SAFE_INTEGER, bytes: TELEMETRY_DAILY_BYTES },
  feedback: { count: FEEDBACK_DAILY_COUNT, bytes: FEEDBACK_DAILY_BYTES },
};

export async function hashClientIp(request, env) {
  const ip = request.headers.get('CF-Connecting-IP');
  if (!ip || typeof env.RATE_LIMIT_SALT !== 'string' || env.RATE_LIMIT_SALT.length < 16) return null;

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

export async function enforceMinuteRateLimit(request, env, bindingName) {
  const limiter = env[bindingName];
  const clientKey = await hashClientIp(request, env);
  if (!limiter || typeof limiter.limit !== 'function' || !clientKey) {
    return { response: jsonResponse(503, { error: 'service_not_configured' }), clientKey: null };
  }

  const result = await limiter.limit({ key: clientKey });
  return result?.success
    ? { response: null, clientKey }
    : { response: jsonResponse(429, { error: 'rate_limited' }, { 'Retry-After': '60' }), clientKey };
}

export async function consumeDailyQuota(env, clientKey, category, bytes) {
  if (!env.ANONYMOUS_QUOTAS || !clientKey) return jsonResponse(503, { error: 'service_not_configured' });
  const id = env.ANONYMOUS_QUOTAS.idFromName(clientKey);
  const headers = {
    ...JSON_HEADER,
    'X-Quota-Category': category,
    'X-Quota-Bytes': String(bytes),
  };
  if (env.TEST_NOW) headers['X-Test-Now'] = String(env.TEST_NOW);
  return env.ANONYMOUS_QUOTAS.get(id).fetch(new Request('https://quota.internal/consume', {
    method: 'POST',
    headers,
  }));
}

export class AnonymousQuotaGuard {
  constructor(state) {
    this.storage = state.storage;
    this.pending = Promise.resolve();
  }

  fetch(request) {
    const operation = this.pending.then(() => this.consume(request));
    this.pending = operation.then(() => undefined, () => undefined);
    return operation;
  }

  async consume(request) {
    if (request.method !== 'POST') return jsonResponse(405, { error: 'method_not_allowed' });
    const category = request.headers.get('X-Quota-Category');
    const limits = DAILY_LIMITS[category];
    const bytes = Number.parseInt(request.headers.get('X-Quota-Bytes') || '-1', 10);
    if (!limits || !Number.isSafeInteger(bytes) || bytes < 0) {
      return jsonResponse(400, { error: 'invalid_quota_request' });
    }

    const now = new Date(request.headers.get('X-Test-Now') || Date.now());
    const day = now.toISOString().slice(0, 10);
    const key = `quota:${category}`;
    const previous = await this.storage.get(key);
    const usage = previous?.day === day ? previous : { day, count: 0, bytes: 0 };
    if (usage.count + 1 > limits.count || usage.bytes + bytes > limits.bytes) {
      const tomorrow = new Date(`${day}T00:00:00.000Z`);
      tomorrow.setUTCDate(tomorrow.getUTCDate() + 1);
      const retryAfter = Math.max(1, Math.ceil((tomorrow.getTime() - now.getTime()) / 1000));
      return jsonResponse(429, { error: 'daily_quota_exceeded' }, { 'Retry-After': String(retryAfter) });
    }

    await this.storage.put(key, { day, count: usage.count + 1, bytes: usage.bytes + bytes });
    return jsonResponse(200, { ok: true });
  }
}
