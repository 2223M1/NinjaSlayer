import { jsonResponse } from "./limits.js";
import { REPLAY_RETENTION_SECONDS, REPLAY_MAX_COMPRESSED } from "./replays.js";

// Leave room for KV administration, deletes and older feedback. Reservations are conservative:
// a failed write is not refunded, so retries can never overspend the free daily write budget.
export const FREE_KV_BYTES = 800 * 1024 * 1024;
export const FREE_KV_WRITES_PER_DAY = 800;
export const FREE_R2_BYTES = 8_000_000_000;
export const R2_CLEANUP_BYTES = 7_000_000_000;
export const FREE_R2_WRITES_PER_DAY = 2_000;
export const FREE_R2_READS_PER_DAY = 20_000;

export async function reserveStorage(env, charge) {
  const budget = env.ANONYMOUS_QUOTAS.get(
    env.ANONYMOUS_QUOTAS.idFromName("kv-free-budget-v1"),
  );
  return budget.fetch(
    new Request("https://quota.internal/storage", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(env.TEST_NOW ? { "X-Test-Now": env.TEST_NOW } : {}),
      },
      body: JSON.stringify(charge),
    }),
  );
}

export async function consumeStorage(storage, charge, now) {
  const { objects = [], reads = 0 } = charge;
  if (
    ![charge.bytes, charge.writes, charge.retentionDays, reads, ...objects.map(o => o.size)].every(
      (n) => Number.isSafeInteger(n) && n >= 0,
    ) ||
    charge.retentionDays > 180
  )
    return jsonResponse(400, { error: "invalid_storage_reservation" });
  const today = now.toISOString().slice(0, 10);
  const usage = (await storage.get("free-storage")) ?? {
    day: today,
    writes: 0,
    retained: [],
  };
  if (usage.day !== today) {
    usage.day = today;
    usage.writes = 0;
    usage.r2Writes = 0;
    usage.r2Reads = 0;
  }
  usage.retained = usage.retained.filter((item) => item.until > today);
  const bytes = usage.retained.reduce((sum, item) => sum + item.bytes, 0);
  const at = charge.at ?? now.toISOString();
  const expires = charge.expires ?? new Date(now.getTime() + charge.retentionDays * 86400000).toISOString();
  const records = {};
  let addedR2Bytes = 0;
  for (const object of objects) {
    const key = `r2-record:${at}:${object.key}`;
    const previous = await storage.get(key);
    const size = Math.max(previous?.size ?? 0, object.size);
    addedR2Bytes += size - (previous?.size ?? 0);
    records[key] = { ...object, size, expires, reservedAt: now.toISOString() };
  }
  const r2Bytes = usage.r2Bytes ?? 0;
  if (
    usage.writes + charge.writes > FREE_KV_WRITES_PER_DAY ||
    bytes + charge.bytes > FREE_KV_BYTES ||
    r2Bytes + addedR2Bytes > FREE_R2_BYTES ||
    (usage.r2Writes ?? 0) + objects.length > FREE_R2_WRITES_PER_DAY ||
    (usage.r2Reads ?? 0) + reads > FREE_R2_READS_PER_DAY
  )
    return jsonResponse(
      429,
      { error: "free_storage_budget_reached", retryable: true },
      { "Retry-After": "86400" },
    );
  const until = new Date(now.getTime() + (charge.retentionDays + 1) * 86400000)
    .toISOString()
    .slice(0, 10);
  if (charge.bytes) {
    let bucket = usage.retained.find((item) => item.until === until);
    if (!bucket) usage.retained.push((bucket = { until, bytes: 0 }));
    bucket.bytes += charge.bytes;
  }
  usage.writes += charge.writes;
  usage.r2Writes = (usage.r2Writes ?? 0) + objects.length;
  usage.r2Reads = (usage.r2Reads ?? 0) + reads;
  usage.r2Bytes = r2Bytes + addedR2Bytes;
  // Atomic multi-key put: no uploaded object can escape the capacity/cleanup ledger.
  await storage.put({ ...records, 'free-storage': usage });
  if (objects.length) {
    const alarm = await storage.getAlarm();
    const next = now.getTime() + (usage.r2Bytes >= R2_CLEANUP_BYTES ? 60000 : 86400000);
    if (alarm === null || alarm > next) await storage.setAlarm(next);
  }
  return jsonResponse(200, {
    ok: true,
    bytes: bytes + charge.bytes,
    writes: usage.writes,
    r2Bytes: r2Bytes + addedR2Bytes,
  });
}

