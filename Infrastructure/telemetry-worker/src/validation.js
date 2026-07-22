import {
  JSON_MAX_BYTES,
  LOGS_MAX_BYTES,
  SCREENSHOT_MAX_BYTES,
  FeedbackValidationError,
} from './limits.js';

export const FEEDBACK_CATEGORIES = new Set(['bug', 'balance', 'feedback', 'translation']);
export const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const INSTALL_ID_PATTERN = /^[0-9a-f]{32}$/i;
const PNG_SIGNATURE = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const ZIP_SIGNATURES = [
  new Uint8Array([0x50, 0x4b, 0x03, 0x04]),
  new Uint8Array([0x50, 0x4b, 0x05, 0x06]),
  new Uint8Array([0x50, 0x4b, 0x07, 0x08]),
];

const TELEMETRY_BODY_FIELDS = new Set(['api_key', 'batch', 'sentAt']);
const TELEMETRY_EVENT_FIELDS = new Set(['event', 'distinct_id', 'properties', 'timestamp']);
const TELEMETRY_PROPERTY_FIELDS = new Set([
  'schema', 'applicant_id', 'request_id', 'category', 'payload',
  'anonymous_install_id', 'session_id', 'ritsulib_version', 'ritsulib_informational_version',
  'ritsulib_build_channel', 'ritsulib_build_configuration', 'owner_mod_id', 'applicant_display_name',
  'game_version', 'game_release_label', 'platform', 'os_name', 'os_version', 'process_architecture',
  'dotnet_runtime', 'game_language', 'payload_kind', 'json_indented', 'is_victory', 'is_abandoned',
  'occurred_at_utc', 'run_game_mode', 'run_is_daily', 'run_player_count', 'run_floor_reached',
  'run_ascension', 'run_time_seconds', 'run_win_time_seconds', 'run_reload_count', 'run_character_ids',
]);
const FEEDBACK_PAYLOAD_FIELDS = new Set([
  'description', 'category', 'gameVersion', 'commit', 'platformBranch', 'isModded', 'isFullConsole', 'lang',
]);
const FEEDBACK_CONTEXT_FIELDS = new Set([
  'submissionId', 'submittedAtUtc', 'modVersion', 'characterId', 'isDebugCharacter', 'seed',
  'currentActIndex', 'actId', 'actFloor', 'totalFloor', 'room', 'roomType', 'ascensionLevel',
  'gameMode', 'playerCount',
]);

function hasOnlyFields(value, allowed) {
  return Object.keys(value).every((key) => allowed.has(key));
}

function validateJsonComplexity(value) {
  const stack = [{ value, depth: 0 }];
  let nodes = 0;
  while (stack.length > 0) {
    const current = stack.pop();
    nodes += 1;
    if (nodes > 50000 || current.depth > 32) return false;
    if (typeof current.value === 'string' && current.value.length > 16384) return false;
    if (!current.value || typeof current.value !== 'object') continue;
    if (Array.isArray(current.value)) {
      if (current.value.length > 10000) return false;
      for (const item of current.value) stack.push({ value: item, depth: current.depth + 1 });
      continue;
    }
    const entries = Object.entries(current.value);
    if (entries.length > 2000) return false;
    for (const [key, item] of entries) {
      if (key.length > 128) return false;
      stack.push({ value: item, depth: current.depth + 1 });
    }
  }
  return true;
}

