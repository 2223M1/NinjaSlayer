import test from "node:test";
import assert from "node:assert/strict";
import { gzipSync } from "node:zlib";
import { MockR2 } from './support/r2.js';
import {
  decodeReplay,
  validateReport,
  publicRunId,
  readPublicReplay,
} from "../src/replays.js";
import {
  consumeStorage,
  storeReport,
  FREE_KV_BYTES,
  FREE_KV_WRITES_PER_DAY,
  FREE_R2_BYTES,
  R2_CLEANUP_BYTES,
  FREE_R2_READS_PER_DAY,
  FREE_R2_WRITES_PER_DAY,
  expireReceipts,
} from "../src/free-storage.js";
import { AnonymousQuotaGuard } from "../src/security.js";
import { handleRequest } from "../src/index.js";
import {
  charts,
  wilson,
  chartRows,
  selectGroups,
} from "../dashboard/charts.mjs";
import {
  chartBins,
  a10Cohorts,
  mechanismBins,
} from "../dashboard/chart-data.mjs";

const report = () => ({
  schema: "ninja_slayer_replay_v1",
  version: "0.2.7",
  won: true,
  ascension: 10,
  mode: "Standard",
  party: 1,
  contributor: 0,
  reloads: 0,
  duration: 600,
  coverage: "complete",
  floors: [
    {
      floor: 1,
      rooms: [{ type: "Monster", model: "ENCOUNTER.TEST" }],
      hp: 50,
      max_hp: 50,
      gold: 99,
      damage_taken: 0,
      healed: 0,
    },
  ],
  frames: [
    { kind: "combat_start", round: 0, side: "None" },
    {
      kind: "play",
      round: 1,
      side: "Player",
      actor: "self",
      model: "CARD.TEST",
      instance: 1,
      upgrade: 0,
      auto: false,
      repeat: 0,
      energy: 1,
      stars: 0,
    },
    { kind: "attempt_end", round: 1, side: "None" },
  ].map((action, sequence) => ({
    attempt: "a".repeat(32),
    floor: 1,
    room: 0,
    sequence,
    action,
  })),
});
const payload = (value) => ({
  run_key: "b".repeat(64),
  encoding: "gzip+base64",
  report: gzipSync(JSON.stringify(value)).toString("base64"),
});
class Storage {
  data = new Map();
  alarm = null;
  async get(key) {
    return structuredClone(this.data.get(key));
  }
  async put(key, value) {
    if (typeof key === 'object') for (const [name, item] of Object.entries(key)) this.data.set(name, structuredClone(item));
    else this.data.set(key, structuredClone(value));
  }
  async delete(key) {
    return this.data.delete(key);
  }
  async setAlarm(value) { this.alarm = value; }
  async getAlarm() { return this.alarm; }
  async transaction(callback) { return callback(this); }
  async list({ prefix, end, startAfter, limit = Infinity }) {
    return new Map([...this.data].filter(([key]) => key.startsWith(prefix) && (!end || key < end) && (!startAfter || key > startAfter))
      .sort(([a], [b]) => a.localeCompare(b)).slice(0, limit));
  }
}
class Kv {
  data = new Map();
  writes = 0;
  async put(key, value, options) {
    this.writes++;
    this.data.set(key, { value, metadata: options.metadata });
  }
  async delete(key) { this.data.delete(key); }
  async get(key, type) {
    const value = this.data.get(key)?.value ?? null;
    return value && type === 'json' ? JSON.parse(value) : value;
  }
}

function storageEnv(storage, kv = new Kv()) {
  const env = { FEEDBACK_KV: kv, RECORDS_BUCKET: new MockR2() };
  const guard = new AnonymousQuotaGuard({ storage }, env);
  env.ANONYMOUS_QUOTAS = { idFromName: name => name, get: () => guard };
  return env;
}

test("a failed index write retries the report without publishing a false receipt", async () => {
  const storage = new Storage();
  const kv = new Kv();
  const env = storageEnv(storage, kv);
  const put = kv.put.bind(kv);
  let failIndex = true;
  kv.put = async (key, value, options) => {
    if (key.startsWith("replay-index/") && failIndex)
      throw new Error("injected KV failure");
    await put(key, value, options);
  };
  const body = { id: "e".repeat(64), host: "0.107.1", report: report() };
  const key = `${body.id}/0`;
  const now = new Date();
  assert.equal(
    (await storeReport(storage, env, body, now)).status,
    503,
  );
  assert.equal(await storage.get(`report:${key}`), undefined);
  assert.equal(kv.data.has(`replay-index/${key}`), false);
  failIndex = false;
  assert.equal(
    (await storeReport(storage, env, body, now)).status,
    200,
  );
  assert.ok(await storage.get(`report:${key}`));
  assert.ok(kv.data.has(`replay-index/${key}`));
  const writes = kv.writes;
  assert.equal(
    (await storeReport(storage, env, body, now)).status,
    200,
  );
  assert.equal(kv.writes, writes);
  assert.equal((await storage.get("free-storage")).writes, 2);
});

