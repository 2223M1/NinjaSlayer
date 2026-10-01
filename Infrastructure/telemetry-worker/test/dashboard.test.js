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
import { chartSchemaVersion, publicFeedback, publishSnapshot } from '../dashboard/publish.mjs';
import { summarizePublic, useCurrentCatalog } from '../dashboard/public-data.mjs';
import { loadTelemetry } from '../dashboard/posthog.mjs';

const cardA = 'CARD.NINJA_SLAYER_CARD_STRIKE_NINJA_SLAYER';
const cardB = 'CARD.NINJA_SLAYER_CARD_DEFEND_NINJA_SLAYER';
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

test('current catalog excludes vanilla, archived and unknown cards across every public statistic', () => {
  const row = event();
  const payload = row.properties.payload.applicant_payload;
  const foreign = ['CARD.ANOINTED', 'CARD.NINJA_SLAYER_CARD_GUARD_STANCE', 'CARD.UNKNOWN'];
  const history = payload.run_history;
  for (const id of foreign) {
    history.players[0].deck.push({ id });
    const stats = history.map_point_history[0][0].player_stats[0];
    stats.card_choices.push({ card: { id }, was_picked: true });
    stats.cards_removed.push({ id });
    stats.upgraded_cards.push(id);
    payload.mod_payload.combats['1/0'].players[0].cards[id] = { drawn: 2, started: 1, finished: 1, manual_plays: 1, auto_plays: 0, energy_spent: 1, stars_spent: 0 };
  }
  const latest = [{ ...catalog[0], name: '新版打击', thumbnail: 'images/new.webp' }, catalog[1]];
  const telemetry = normalizeEvents([row]);
  const snapshot = publishSnapshot(telemetry, latest);
  const totals = summarizePublic(snapshot, {}, now);
  assert.deepEqual(snapshot.catalog, latest);
  assert.deepEqual(totals.cards.map(c => c.id), [cardA, cardB]);
  assert.equal(totals.cards[0].name, '新版打击');
  assert.equal(totals.cards[0].picked, 1);
  assert.equal(totals.cards[0].energy_spent, 1);
  assert.equal(totals.runs, 1);
  assert.equal(totals.wins, 1);
  assert.equal(totals.playerSamples, 1);
  assert.equal(totals.totalCombats, 1);
  for (const id of foreign) assert.ok(!JSON.stringify(snapshot).includes(id));
});

test('last successful snapshot is pruned against a new catalog without losing run denominators', () => {
  const snapshot = publishSnapshot(normalizeEvents([event()]), catalog);
  const group = snapshot.groups[0];
  const removed = cardB;
  group.mechanisms.push({ group: 'card', id: removed + '/1', n: 1, version: '0.2.4' },
    { group: 'damage_source', id: removed, n: 1, sum: 8, version: '0.2.4' },
    { group: 'mechanic', id: 'shuriken', n: 1, sum: 6, version: '0.2.4' });
  group.combats[0].cards.push({ ...group.combats[0].cards[0], id: removed });
  const latest = [{ ...catalog[0], name: '最新名称' }];
  const pruned = useCurrentCatalog(snapshot, latest);
  assert.equal(pruned.catalog[0].name, '最新名称');
  assert.ok(!JSON.stringify(pruned).includes(removed));
  assert.ok(JSON.stringify(pruned).includes('shuriken'));
  const summary = summarizePublic(pruned, { version: '0.2.4' }, now);
  assert.equal(summary.runs, 1);
  assert.equal(summary.playerSamples, 1);
  assert.equal(summary.measuredCombats, 1);
  assert.equal(summary.cards[0].picked, 1);
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
  for (const filter of [{ version: '0.2.4' }, { outcome: 'win' }, { ascension: '10' }]) {
    const filtered = summarize(runs, catalog, filter, now);
    assert.equal(filtered.runs, 1); assert.equal(filtered.playerSamples, 1);
  }
  const empty = summarize(runs, catalog, { version: 'different' }, now);
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
  assert.equal((await read()).cards.length, 93);
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
  assert.match(await page.text(), /忍杀情报站/);
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
    id: 'id', at: '2026-09-12', description: '<script>hello</script>', category: 'bug', gameVersion: 'v', context: { modVersion: '0.2.6' },
    review: { status: 'unresolved', reply: '', updatedAt: null }
  }]);
});

