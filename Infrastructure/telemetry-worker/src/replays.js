import { BodyTooLargeError, jsonResponse, readBodyLimited } from "./limits.js";
import { reserveStorage } from "./free-storage.js";

export const REPLAY_RETENTION_SECONDS = 90 * 86400;
export const REPLAY_MAX_COMPRESSED = 2 * 1024 * 1024;
export const REPLAY_MAX_EXPANDED = 12 * 1024 * 1024;
const ID = /^[A-Z][A-Z0-9_]*\.[A-Z0-9_]+$/;
const KEY = /^[a-zA-Z0-9_.+-]{1,180}$/;
const HEX = /^[a-f0-9]{64}$/;
const TYPES = new Set([
  "combat_start",
  "combat_end",
  "attempt_end",
  "attempt_interrupted",
  "snapshot_end",
  "pile",
  "creature",
  "turn",
  "draw",
  "discard",
  "exhaust",
  "generate",
  "afflict",
  "play",
  "resolve",
  "hit",
  "block",
  "move",
  "potion",
  "channel",
  "hp_loss",
  "heal",
  "power",
  "power_snapshot",
  "shuffle",
  "naraku_absorbed",
  "naraku_gained",
  "karate_gained",
  "karate_lost",
  "chado_breath",
  "scry_discard",
  "shuriken_evoked",
  "shuriken_converted",
  "shuriken_stock",
]);
const ACTION_FIELDS = new Set([
  "kind",
  "round",
  "side",
  "actor",
  "target",
  "model",
  "source",
  "pile",
  "instance",
  "upgrade",
  "auto",
  "repeat",
  "energy",
  "stars",
  "amount",
  "value",
  "hp_loss",
  "blocked",
  "overkill",
  "killed",
  "hp",
  "max_hp",
  "vars",
]);
const REPORT_FIELDS = new Set([
  "schema",
  "version",
  "won",
  "ascension",
  "mode",
  "party",
  "contributor",
  "reloads",
  "duration",
  "coverage",
  "floors",
  "frames",
]);
const FLOOR_FIELDS = new Set([
  "floor",
  "rooms",
  "hp",
  "max_hp",
  "gold",
  "damage_taken",
  "healed",
  "cards_gained",
  "card_choices",
  "relic_choices",
  "potion_choices",
  "cards_removed",
  "upgraded",
  "transformed",
  "enchanted",
  "events",
  "rests",
  "bought_relics",
  "bought_potions",
  "potions_used",
]);
const only = (object, fields) =>
  object &&
  typeof object === "object" &&
  !Array.isArray(object) &&
  Object.keys(object).every((key) => fields.has(key));
const finite = (n) =>
  typeof n === "number" && Number.isFinite(n) && Math.abs(n) <= 1e9;
const integer = (n) => Number.isSafeInteger(n) && n >= 0 && n <= 1e7;
const entity = (n) => typeof n === "string" && ID.test(n);
const key = (n) => typeof n === "string" && KEY.test(n);
const array = (items, validate, max = 2000) =>
  Array.isArray(items) && items.length <= max && items.every(validate);
const actor = (value) =>
  value === "self" ||
  value === "uncollected" ||
  (typeof value === "string" &&
    /^[A-Z][A-Z0-9_]*\.[A-Z0-9_]+\/\d*$/.test(value));

function validAction(action) {
  if (
    !only(action, ACTION_FIELDS) ||
    !TYPES.has(action.kind) ||
    !integer(action.round) ||
    !["None", "Player", "Enemy"].includes(action.side)
  )
    return false;
  for (const [field, value] of Object.entries(action)) {
    if (["kind", "round", "side"].includes(field)) continue;
    if (["actor", "target"].includes(field)) {
      if (!actor(value)) return false;
    } else if (field === "model") {
      if (!entity(value)) return false;
    } else if (field === "source") {
      if (!key(value)) return false;
    } else if (field === "pile") {
      if (
        ![
          "None",
          "Hand",
          "Draw",
          "Discard",
          "Exhaust",
          "Play",
          "Deck",
        ].includes(value)
      )
        return false;
    } else if (["auto", "killed"].includes(field)) {
      if (typeof value !== "boolean") return false;
    } else if (field === "vars") {
      if (
        !value ||
        Array.isArray(value) ||
        Object.keys(value).length > 80 ||
        !Object.entries(value).every(([k, v]) => key(k) && finite(v))
      )
        return false;
    } else if (!finite(value)) return false;
  }
  if (
    action.kind === "play" &&
    (![
      action.repeat,
      action.energy,
      action.stars,
      action.instance,
      action.upgrade,
    ].every(integer) ||
      typeof action.auto !== "boolean" ||
      !entity(action.model) ||
      (action.repeat > 0 && (action.energy !== 0 || action.stars !== 0)))
  )
    return false;
  if (
    action.kind === "hit" &&
    ![action.hp_loss, action.blocked, action.overkill].every(integer)
  )
    return false;
  return true;
}

