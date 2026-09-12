import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { get } from 'node:http';
import { readFile, readdir, mkdtemp, rm } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { normalizeEvents, parseData, summarize } from '../dashboard/data.mjs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createDashboardServer } from '../dashboard/server.mjs';
import { publicFeedback, publishSnapshot } from '../dashboard/publish.mjs';
import { summarizePublic } from '../dashboard/public-data.mjs';
import { loadTelemetry } from '../dashboard/posthog.mjs';

const cardA = 'CARD.NINJA_SLAYER_CARD_STRIKE_NINJA_SLAYER_REDESIGN_V1';
const cardB = 'CARD.NINJA_SLAYER_CARD_DEFEND_NINJA_SLAYER_REDESIGN_V1';
const character = 'CHARACTER.NINJA_SLAYER_CHARACTER_NINJA_SLAYER_CHARACTER';
const playerId = '76561198000000001';
const now = Date.parse('2026-09-12T12:00:00Z');
const catalog = [{ id: cardA, name: '打击' }, { id: cardB, name: '防御' }];

export function event({ seed = 'run-1', won = true, version = '0.2.4' } = {}) {
  return {
    event: 'run_history.completed', timestamp: '2026-09-12T10:00:00Z',
    properties: {
      applicant_id: 'NinjaSlayer', is_victory: won, is_abandoned: false, game_version: '0.107.1',
      run_game_mode: 'Standard',
      payload: {
        private_contributions: { NinjaSlayer: { ninja_slayer_balance_context: { version, balance_schema: 'ninja_slayer_run_history_v2' } } },
        applicant_payload: {
          run_history: { rng: { seed }, start_time: 100, ascension: 10, num_reloads: 0,
            players: [{ character_id: character, net_id: playerId, deck: [{ id: cardA }, { id: cardA, current_upgrade_level: 1 }] }],
            map_point_history: [[{ rooms: [{ room_type: 'monster', model_id: 'ENCOUNTER.TEST' }],
              player_stats: [{ player_id: playerId, card_choices: [{ card: { id: cardA }, was_picked: true }, { card: { id: cardB }, was_picked: false }],
                upgraded_cards: [cardA], cards_removed: [{ id: cardB }] }] }]],
          },
          mod_payload: { combats: { '1/0': { floor: 1, room_index: 0, encounter: 'ENCOUNTER.TEST', version, rounds: 3, won,
            players: [{ player_id: playerId, cards: { [cardA]: { drawn: 2, started: 3, finished: 3, manual_plays: 1, auto_plays: 1, energy_spent: 1, stars_spent: 0 } } }] } } },
        },
      },
    },
  };
}

test('native SerializableRun fields, event victory and UInt64 IDs determine the sample', () => {
  const raw = JSON.stringify(event()).replaceAll(`"${playerId}"`, playerId);
  const parsed = parseData(raw);
  assert.equal(parsed.properties.payload.applicant_payload.run_history.players[0].net_id, playerId);
  const normalized = normalizeEvents([parsed]);
  const summary = summarize(normalized.runs, catalog, {}, now);
  assert.equal(summary.runs, 1);
  assert.equal(summary.wins, 1);
  assert.equal(summary.cards[0].held, 1, 'duplicate copies and upgrades are one holding sample');
  assert.equal(summary.cards[0].picked, 1);
  assert.equal(summary.cards[0].pickFloorTotal, 1);
  assert.equal(summary.cards[0].upgraded, 1);
  assert.equal(summary.cards[1].removed, 1);
  assert.equal(summary.cards[1].skippedRuns, 1);
  assert.equal(summary.measuredCombats, 1);
  assert.equal(summary.totalCombats, 1);
  assert.equal(summary.cards[0].energy_spent, 1);
});

test('one multiplayer run is deduplicated; close UInt64 IDs keep separate choices', () => {
  const row = event(), history = row.properties.payload.applicant_payload.run_history;
  const other = '76561198000000002';
  history.players.push({ character_id: character, net_id: other, deck: [{ id: cardB }] });
  history.map_point_history[0][0].player_stats.push({ player_id: other, card_choices: [{ card: { id: cardB }, was_picked: true }] });
  const normalized = normalizeEvents({ batch: [row, structuredClone(row)] });
  assert.equal(normalized.duplicates, 1);
  const summary = summarize(normalized.runs, catalog, { party: 'multi' }, now);
  assert.equal(summary.runs, 1);
  assert.equal(summary.playerSamples, 2);
  assert.equal(summary.cards[1].picked, 1);
  assert.equal(summary.cards[1].offered, 2);
  assert.equal(summarize(normalized.runs, catalog, { party: 'solo' }, now).runs, 0);
});