test("public replay reads use the edge cache and never cache past expiration", async () => {
  const previous = globalThis.caches;
  const cached = new Map();
  globalThis.caches = {
    default: {
      async match(request) {
        return cached.get(request.url)?.clone();
      },
      async put(request, response) {
        cached.set(request.url, response);
      },
    },
  };
  try {
    const storage = new Storage();
    const kv = new Kv();
    const body = { id: "f".repeat(64), host: "0.107.1", report: report() };
    const env = storageEnv(storage, kv);
    await storeReport(storage, env, body, new Date());
    let reads = 0;
    const r2 = env.RECORDS_BUCKET;
    const get = r2.get.bind(r2);
    r2.get = async (...args) => {
      reads++;
      return get(...args);
    };
    const request = new Request(
      `https://public/observatory/replays/${body.id}/0`,
    );
    const pending = [];
    const ctx = {
      waitUntil(promise) {
        pending.push(promise);
      },
    };
    assert.equal((await handleRequest(request, env, ctx)).status, 200);
    await Promise.all(pending);
    assert.equal((await handleRequest(request, env, ctx)).status, 200);
    assert.equal(reads, 1);
    cached.clear();
    r2.objects.get(`replays/${body.id}/0`).customMetadata.expires = new Date(
      Date.now() + 20_000,
    ).toISOString();
    const expiring = await handleRequest(request, env, ctx);
    assert.match(expiring.headers.get("Cache-Control"), /max-age=(19|20)$/);
    await Promise.all(pending);
    cached.clear();
    r2.objects.get(`replays/${body.id}/0`).customMetadata.expires =
      "2000-01-01T00:00:00Z";
    assert.equal((await handleRequest(request, env, ctx)).status, 410);
    assert.equal(cached.size, 0);
    assert.equal(
      (
        await handleRequest(
          new Request(request.url, { method: "POST" }),
          env,
          ctx,
        )
      ).status,
      405,
    );
  } finally {
    globalThis.caches = previous;
  }
});

test("public replay uses an explicit schema and excludes identity, free text, paths and attachments", async () => {
  const original = report();
  assert.equal(validateReport(original), true);
  const decoded = await decodeReplay(payload(original));
  assert.deepEqual(decoded.report, original);
  for (const field of [
    "seed",
    "net_id",
    "anonymous_install_id",
    "screenshot",
    "logs",
    "name",
    "path",
  ]) {
    const invalid = report();
    invalid[field] = "private";
    assert.equal(validateReport(invalid), false, field);
    await assert.rejects(decodeReplay(payload(invalid)));
  }
  const action = report();
  action.frames[0].action.source = "C:/Users/Alice";
  assert.equal(validateReport(action), false);
  const peer = report();
  peer.frames[0].action.actor = "76561198000000001";
  assert.equal(validateReport(peer), false);
  const repeat = report();
  repeat.frames[1].action.repeat = 1;
  assert.equal(validateReport(repeat), false);
  repeat.frames[1].action.energy = 0;
  assert.equal(validateReport(repeat), true);
  const sequence = report();
  sequence.frames[0].sequence = 2;
  assert.equal(validateReport(sequence), false);
});

test("compressed replay has an expanded-size boundary and opaque salted run IDs", async () => {
  await assert.rejects(decodeReplay(payload("x".repeat(13 * 1024 * 1024))));
  const first = await publicRunId("b".repeat(64), "server-only-salt");
  assert.match(first, /^[a-f0-9]{64}$/);
  assert.notEqual(first, await publicRunId("b".repeat(64), "different-salt"));
});