function validFloor(floor) {
  if (
    !only(floor, FLOOR_FIELDS) ||
    !integer(floor.floor) ||
    !["hp", "max_hp", "gold", "damage_taken", "healed"].every((field) =>
      finite(floor[field]),
    )
  )
    return false;
  if (
    !array(
      floor.rooms,
      (room) =>
        only(room, new Set(["type", "model"])) &&
        key(room.type) &&
        (room.model === null || entity(room.model)),
      20,
    )
  )
    return false;
  for (const [field, values] of Object.entries(floor)) {
    if (
      [
        "floor",
        "rooms",
        "hp",
        "max_hp",
        "gold",
        "damage_taken",
        "healed",
      ].includes(field)
    )
      continue;
    const validate =
      field === "cards_gained"
        ? (item) =>
            only(item, new Set(["id", "upgrade"])) &&
            entity(item.id) &&
            integer(item.upgrade)
        : ["card_choices", "relic_choices", "potion_choices"].includes(field)
          ? (item) =>
              only(item, new Set(["id", "upgrade", "picked"])) &&
              entity(item.id) &&
              typeof item.picked === "boolean" &&
              (item.upgrade === undefined || integer(item.upgrade))
          : field === "transformed"
            ? (item) =>
                only(item, new Set(["from", "to"])) &&
                entity(item.from) &&
                entity(item.to)
            : field === "enchanted"
              ? (item) =>
                  only(item, new Set(["card", "enchantment"])) &&
                  entity(item.card) &&
                  entity(item.enchantment)
              : ["events", "rests"].includes(field)
                ? key
                : entity;
    if (!array(values, validate)) return false;
  }
  return true;
}

export function validateReport(report) {
  if (
    !only(report, REPORT_FIELDS) ||
    report.schema !== "ninja_slayer_replay_v1" ||
    !key(report.version) ||
    typeof report.won !== "boolean" ||
    !integer(report.ascension) ||
    !key(report.mode) ||
    !integer(report.party) ||
    report.party < 1 ||
    report.party > 4 ||
    !integer(report.contributor) ||
    report.contributor >= report.party ||
    !integer(report.reloads) ||
    !integer(report.duration) ||
    !["complete", "gapped", "truncated"].includes(report.coverage) ||
    !array(report.floors, validFloor, 300) ||
    !array(
      report.frames,
      (frame) =>
        only(
          frame,
          new Set(["attempt", "floor", "room", "sequence", "action"]),
        ) &&
        /^[a-f0-9]{32}$/.test(frame.attempt) &&
        integer(frame.floor) &&
        integer(frame.room) &&
        integer(frame.sequence) &&
        validAction(frame.action),
      50000,
    )
  )
    return false;
  const attempts = new Map();
  for (const frame of report.frames) {
    const attempt = attempts.get(frame.attempt);
    if (
      frame.sequence !== (attempt?.next ?? 0) ||
      (attempt &&
        (attempt.floor !== frame.floor ||
          attempt.room !== frame.room ||
          ["attempt_end", "attempt_interrupted"].includes(attempt.last)))
    )
      return false;
    attempts.set(frame.attempt, {
      floor: frame.floor,
      room: frame.room,
      next: frame.sequence + 1,
      first: attempt?.first ?? frame.action.kind,
      last: frame.action.kind,
    });
  }
  if (
    new Set(report.floors.map((floor) => floor.floor)).size !==
    report.floors.length
  )
    return false;
  if (report.coverage === "complete") {
    if (
      !report.floors.length ||
      report.floors.some((floor, index) => floor.floor !== index + 1)
    )
      return false;
    const expected = new Set(
      report.floors.flatMap((floor) =>
        floor.rooms.flatMap((room, index) =>
          ["Monster", "Elite", "Boss"].includes(room.type)
            ? [`${floor.floor}/${index}`]
            : [],
        ),
      ),
    );
    const completed = new Set();
    for (const attempt of attempts.values()) {
      if (
        attempt.first !== "combat_start" ||
        !["attempt_end", "attempt_interrupted"].includes(attempt.last)
      )
        return false;
      const room = `${attempt.floor}/${attempt.room}`;
      if (attempt.last === "attempt_end") completed.add(room);
      else completed.delete(room);
    }
    if (
      expected.size !== completed.size ||
      [...expected].some((room) => !completed.has(room))
    )
      return false;
  }
  return true;
}