test('public aggregates match private statistics across date, version, ascension, outcome and multiplayer filters', () => {
  const old = event({ seed: 'old', won: false, version: '0.2.3' });
  old.timestamp = '2026-09-11T23:30:00Z';
  old.properties.payload.applicant_payload.run_history.ascension = 0;
  const mixed = event({ seed: 'cross-version' });
  mixed.properties.payload.applicant_payload.mod_payload.combats['1/0'].version = '0.2.3';
  const multi = event({ seed: 'multiplayer' });
  multi.properties.payload.applicant_payload.run_history.players.push({ character_id: character, net_id: '76561198000000002', deck: [{ id: cardB }] });
  const telemetry = normalizeEvents([event(), old, mixed, multi]);
  const snapshot = { ...publishSnapshot(telemetry, catalog), sources: {}, feedback: [] };
  for (const filters of [{}, { days: '1' }, { days: '2' }, { version: '0.2.4' }, { version: '0.2.3' },
    { ascension: '0' }, { ascension: '10', outcome: 'win' }, { party: 'multi' }, { party: 'solo' }, { outcome: 'loss' }]) {
    const actual = summarizePublic(snapshot, filters, now);
    const expected = summarize(telemetry.runs, snapshot.catalog, filters, now);
    for (const key of Object.keys(expected)) assert.deepEqual(actual[key], expected[key], `${JSON.stringify(filters)} / ${key}`);
  }
  const output = JSON.stringify(snapshot);
  for (const privateValue of [playerId, 'cross-version', 'card_choices', 'net_id', 'start_time'])
    assert.ok(!output.includes(privateValue), privateValue);
});

async function buildPages(t, { previous, failTelemetry = false, rows = [], feedbackToken = '', failFeedback = false, manualFeedback = false } = {}) {
  const output = await mkdtemp(join(tmpdir(), 'ninjaslayer-pages-'));
  t.after(() => rm(output, { recursive: true, force: true }));
  const env = { ...process.env, POSTHOG_PERSONAL_API_KEY: 'test-only', POSTHOG_PROJECT_ID: '42', POSTHOG_QUERY_HOST: 'https://us.posthog.com',
    OBSERVATORY_READ_TOKEN: feedbackToken, OBSERVATORY_REQUIRE_FRESH_FEEDBACK: String(manualFeedback), OBSERVATORY_PREVIOUS_URL: previous ? 'https://previous.invalid/data.json' : '' };
  const mock = `globalThis.fetch = async url => {
    if (String(url).startsWith('https://previous.invalid/')) return Response.json(${JSON.stringify(previous ?? {})});
    if (String(url).startsWith('https://us.posthog.com/')) return ${failTelemetry ? "new Response('unavailable', {status: 503})" : `Response.json({results: ${JSON.stringify(rows)}})`};
    if (String(url).startsWith('https://telemetry.feixingwawa.cn/observatory/replays')) return Response.json({reports: []});
    if (String(url).startsWith('https://telemetry.feixingwawa.cn/observatory/feedback')) return ${failFeedback ? "new Response('unavailable', {status: 503})" : "Response.json({feedback: [], warnings: []})"};
    throw new Error('Unexpected fetch: ' + url);
  };`;
  await promisify(execFile)(process.execPath, ['--import', 'data:text/javascript,' + encodeURIComponent(mock),
    fileURLToPath(new URL('../dashboard/build-pages.mjs', import.meta.url)), output], { env });
  return output;
}

test('scheduled feedback failure retains the last complete snapshot; manual sync must fetch fresh data', async t => {
  const previous = { schemaVersion: 1, ...publishSnapshot(normalizeEvents([]), catalog),
    feedback: [{ id: 'old-public', at: new Date().toISOString(), description: 'keep me', review: { status: 'resolved', reply: 'saved reply', updatedAt: new Date().toISOString() } }],
    sources: { telemetry: { state: 'ready', at: new Date().toISOString() }, feedback: { state: 'ready', at: new Date().toISOString() } } };
  const args = { previous, feedbackToken: 'test-only', failFeedback: true };
  const output = await buildPages(t, args);
  const snapshot = JSON.parse(await readFile(join(output, 'data.json'), 'utf8'));
  assert.deepEqual(snapshot.feedback, previous.feedback);
  assert.equal(snapshot.sources.feedback.state, 'error');
  await assert.rejects(buildPages(t, { ...args, manualFeedback: true }));
  await assert.rejects(buildPages(t, { previous, manualFeedback: true }));
  const fresh = await buildPages(t, { previous, feedbackToken: 'test-only', manualFeedback: true });
  assert.deepEqual(JSON.parse(await readFile(join(fresh, 'data.json'), 'utf8')).feedback, []);
});

