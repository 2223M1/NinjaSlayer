import { jsonResponse } from "./limits.js";
import { REPLAY_RETENTION_SECONDS, REPLAY_MAX_COMPRESSED } from "./replays.js";

// Leave room for KV administration, deletes and older feedback. Reservations are conservative:
// a failed write is not refunded, so retries can never overspend the free daily write budget.
export const FREE_KV_BYTES = 800 * 1024 * 1024;
export const FREE_KV_WRITES_PER_DAY = 800;

export async function reserveStorage(env, bytes, writes, retentionDays) {
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
      body: JSON.stringify({ bytes, writes, retentionDays }),
    }),
  );
}

export async function consumeStorage(storage, charge, now) {
  if (
    ![charge.bytes, charge.writes, charge.retentionDays].every(
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
  }
  usage.retained = usage.retained.filter((item) => item.until > today);
  const bytes = usage.retained.reduce((sum, item) => sum + item.bytes, 0);
  if (
    usage.writes + charge.writes > FREE_KV_WRITES_PER_DAY ||
    bytes + charge.bytes > FREE_KV_BYTES
  )
    return jsonResponse(
      429,
      { error: "free_storage_budget_reached", retryable: true },
      { "Retry-After": "86400" },
    );
  const until = new Date(now.getTime() + (charge.retentionDays + 1) * 86400000)
    .toISOString()
    .slice(0, 10);
  let bucket = usage.retained.find((item) => item.until === until);
  if (!bucket) usage.retained.push((bucket = { until, bytes: 0 }));
  bucket.bytes += charge.bytes;
  usage.writes += charge.writes;
  await storage.put("free-storage", usage);
  return jsonResponse(200, {
    ok: true,
    bytes: bytes + charge.bytes,
    writes: usage.writes,
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
    { bytes: bytes.byteLength + 4096, writes: 2, retentionDays: 90 },
    now,
  );
  if (!reservation.ok) return reservation;
  const expiration = Math.floor(Date.parse(expires) / 1000);
  try {
    await env.FEEDBACK_KV.put(`replays/${key}`, bytes, {
      expiration,
      metadata,
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
  if (storage.setAlarm) await storage.setAlarm(now.getTime() + 86400000);
  return jsonResponse(200, {
    ok: true,
    id,
    contributor: report.contributor,
    expires,
  });
}

export async function expireReceipts(storage) {
  const today = new Date().toISOString().slice(0, 10);
  for (const [key, ids] of await storage.list({
    prefix: "report-expiry:",
    end: `report-expiry:${today}`,
  })) {
    for (const id of ids) await storage.delete(`report:${id}`);
    await storage.delete(key);
  }
  if (storage.setAlarm) await storage.setAlarm(Date.now() + 86400000);
}