test('conflicting outcomes and incomplete envelopes are not guessed', () => {
  const normalized = normalizeEvents([event(), event({ won: false }), null, {}, { properties: 'bad JSON' }]);
  assert.equal(normalized.runs.length, 0);
  assert.equal(normalized.conflicts, 1);
  assert.equal(normalized.rejected, 3);
  const row = event(); delete row.properties.is_victory;
  assert.equal(normalizeEvents([row]).rejected, 1);
  assert.throws(() => normalizeEvents(null));
});

test('old runs remain usable without invented combat measurements; filters retain denominators', () => {
  const older = event({ seed: 'old', won: false, version: '0.2.3' });
  delete older.properties.payload.applicant_payload.mod_payload;
  older.properties.payload.applicant_payload.run_history.num_reloads = 1;
  older.properties.payload.applicant_payload.run_history.ascension = 0;
  const { runs } = normalizeEvents([event(), older]);
  const all = summarize(runs, catalog, {}, now);
  assert.equal(all.runs, 2); assert.equal(all.measuredCombats, 1); assert.equal(all.totalCombats, 2);
  assert.equal(all.cards[0].drawn, 2);
  for (const filter of [{ version: '0.2.4' }, { reloads: 'none' }, { ascension: '10' }]) {
    const filtered = summarize(runs, catalog, filter, now);
    assert.equal(filtered.runs, 1); assert.equal(filtered.playerSamples, 1);
  }
  const empty = summarize(runs, catalog, { gameVersion: 'different' }, now);
  assert.equal(empty.averageFloor, null);
  assert.equal(empty.cards[0].combatSamples, 0);
});

test('combat data must match its floor, player and encounter', () => {
  const row = event(); row.properties.payload.applicant_payload.mod_payload.combats['1/0'].encounter = 'ENCOUNTER.WRONG';
  const normalized = normalizeEvents([row]);
  assert.equal(normalized.runs.length, 1);
  assert.equal(normalized.invalidCombats, 1);
  assert.equal(summarize(normalized.runs, catalog, {}, now).measuredCombats, 0);
});

test('PostHog pagination freezes its time boundary and keeps secrets out of failures', async () => {
  const calls = [], key = 'private-test-secret';
  const result = await loadTelemetry({ host: 'https://us.posthog.com', projectId: '42', key }, async (url, options) => {
    calls.push({ url, ...options, body: JSON.parse(options.body) });
    return new Response(JSON.stringify({ results: calls.length === 1 ? Array(1000).fill(['id', 'timestamp', '{}']) : [] }));
  });
  assert.equal(result.results.length, 1000); assert.equal(result.truncated, false);
  assert.match(calls[1].body.query.query, /uuid < 'id'/);
  assert.ok(!calls[1].body.query.query.includes('OFFSET'));
  assert.equal(calls[0].body.query.query.match(/timestamp < ('[^']+')/)[1], calls[1].body.query.query.match(/timestamp < ('[^']+')/)[1]);
  assert.equal(calls[0].headers.Authorization, `Bearer ${key}`);
  await assert.rejects(loadTelemetry({ host: 'https://us.posthog.com', projectId: '42', key }, async () => new Response(key, { status: 403 })), error => !error.message.includes(key));
  await assert.rejects(loadTelemetry({ host: 'https://evil.invalid', projectId: '42', key }));
});

test('local dashboard serves real catalog and imports; refuses cross-origin reads and never returns credentials', async t => {
  const server = await createDashboardServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => { server.closeAllConnections(); server.close(); });
  const url = `http://127.0.0.1:${server.address().port}`;
  const read = async () => (await fetch(`${url}/api/view`)).json();
  assert.equal((await read()).cards.length, 92);
  assert.equal((await read()).sources.telemetry.state, 'unconnected');
  assert.equal((await fetch(`${url}/api/view`, { headers: { Origin: 'https://evil.invalid' } })).status, 403);
  const rebindingStatus = await new Promise(resolve => get(`${url}/api/view`, { headers: { Host: 'evil.invalid' } }, response => {
    response.resume(); resolve(response.statusCode);
  }));
  assert.equal(rebindingStatus, 403);
  assert.equal((await fetch(`${url}/api/feedback/not-a-uuid/logs`)).status, 404);
  const secret = 'private-test-secret';
  await fetch(`${url}/api/connect`, { method: 'POST', body: JSON.stringify({ host: 'https://us.posthog.com', projectId: '42', key: secret }) });
  assert.ok(!JSON.stringify(await read()).includes(secret));
  const imported = await fetch(`${url}/api/import`, { method: 'POST', body: JSON.stringify([event()]) });
  assert.equal(imported.status, 200);
  assert.equal((await read()).runs, 1);
  assert.equal((await read()).sources.telemetry.label, '本机导入');
  assert.equal((await fetch(`${url}/api/import`, { method: 'POST', body: '[]' })).status, 400);
  assert.equal((await read()).runs, 1, 'a failed import must preserve the last usable view');
  const page = await fetch(url);
  assert.ok(page.headers.get('content-security-policy').includes("frame-ancestors 'none'"));
  assert.match(await page.text(), /卡池观察/);
});