test("retries are idempotent and full reports stay separate from summary storage", async () => {
  const storage = new Storage(),
    kv = new Kv(),
    env = storageEnv(storage, kv);
  const guard = new AnonymousQuotaGuard({ storage }, env);
  const body = { id: "c".repeat(64), host: "0.107.1", report: report() };
  const request = () =>
    new Request("https://internal/report", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  const replies = await Promise.all([
    guard.fetch(request()),
    guard.fetch(request()),
    guard.fetch(request()),
  ]);
  assert.ok(replies.every((response) => response.status === 200));
  assert.equal(kv.writes, 1);
  assert.equal(env.RECORDS_BUCKET.writes.length, 1);
  assert.equal(kv.data.has(`replays/${body.id}/0`), false);
  const object = env.RECORDS_BUCKET.objects.get(`replays/${body.id}/0`);
  assert.ok(object.value.byteLength > 0);
  assert.ok(
    Date.parse(object.customMetadata.expires) - Date.parse(kv.data.get(`replay-index/${body.id}/0`).metadata.at) ===
      90 * 86400000,
  );
  const raw = await new Response(
    new Blob([object.value])
      .stream()
      .pipeThrough(new DecompressionStream("gzip")),
  ).text();
  assert.ok(!raw.includes("run_key"));
  assert.ok(!raw.includes("server-only-salt"));
  const response = await readPublicReplay(
    new Request(`https://public/observatory/replays/${body.id}/0`),
    env,
  );
  assert.equal(response.status, 200);
  assert.equal(response.headers.get("Access-Control-Allow-Origin"), "*");
  object.customMetadata.expires = "2000-01-01T00:00:00Z";
  assert.equal(
    (
      await readPublicReplay(
        new Request(`https://public/observatory/replays/${body.id}/0`),
        env,
      )
    ).status,
    410,
  );
});

test("feedback and replay reservations share a free storage/write budget without replacing prior data", async () => {
  const storage = new Storage(),
    now = new Date("2030-01-01T00:00:00Z");
  assert.equal(
    (
      await consumeStorage(
        storage,
        {
          bytes: FREE_KV_BYTES - 100,
          writes: FREE_KV_WRITES_PER_DAY - 1,
          retentionDays: 180,
        },
        now,
      )
    ).status,
    200,
  );
  assert.equal(
    (
      await consumeStorage(
        storage,
        { bytes: 101, writes: 1, retentionDays: 90 },
        now,
      )
    ).status,
    429,
  );
  assert.equal(
    (
      await consumeStorage(
        storage,
        { bytes: 100, writes: 2, retentionDays: 90 },
        now,
      )
    ).status,
    429,
  );
  assert.equal(
    (
      await consumeStorage(
        storage,
        { bytes: 100, writes: 1, retentionDays: 90 },
        now,
      )
    ).status,
    200,
  );
  const previous = await storage.get("free-storage");
  assert.equal(
    (
      await consumeStorage(
        storage,
        { bytes: 1, writes: 1, retentionDays: 90 },
        new Date("2030-01-02T00:00:00Z"),
      )
    ).status,
    429,
  );
  assert.deepEqual(await storage.get("free-storage"), previous);
});

test('R2 capacity and operation limits reject before writes and survive day rollover', async () => {
  const storage = new Storage(), now = new Date('2030-01-01T00:00:00Z');
  const charge = { bytes: 0, writes: 0, retentionDays: 90,
    objects: [{ key: 'replays/capacity', size: FREE_R2_BYTES }] };
  assert.equal((await consumeStorage(storage, charge, now)).status, 200);
  const before = await storage.get('free-storage');
  assert.equal((await consumeStorage(storage, { ...charge,
    objects: [{ key: 'replays/overflow', size: 1 }] }, new Date('2030-01-02'))).status, 429);
  assert.deepEqual(await storage.get('free-storage'), before);
  assert.equal([...storage.data.keys()].some(key => key.endsWith('replays/overflow')), false);

  const operations = new Storage();
  await consumeStorage(operations, { bytes: 0, writes: 0, retentionDays: 90,
    reads: FREE_R2_READS_PER_DAY,
    objects: Array.from({ length: FREE_R2_WRITES_PER_DAY }, (_, i) => ({ key: `replays/${i}`, size: 1 })) }, now);
  assert.equal((await consumeStorage(operations, { bytes: 0, writes: 0, retentionDays: 0, reads: 1 }, now)).status, 429);
  assert.equal((await consumeStorage(operations, { ...charge, objects: [{ key: 'replays/extra', size: 1 }] }, now)).status, 429);
  assert.equal((await consumeStorage(operations, { bytes: 0, writes: 0, retentionDays: 0, reads: 1 }, new Date('2030-01-02'))).status, 200);
});

test('expired R2 capacity is refunded only after deletion succeeds; uploads do not postpone cleanup', async () => {
  const storage = new Storage(), env = storageEnv(storage);
  const now = new Date('2030-01-01T00:00:00Z');
  const charge = { bytes: 100, writes: 1, retentionDays: 90,
    objects: [{ key: 'replays/expired', size: FREE_R2_BYTES - 1 }] };
  await consumeStorage(storage, charge, now);
  await env.RECORDS_BUCKET.put('replays/expired', 'x', { customMetadata: { expires: '2030-04-01T00:00:00Z' } });
  const firstAlarm = storage.alarm;
  await consumeStorage(storage, { ...charge, objects: [{ key: 'replays/next', size: 1 }] }, new Date('2030-01-02'));
  assert.equal(storage.alarm, firstAlarm);
  const del = env.RECORDS_BUCKET.delete.bind(env.RECORDS_BUCKET);
  env.RECORDS_BUCKET.delete = async () => { throw new Error('storage unavailable'); };
  const later = new Date('2030-05-01T00:00:00Z');
  await assert.rejects(expireReceipts(storage, env, later), /unavailable/);
  assert.equal((await consumeStorage(storage, charge, later)).status, 429);
  assert.equal(env.RECORDS_BUCKET.objects.size, 1);
  env.RECORDS_BUCKET.delete = del;
  await expireReceipts(storage, env, later);
  assert.equal(env.RECORDS_BUCKET.objects.size, 0);
  assert.equal((await consumeStorage(storage, charge, later)).status, 200);
});

test('a depleted public read budget does not touch R2 or cache an error', async () => {
  const storage = new Storage(), env = storageEnv(storage);
  await consumeStorage(storage, { bytes: 0, writes: 0, retentionDays: 0, reads: FREE_R2_READS_PER_DAY }, new Date());
  env.RECORDS_BUCKET.get = () => { throw new Error('must not read'); };
  const response = await readPublicReplay(new Request(`https://public/observatory/replays/${'a'.repeat(64)}/0`), env);
  assert.equal(response.status, 429);
  assert.equal(response.headers.get('Cache-Control'), 'no-store');
});

test('capacity cleanup evicts oldest uploads first, preserves newer records and blocks replay resurrection', async () => {
  const storage = new Storage(), env = storageEnv(storage);
  const first = new Date('2030-01-01'), second = new Date('2030-01-02');
  const oldKey = 'replays/old/0', newKey = 'replays/new/0';
  const expires = '2030-04-01T00:00:00.000Z';
  await consumeStorage(storage, { bytes: 0, writes: 0, retentionDays: 90, expires,
    objects: [{ key: oldKey, size: 1_000_000_000 }] }, first);
  await consumeStorage(storage, { bytes: 0, writes: 0, retentionDays: 90, expires,
    objects: [{ key: newKey, size: R2_CLEANUP_BYTES - 1_000_000_000 }] }, second);
  for (const key of [oldKey, newKey]) {
    await env.RECORDS_BUCKET.put(key, 'report', { customMetadata: { expires } });
    await env.FEEDBACK_KV.put(key.replace('replays/', 'replay-index/'), '1', {});
    await storage.put(`report:${key.slice(8)}`, { expires });
  }
  await expireReceipts(storage, env, new Date('2030-01-03'));
  assert.equal(env.RECORDS_BUCKET.objects.has(oldKey), false);
  assert.equal(env.RECORDS_BUCKET.objects.has(newKey), true);
  assert.equal(env.FEEDBACK_KV.data.has('replay-index/old/0'), false);
  assert.equal(env.FEEDBACK_KV.data.has('replay-index/new/0'), true);
  assert.equal((await storage.get('free-storage')).r2Bytes, 6_000_000_000);
  assert.equal((await storage.get('report:old/0')).expires, '2030-01-03T00:00:00.000Z');
});

test('replay replacement keeps its original deadline and the expiry alarm deletes it', async () => {
  const storage = new Storage(), env = storageEnv(storage);
  const body = { id: 'd'.repeat(64), host: '0.107.1', report: report() };
  const first = await storeReport(storage, env, body, new Date('2030-01-01'));
  const deadline = (await first.json()).expires;
  body.report.reloads++;
  const second = await storeReport(storage, env, body, new Date('2030-02-01'));
  assert.equal((await second.json()).expires, deadline);
  await expireReceipts(storage, env, new Date('2030-04-02'));
  assert.equal(env.RECORDS_BUCKET.objects.size, 0);
  assert.equal(await storage.get(`report:${body.id}/0`), undefined);
});

const run = (win = true) => ({
  at: "2030-01-01T10:00:00Z",
  version: "0.2.7",
  gameVersion: "0.107.1",
  mode: "Standard",
  ascension: 10,
  win,
  floor: 1,
  playerCount: 1,
  reloads: 0,
  duration: 600,
  winTime: win ? 600 : 0,
  daily: null,
  actFloors: [1],
  floorMeasurements: {},
  players: [{ net_id: "private-player", deck: [{ id: "CARD.TEST" }] }],
  rooms: [
    {
      rooms: [
        { room_type: "monster", model_id: "ENCOUNTER.TEST", turns_taken: 3 },
      ],
      player_stats: [
        {
          player_id: "private-player",
          current_hp: 25,
          max_hp: 50,
          current_gold: 100,
          damage_taken: 15,
          hp_healed: 5,
        },
      ],
    },
  ],
  combats: [],
});
test("26 chart definitions and independent denominator/CI fixtures", () => {
  assert.equal(charts.length, 26);
  assert.equal(new Set(charts.map((chart) => chart.id)).size, 26);
  const bins = chartBins([run(), run(false), run()]);
  const rows = chartRows([{ charts: bins }], "winrate-by-ascension");
  assert.deepEqual(rows, [{ x: 10, y: null, n: 3, sum: 3, wins: 2 }]);
  assert.equal(bins.find((bin) => bin.chart === "encounter-damage").sum, 45); // Healing does not cancel damage.
  assert.equal(
    bins.some((bin) => bin.chart === "deck-growth"),
    false,
  ); // Never manufacture old deck measurements.
  const ci = wilson(2, 3);
  assert.ok(Math.abs(ci[0] - 0.20766) < 0.0001);
  assert.ok(Math.abs(ci[1] - 0.9385) < 0.0001);
  assert.equal(wilson(0, 0), null);
  assert.ok(!JSON.stringify(bins).includes("private-player"));
});
test("A10 grouping requires 20 valid runs and filters retain outcome/date/mode boundaries", () => {
  assert.equal(a10Cohorts(Array.from({ length: 19 }, () => run())).size, 0);
  const cohort = a10Cohorts([
    ...Array.from({ length: 10 }, () => run()),
    ...Array.from({ length: 10 }, () => run(false)),
  ]);
  assert.equal(cohort.get("private-player"), "50-60%");
  const snapshot = {
    groups: [
      {
        date: "2030-01-01",
        version: "0.2.7",
        gameVersion: "0.107.1",
        mode: "Standard",
        party: "solo",
        ascension: 10,
        noReloads: false,
        outcome: "loss",
        a10: "50-60%",
      },
    ],
  };
  assert.equal(selectGroups(snapshot, { outcome: "win" }).length, 0);
  assert.equal(
    selectGroups(snapshot, {
      outcome: "loss",
      reloads: "yes",
      from: "2030-01-01",
      to: "2030-01-01",
      a10: "50-60%",
    }).length,
    1,
  );
  assert.equal(selectGroups(snapshot, { reloads: "none" }).length, 0);
  assert.deepEqual(mechanismBins([run()]), []);
});

test("complete reports require each combat room and the final attempt, with coherent boundaries", () => {
  const missing = report();
  missing.frames = [];
  assert.equal(validateReport(missing), false);
  missing.coverage = "gapped";
  assert.equal(validateReport(missing), true);
  const sl = report();
  sl.frames.push(
    ...sl.frames.map((frame) => ({
      ...structuredClone(frame),
      attempt: "d".repeat(32),
    })),
  );
  sl.frames[2].action.kind = "attempt_interrupted";
  assert.equal(validateReport(sl), true);
  sl.frames.pop();
  assert.equal(validateReport(sl), false);
  sl.coverage = "gapped";
  assert.equal(validateReport(sl), true);
  const wrongRoom = report();
  wrongRoom.frames[1].room = 1;
  assert.equal(validateReport(wrongRoom), false);
  const doubleBoss = report();
  doubleBoss.floors[0].rooms.push({ type: "Boss", model: "ENCOUNTER.BOSS" });
  assert.equal(validateReport(doubleBoss), false);
  const empty = report();
  empty.floors = [];
  empty.frames = [];
  assert.equal(validateReport(empty), false);
});