export async function decodeReplay(payload) {
  if (
    !only(payload, new Set(["run_key", "encoding", "report"])) ||
    !HEX.test(payload.run_key) ||
    payload.encoding !== "gzip+base64" ||
    typeof payload.report !== "string" ||
    payload.report.length > Math.ceil(REPLAY_MAX_COMPRESSED / 3) * 4 ||
    !/^[A-Za-z0-9+/]*={0,2}$/.test(payload.report)
  )
    throw new Error("invalid_replay_envelope");
  const compressed = Uint8Array.from(atob(payload.report), (c) =>
    c.charCodeAt(0),
  );
  const stream = new Blob([compressed])
    .stream()
    .pipeThrough(new DecompressionStream("gzip"));
  const bytes = await readBodyLimited(
    new Response(stream),
    REPLAY_MAX_EXPANDED,
  );
  const report = JSON.parse(
    new TextDecoder("utf8", { fatal: true }).decode(bytes),
  );
  if (!validateReport(report)) throw new Error("invalid_public_report");
  return { report, bytes };
}

export async function publicRunId(runKey, salt) {
  const encoder = new TextEncoder();
  const secret = await crypto.subtle.importKey(
    "raw",
    encoder.encode(salt),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  return [
    ...new Uint8Array(
      await crypto.subtle.sign(
        "HMAC",
        secret,
        encoder.encode("replay/" + runKey),
      ),
    ),
  ]
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

export async function acceptReplay(event, env) {
  if (!env.FEEDBACK_KV || !env.RECORDS_BUCKET)
    return jsonResponse(503, { error: "service_not_configured" });
  let decoded;
  try {
    decoded = await decodeReplay(event.properties.payload.applicant_payload);
  } catch (error) {
    return jsonResponse(error instanceof BodyTooLargeError ? 413 : 400, {
      error: "invalid_replay",
    });
  }
  const id = await publicRunId(
    event.properties.payload.applicant_payload.run_key,
    env.RATE_LIMIT_SALT,
  );
  const host = event.properties.game_version;
  if (!key(host)) return jsonResponse(400, { error: "invalid_host_version" });
  const coordinator = env.ANONYMOUS_QUOTAS.get(
    env.ANONYMOUS_QUOTAS.idFromName("kv-free-budget-v1"),
  );
  return coordinator.fetch(
    new Request("https://quota.internal/report", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(env.TEST_NOW ? { "X-Test-Now": env.TEST_NOW } : {}),
      },
      body: JSON.stringify({ id, host, report: decoded.report }),
    }),
  );
}

const PUBLIC_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Cache-Control": "public, max-age=300",
  "X-Content-Type-Options": "nosniff",
};
export async function readPublicReplay(request, env) {
  if (request.method !== "GET")
    return jsonResponse(405, { error: "method_not_allowed" }, PUBLIC_HEADERS);
  if (!env.FEEDBACK_KV || !env.RECORDS_BUCKET || !env.ANONYMOUS_QUOTAS)
    return jsonResponse(503, { error: "service_not_configured" }, PUBLIC_HEADERS);
  const url = new URL(request.url);
  if (url.pathname === "/observatory/replays") {
    const cursor = url.searchParams.get("cursor");
    const page = await env.FEEDBACK_KV.list({
      prefix: "replay-index/",
      limit: 100,
      ...(cursor ? { cursor } : {}),
    });
    const now = Date.now();
    return jsonResponse(
      200,
      {
        reports: page.keys
          .map((k) => k.metadata)
          .filter((m) => m && Date.parse(m.expires) > now),
        cursor: page.list_complete ? undefined : page.cursor,
      },
      PUBLIC_HEADERS,
    );
  }
  const match = /^\/observatory\/replays\/([a-f0-9]{64})\/([0-3])$/.exec(
    url.pathname,
  );
  if (!match) return jsonResponse(404, { error: "not_found" }, PUBLIC_HEADERS);
  const budget = await reserveStorage(env, { bytes: 0, writes: 0, reads: 1, retentionDays: 0 });
  if (!budget.ok) {
    budget.headers.set("Access-Control-Allow-Origin", "*");
    budget.headers.set("Cache-Control", "no-store");
    return budget;
  }
  const object = await env.RECORDS_BUCKET.get(`replays/${match[1]}/${match[2]}`);
  if (!object || Date.parse(object.customMetadata.expires) <= Date.now())
    return jsonResponse(
      410,
      { error: "report_expired_or_missing" },
      PUBLIC_HEADERS,
    );
  // R2 stores gzip; stream JSON to the HTTP cache and let the edge negotiate transfer encoding.
  const ttl = Math.max(
    0,
    Math.min(
      300,
      Math.floor((Date.parse(object.customMetadata.expires) - Date.now()) / 1000),
    ),
  );
  return new Response(object.body.pipeThrough(new DecompressionStream("gzip")), {
    headers: {
      ...PUBLIC_HEADERS,
      "Cache-Control": `public, max-age=${ttl}`,
      "Content-Type": "application/json",
      ETag: object.customMetadata.hash,
    },
  });
}