export async function storeReport(storage, env, { id, host, report }, now) {
  const key = `${id}/${report.contributor}`;
  const receipt = await storage.get(`report:${key}`);
  const raw = JSON.stringify(report);
  const hash = [
    ...new Uint8Array(
      await crypto.subtle.digest("SHA-256", new TextEncoder().encode(raw)),
    ),
  ]
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
  if (receipt?.hash === hash || (receipt && receipt.reloads > report.reloads))
    return jsonResponse(200, {
      ok: true,
      id,
      contributor: report.contributor,
      idempotent: true,
    });
  if (receipt && now.getTime() - Date.parse(receipt.at) < 1100)
    return jsonResponse(
      429,
      { error: "report_update_pending" },
      { "Retry-After": "2" },
    );
  const expires =
    receipt?.expires ??
    new Date(now.getTime() + REPLAY_RETENTION_SECONDS * 1000).toISOString();
  if (Date.parse(expires) <= now.getTime())
    return jsonResponse(410, { error: "report_expired" });
  const metadata = {
    id,
    contributor: report.contributor,
    at: receipt?.at ?? now.toISOString(),
    expires,
    hash,
    version: report.version,
    gameVersion: host,
    won: report.won,
    ascension: report.ascension,
    party: report.party,
    mode: report.mode,
    reloads: report.reloads,
    duration: report.duration,
    coverage: report.coverage,
    rooms: report.floors.length,
  };
  const bytes = await new Response(
    new Blob([JSON.stringify({ ...report, id, game_version: host, expires })])
      .stream()
      .pipeThrough(new CompressionStream("gzip")),
  ).arrayBuffer();
  if (bytes.byteLength > REPLAY_MAX_COMPRESSED)
    return jsonResponse(413, { error: "compressed_report_too_large" });
  const reservation = await consumeStorage(
    storage,
    { bytes: 4096, writes: 1, retentionDays: 90, at: metadata.at, expires,
      objects: [{ key: `replays/${key}`, size: bytes.byteLength }] },
    now,
  );
  if (!reservation.ok) return reservation;
  const expiration = Math.floor(Date.parse(expires) / 1000);
  try {
    await env.RECORDS_BUCKET.put(`replays/${key}`, bytes, {
      httpMetadata: { contentType: "application/json", contentEncoding: "gzip" },
      customMetadata: { expires, hash },
    });
    await env.FEEDBACK_KV.put(`replay-index/${key}`, "1", {
      expiration,
      metadata,
    });
  } catch {
    return jsonResponse(
      503,
      { error: "report_storage_failed", retryable: true },
      { "Retry-After": "60" },
    );
  }
  await storage.put(`report:${key}`, metadata);
  const expiryDay = expires.slice(0, 10);
  const expiryKeys = (await storage.get(`report-expiry:${expiryDay}`)) ?? [];
  if (!expiryKeys.includes(key)) {
    expiryKeys.push(key);
    await storage.put(`report-expiry:${expiryDay}`, expiryKeys);
  }
  if (await storage.getAlarm() === null) await storage.setAlarm(now.getTime() + 86400000);
  return jsonResponse(200, {
    ok: true,
    id,
    contributor: report.contributor,
    expires,
  });
}

export async function expireReceipts(storage, env, now = new Date()) {
  const today = now.toISOString().slice(0, 10);
  let usage = await storage.get('free-storage');
  const pressure = (usage?.r2Bytes ?? 0) >= R2_CLEANUP_BYTES;
  const cursor = pressure ? undefined : await storage.get('r2-cleanup-cursor');
  // Eight objects, including feedback KV work, stay below the free Worker subrequest limit.
  const rows = await storage.list({ prefix: 'r2-record:', limit: 8, ...(cursor ? { startAfter: cursor } : {}) });
  for (const [key, record] of rows) {
    // Feedback puts run in another DO after reservation. Let its two-minute lease finish first.
    if (record.feedback && now.getTime() - Date.parse(record.reservedAt) <= 120000) continue;
    if (Date.parse(record.expires) > now.getTime() && usage.r2Bytes < R2_CLEANUP_BYTES) continue;
    const object = await env.RECORDS_BUCKET.head(record.key);
    if (object && !Number.isFinite(Date.parse(object.customMetadata.expires)))
      throw new Error('Stored R2 object is missing its expiration');
    // A failed report attempt may have been replaced and reserved under a later deadline.
    if (object && Date.parse(object.customMetadata.expires) <= Date.parse(record.expires)) {
      if (record.feedback) {
        const { id, metadataKey } = record.feedback;
        const indexKey = `feedback-index/${id}`;
        const marker = await env.FEEDBACK_KV.get(indexKey, 'json');
        if (marker?.lease.metadataKey === metadataKey) {
          const charge = await consumeStorage(storage, { bytes: 1024, writes: 1, retentionDays: 180 }, now);
          if (!charge.ok) continue;
          usage = await storage.get('free-storage');
          await env.FEEDBACK_KV.put(`feedback-tombstone/${id}`, JSON.stringify({ deletedAtUtc: now.toISOString() }),
            { expirationTtl: 180 * 86400 });
          await env.FEEDBACK_KV.delete(indexKey);
        }
        await env.FEEDBACK_KV.delete(metadataKey);
      } else if (record.key.startsWith('replays/')) {
        const id = record.key.slice('replays/'.length);
        await env.FEEDBACK_KV.delete(`replay-index/${id}`);
        const receipt = await storage.get(`report:${id}`);
        if (receipt) await storage.put(`report:${id}`, { ...receipt, expires: now.toISOString() });
      }
      await env.RECORDS_BUCKET.delete(record.key);
    }
    usage.r2Bytes -= record.size;
    // Refund and remove the ledger row together, only after successful deletion (or a missing object).
    await storage.transaction(async txn => {
      await txn.put('free-storage', usage);
      await txn.delete(key);
    });
  }
  if (rows.size === 8) await storage.put('r2-cleanup-cursor', [...rows.keys()].at(-1));
  else await storage.delete('r2-cleanup-cursor');
  const receipts = await storage.list({
    prefix: "report-expiry:",
    end: `report-expiry:${today}`,
    limit: 1,
  });
  for (const [key, ids] of receipts) {
    await env.RECORDS_BUCKET.delete(ids.map(id => `replays/${id}`));
    for (const id of ids) await storage.delete(`report:${id}`);
    await storage.delete(key);
  }
  await storage.setAlarm(now.getTime() + (rows.size === 8 || receipts.size ? 60000 : 86400000));
}