test('dashboard accepts an exact native product fixture when supplied by host contracts', async t => {
  const path = process.env.NINJASLAYER_TELEMETRY_FIXTURE;
  if (!path) { t.skip('Requires the local host contract fixture.'); return; }
  const normalized = normalizeEvents(parseData(await readFile(path, 'utf8')));
  assert.equal(normalized.rejected, 0);
  assert.equal(normalized.runs.length, 1);
  assert.equal(summarize(normalized.runs, catalog).totalCombats, 2);
});


test('public feedback projection excludes old notices and private context', () => {
  const item = { id: 'id', at: '2026-09-12', description: '<script>hello</script>', category: 'bug', gameVersion: 'v',
    context: { publishDescription: true, modVersion: '0.2.6', seed: 'secret-seed', characterId: 'private', playerCount: 2 } };
  assert.deepEqual(publicFeedback([item, { ...item, context: { modVersion: 'old' } }]), [{
    id: 'id', at: '2026-09-12', description: '<script>hello</script>', category: 'bug', gameVersion: 'v', context: { modVersion: '0.2.6' }
  }]);
});

test('public aggregates match private statistics across date, version, mode and multiplayer filters', () => {
  const old = event({ seed: 'old', won: false, version: '0.2.3' });
  old.timestamp = '2026-09-11T23:30:00Z';
  old.properties.payload.applicant_payload.run_history.ascension = 0;
  old.properties.payload.applicant_payload.run_history.num_reloads = 2;
  old.properties.game_version = '0.111.0';
  old.properties.run_game_mode = 'Daily';
  const mixed = event({ seed: 'cross-version' });
  mixed.properties.payload.applicant_payload.mod_payload.combats['1/0'].version = '0.2.3';
  const multi = event({ seed: 'multiplayer' });
  multi.properties.payload.applicant_payload.run_history.players.push({ character_id: character, net_id: '76561198000000002', deck: [{ id: cardB }] });
  const telemetry = normalizeEvents([event(), old, mixed, multi]);
  const snapshot = { ...publishSnapshot(telemetry, catalog), sources: {}, feedback: [] };
  for (const filters of [{}, { days: '1' }, { days: '2' }, { version: '0.2.4' }, { version: '0.2.3' },
    { ascension: '0' }, { ascension: '10', reloads: 'none' }, { party: 'multi' }, { party: 'solo' }, { gameVersion: '0.111.0' }, { mode: 'Daily' }, { mode: 'empty' }]) {
    const actual = summarizePublic(snapshot, filters, now);
    const expected = summarize(telemetry.runs, snapshot.catalog, filters, now);
    for (const key of Object.keys(expected)) assert.deepEqual(actual[key], expected[key], `${JSON.stringify(filters)} / ${key}`);
  }
  const output = JSON.stringify(snapshot);
  for (const privateValue of [playerId, 'cross-version', 'ENCOUNTER.TEST', 'card_choices', 'net_id', 'start_time'])
    assert.ok(!output.includes(privateValue), privateValue);
});

test('Pages artifact is standalone under the project subpath and excludes private controls and data', async t => {
  const output = await mkdtemp(join(tmpdir(), 'ninjaslayer-pages-'));
  t.after(() => rm(output, { recursive: true, force: true }));
  const env = { ...process.env, POSTHOG_PERSONAL_API_KEY: '', POSTHOG_PROJECT_ID: '', OBSERVATORY_READ_TOKEN: '', OBSERVATORY_PREVIOUS_URL: '' };
  await promisify(execFile)(process.execPath, [fileURLToPath(new URL('../dashboard/build-pages.mjs', import.meta.url)), output], { env });
  assert.deepEqual((await readdir(output)).sort(), ['.nojekyll', 'app.js', 'assets', 'data.json', 'index.html', 'public-data.mjs', 'styles.css']);
  const html = await readFile(join(output, 'index.html'), 'utf8');
  assert.match(html, /data-view="pages"/);
  assert.match(html, /Content-Security-Policy/);
  assert.doesNotMatch(html, /connection-dialog|screenshot-link|logs-link|feedback-screenshot|\{\{view\}\}/);
  for (const [, local] of html.matchAll(/(?:src|href)="\.\/([^"]+)"/g)) await readFile(join(output, local));
  const snapshot = JSON.parse(await readFile(join(output, 'data.json'), 'utf8'));
  assert.equal(snapshot.catalog.length, 92);
  assert.equal(summarizePublic(snapshot).runs, 0);
  assert.equal(snapshot.sources.telemetry.state, 'unconnected');
  assert.deepEqual(snapshot.feedback, []);
});
