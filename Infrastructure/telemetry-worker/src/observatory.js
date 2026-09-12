import { jsonResponse } from './limits.js';
import { feedbackIndexKey, parseFeedbackIndexMarker, validateCompletedFeedbackMetadata } from './feedback-storage.js';

async function readFeedback(kv, id) {
  const marker = parseFeedbackIndexMarker(await kv.get(feedbackIndexKey(id)), id);
  if (!marker || marker.state !== 'completed') return null;
  const bytes = await kv.get(marker.completion.metadataKey, 'arrayBuffer');
  if (!bytes) throw new Error('Missing completed feedback metadata');
  const hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))]
    .map(value => value.toString(16).padStart(2, '0')).join('');
  const metadata = JSON.parse(new TextDecoder().decode(bytes));
  if (hash !== marker.completion.metadataSha256 || !validateCompletedFeedbackMetadata(metadata, marker))
    throw new Error('Invalid completed feedback metadata');
  return metadata;
}

export async function handleObservatory(request, env) {
  const url = new URL(request.url);
  // Export jobs and the private admin receive metadata; public artifacts use an explicit field projection.
  if (!env.OBSERVATORY_READ_TOKEN || request.headers.get('Authorization') !== `Bearer ${env.OBSERVATORY_READ_TOKEN}`)
    return jsonResponse(403, { error: 'forbidden' });
  if (request.method !== 'GET') return jsonResponse(405, { error: 'method_not_allowed' });
  if (url.pathname === '/observatory/feedback') {
    const feedback = [], warnings = [];
    const cursor = url.searchParams.get('cursor');
    const page = await env.FEEDBACK_KV.list({ prefix: 'feedback-index/', limit: 20, ...(cursor ? { cursor } : {}) });
    for (const key of page.keys) {
      const id = key.name.slice('feedback-index/'.length);
      try {
        const metadata = await readFeedback(env.FEEDBACK_KV, id);
        if (metadata) feedback.push({ id, at: metadata.receivedAtUtc, ...metadata.payload, context: metadata.modContext });
      } catch { warnings.push(id); }
    }
    // Keep each Worker invocation below KV subrequest limits. The export job follows this cursor.
    return jsonResponse(200, { feedback, warnings, cursor: page.list_complete ? undefined : page.cursor });
  }
  return jsonResponse(404, { error: 'not_found' });
}