export function validateTelemetryBody(body, maximumBatchSize) {
  if (!body || typeof body !== 'object' || Array.isArray(body) || !hasOnlyFields(body, TELEMETRY_BODY_FIELDS)) {
    return 'Body contains unsupported fields';
  }
  if (body.api_key !== undefined && body.api_key !== 'proxy') return 'api_key is not allowed';
  if (!Array.isArray(body.batch) || body.batch.length === 0) return 'Missing or empty batch array';
  if (body.batch.length > maximumBatchSize) return `Max ${maximumBatchSize} events per batch`;
  if (body.sentAt !== undefined && (typeof body.sentAt !== 'string' || body.sentAt.length > 64)) {
    return 'sentAt is invalid';
  }

  for (let index = 0; index < body.batch.length; index += 1) {
    const event = body.batch[index];
    if (!event || typeof event !== 'object' || Array.isArray(event) || !hasOnlyFields(event, TELEMETRY_EVENT_FIELDS)) {
      return `batch[${index}] contains unsupported fields`;
    }
    if (event.event !== 'run_history.completed') return `batch[${index}].event is not allowed`;
    if (!event.properties || typeof event.properties !== 'object' || Array.isArray(event.properties)
      || !hasOnlyFields(event.properties, TELEMETRY_PROPERTY_FIELDS)) {
      return `batch[${index}].properties contains unsupported fields`;
    }
    const properties = event.properties;
    if (properties.schema !== 'ritsulib.telemetry.v1'
      || properties.applicant_id !== 'NinjaSlayer'
      || properties.owner_mod_id !== 'NinjaSlayer'
      || properties.request_id !== 'run_history'
      || properties.category !== 'RunHistory') {
      return `batch[${index}] is not a NinjaSlayer RunHistory envelope`;
    }
    if (!INSTALL_ID_PATTERN.test(properties.anonymous_install_id)
      || event.distinct_id !== properties.anonymous_install_id) {
      return `batch[${index}].distinct_id is invalid`;
    }
    if (!properties.payload || typeof properties.payload !== 'object' || Array.isArray(properties.payload)
      || !validateJsonComplexity(properties.payload)) {
      return `batch[${index}].properties.payload is invalid`;
    }
    if (event.timestamp !== undefined && event.timestamp !== null
      && (typeof event.timestamp !== 'string' || Number.isNaN(Date.parse(event.timestamp)))) {
      return `batch[${index}].timestamp is invalid`;
    }
  }
  return null;
}

export function parseJsonField(form, name, allowedFields) {
  const value = form.get(name);
  if (typeof value !== 'string' || new TextEncoder().encode(value).length > JSON_MAX_BYTES) {
    throw new FeedbackValidationError(`${name} must be JSON text no larger than ${JSON_MAX_BYTES} bytes`);
  }
  let parsed;
  try {
    parsed = JSON.parse(value);
  } catch {
    throw new FeedbackValidationError(`${name} is not a valid JSON object`);
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed) || !hasOnlyFields(parsed, allowedFields)) {
    throw new FeedbackValidationError(`${name} contains unsupported fields`);
  }
  return parsed;
}

function requireFile(form, name, type, maximumBytes) {
  const value = form.get(name);
  if (!(value instanceof File)) throw new FeedbackValidationError(`${name} is required`);
  if (value.type !== type) throw new FeedbackValidationError(`${name} must use Content-Type ${type}`);
  if (value.size > maximumBytes) throw new FeedbackValidationError(`${name} exceeds ${maximumBytes} bytes`, 413);
  return value;
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

export async function validateFeedbackForm(form, expectedSubmissionId = null) {
  const payload = parseJsonField(form, 'payload_json', FEEDBACK_PAYLOAD_FIELDS);
  const modContext = parseJsonField(form, 'mod_context', FEEDBACK_CONTEXT_FIELDS);
  if (!FEEDBACK_CATEGORIES.has(payload.category)) throw new FeedbackValidationError('Unsupported feedback category');
  if (typeof payload.description !== 'string' || payload.description.length === 0 || payload.description.length > 8000) {
    throw new FeedbackValidationError('Feedback description must contain 1 to 8000 characters');
  }
  if (typeof modContext.submissionId !== 'string' || !UUID_PATTERN.test(modContext.submissionId)) {
    throw new FeedbackValidationError('mod_context.submissionId must be a UUID');
  }
  if (expectedSubmissionId && expectedSubmissionId !== modContext.submissionId) {
    throw new FeedbackValidationError('Submission header does not match mod_context.submissionId');
  }
  if (typeof modContext.submittedAtUtc !== 'string' || Number.isNaN(Date.parse(modContext.submittedAtUtc))) {
    throw new FeedbackValidationError('mod_context.submittedAtUtc is invalid');
  }
  if (typeof modContext.modVersion !== 'string' || modContext.modVersion.length > 128) {
    throw new FeedbackValidationError('mod_context.modVersion is invalid');
  }

  const screenshot = requireFile(form, 'screenshot', 'image/png', SCREENSHOT_MAX_BYTES);
  const logs = requireFile(form, 'logs', 'application/zip', LOGS_MAX_BYTES);
  if (!(await hasFileSignature(screenshot, [PNG_SIGNATURE]))) {
    throw new FeedbackValidationError('screenshot does not have a valid PNG signature');
  }
  if (!(await hasFileSignature(logs, ZIP_SIGNATURES))) {
    throw new FeedbackValidationError('logs does not have a valid ZIP signature');
  }
  return { payload, modContext, screenshot, logs };
}