test('Pages artifact is standalone under the project subpath and accepts genuinely empty fresh telemetry', async t => {
  const output = await buildPages(t);
  assert.deepEqual((await readdir(output)).sort(), ['.nojekyll', 'app.js', 'assets', 'catalog-view.mjs', 'chart-view.mjs', 'charts.mjs', 'content', 'data.json', 'i18n.mjs', 'index.html', 'public-data.mjs', 'replay-view.mjs', 'site-copy.mjs', 'styles.css', 'translations.mjs', 'vendor']);
  const html = await readFile(join(output, 'index.html'), 'utf8');
  assert.match(html, /data-view="pages"/);
  assert.match(html, /Content-Security-Policy/);
  assert.doesNotMatch(html, /connection-dialog|screenshot-link|logs-link|feedback-screenshot|\{\{view\}\}/);
  for (const [, local] of html.matchAll(/(?:src|href)="\.\/([^"]+)"/g)) await readFile(join(output, local.split('?')[0]));
  const assetVersion = html.match(/app\.js\?v=([a-f0-9]{16})/)[1];
  assert.ok(html.includes(`styles.css?v=${assetVersion}`));
  // An existing visitor's unversioned module cache must not mix with the new page.
  for (const file of (await readdir(output)).filter(file => /\.(?:js|mjs)$/.test(file))) {
    const source = await readFile(join(output, file), 'utf8');
    for (const [, path] of source.matchAll(/from\s+["'](\.\/[^"']+)["']/g)) {
      assert.equal(new URL(path, 'https://example.test/NinjaSlayer/').searchParams.get('v'), assetVersion, `${file}: ${path}`);
      await readFile(join(output, path.split('?')[0]));
    }
  }
  const snapshot = JSON.parse(await readFile(join(output, 'data.json'), 'utf8'));
  assert.equal(snapshot.catalog.length, 93);
  assert.equal(summarizePublic(snapshot).runs, 0);
  assert.equal(snapshot.sources.telemetry.state, 'ready');
  assert.equal(snapshot.chartSchemaVersion, chartSchemaVersion);
  assert.deepEqual(snapshot.feedback, []);
});

test('Pages refuses old chart snapshots after aggregation failure and preserves compatible data', async t => {
  const previous = { schemaVersion: 1, ...publishSnapshot(normalizeEvents([event()]), catalog), feedback: [],
    sources: { telemetry: { state: 'ready', at: '2026-09-30T00:00:00Z' }, feedback: { state: 'unloaded' } } };
  const legacy = structuredClone(previous);
  delete legacy.chartSchemaVersion;
  legacy.groups[0].charts = [{ chart: 'win-rate-floor', x: 1, n: 1, sum: 1, wins: 1 }];
  await assert.rejects(buildPages(t, { previous: legacy, failTelemetry: true }));
  await assert.rejects(buildPages(t, { failTelemetry: true }));
  const output = await buildPages(t, { previous, failTelemetry: true });
  const snapshot = JSON.parse(await readFile(join(output, 'data.json'), 'utf8'));
  assert.equal(snapshot.sources.telemetry.state, 'error');
  assert.deepEqual(snapshot.groups.map(group => group.charts), previous.groups.map(group => group.charts));
  assert.equal(snapshot.groups[0].runs, 1);
  const row = event();
  const fresh = await buildPages(t, { previous: legacy, rows: [['fixture-id', row.timestamp, JSON.stringify(row.properties)]] });
  const replaced = JSON.parse(await readFile(join(fresh, 'data.json'), 'utf8'));
  assert.equal(replaced.sources.telemetry.state, 'ready');
  assert.equal(replaced.chartSchemaVersion, chartSchemaVersion);
  assert.equal(replaced.groups[0].runs, 1);
  assert.ok(replaced.groups[0].charts.some(point => point.chart === 'ascension-wins'));
  assert.ok(!replaced.groups[0].charts.some(point => point.chart === 'win-rate-floor'));
});
