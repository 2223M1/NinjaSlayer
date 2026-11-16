import test from 'node:test';
import assert from 'node:assert/strict';
import { summarizeNarakuChoices } from './naraku-choices.mjs';
import { normalizeEvents } from '../../Infrastructure/telemetry-worker/dashboard/data.mjs';

const prefix = 'NINJA_SLAYER_EVENT_NARAKU_EVENT';
function row(seed, options, reloads = 0, version = '1.2.8') {
  return { event: 'run_history.completed', timestamp: '2026-11-16T00:00:00Z', properties: {
    applicant_id: 'NinjaSlayer', is_abandoned: false, is_victory: true,
    payload: { applicant_payload: { run_history: {
      players: [{ character_id: 'CHARACTER.NINJA_SLAYER_CHARACTER_NINJA_SLAYER_CHARACTER', net_id: '1', deck: [] }],
      rng: { seed }, start_time: 100, ascension: 0, num_reloads: reloads,
      map_point_history: [[{ rooms: [{ room_type: 'event', model_id: 'EVENT.' + prefix }],
        player_stats: [{ player_id: '1', event_choices: options.map(([page, option]) => ({
          title: { table: 'events', key: `${prefix}.pages.${page}.options.${option}.title` },
        })) }] }]],
    } }, private_contributions: { NinjaSlayer: { ninja_slayer_balance_context: { version } } } },
  } };
}
const now = Date.parse('2026-11-16T01:00:00Z');
test('final choices combine silence pages and exclude intermediate/missing/unknown records', () => {
  const rows = [row('a', [['INITIAL', 'CALL_NARAKU'], ['CALL_1', 'CALL_NARAKU'], ['ACCEPT', 'ACCEPT_NARAKU']]),
    row('b', [['INITIAL', 'SILENCE_NARAKU']]), row('c', [['CALL_2', 'SILENCE_NARAKU']]),
    row('d', [['INITIAL', 'CALL_NARAKU']]), row('e', []), row('f', [['INITIAL', 'FUTURE_OPTION']])];
  const result = summarizeNarakuChoices(normalizeEvents(rows), {}, now);
  assert.equal(result.encounters, 6); assert.equal(result.completed, 3);
  assert.deepEqual(result.choices.map(x => [x.count, x.rate]), [[1, 1/3], [2, 2/3]]);
  assert.deepEqual([result.unfinished, result.missing, result.unknown], [1, 1, 1]);
  assert.equal(Object.values(result.pages).reduce((a, b) => a + b), 3);
  assert.ok(!JSON.stringify(result).includes('net_id'));
});
test('deduplication keeps the final SL outcome and excludes conflicting results', () => {
  const old = row('same', [['INITIAL', 'SILENCE_NARAKU']]);
  const latest = row('same', [['ACCEPT', 'ACCEPT_NARAKU']], 1);
  const result = summarizeNarakuChoices(normalizeEvents([old, latest, structuredClone(latest), old]), {}, now);
  assert.equal(result.completed, 1); assert.equal(result.choices[0].count, 1);
  const conflict = structuredClone(latest); conflict.properties.is_victory = false;
  assert.equal(summarizeNarakuChoices(normalizeEvents([latest, conflict]), {}, now).completed, 0);
});
test('filters apply to completed runs and no data has no inferred probability', () => {
  const old = row('old', [['ACCEPT', 'ACCEPT_NARAKU']], 0, '1.2.7'); old.timestamp = '2026-11-01T00:00:00Z';
  const data = normalizeEvents([old, row('new', [['INITIAL', 'SILENCE_NARAKU']])]);
  assert.equal(summarizeNarakuChoices(data, { days: 7 }, now).completed, 1);
  assert.equal(summarizeNarakuChoices(data, { version: '1.2.7' }, now).choices[0].count, 1);
  assert.deepEqual(summarizeNarakuChoices(data, { version: '1.2.6' }, now).choices.map(x => x.rate), [null, null]);
});
