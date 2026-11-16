import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseArgs } from 'node:util';
import { normalizeEvents, parseData } from '../../Infrastructure/telemetry-worker/dashboard/data.mjs';
import { loadTelemetry } from '../../Infrastructure/telemetry-worker/dashboard/posthog.mjs';
import { matchesVersion } from '../../Infrastructure/telemetry-worker/dashboard/version-filter.mjs';

const eventId = 'NINJA_SLAYER_EVENT_NARAKU_EVENT';

export function summarizeNarakuChoices(telemetry, { days = 0, version = '' } = {}, now = Date.now()) {
  const cutoff = days ? now - days * 86400000 : -Infinity;
  const runs = telemetry.runs.filter(run => Date.parse(run.at) >= cutoff && Date.parse(run.at) <= now
    && matchesVersion(run.version, version));
  let encounters = 0, accepted = 0, silenced = 0, unfinished = 0, missing = 0, unknown = 0;
  const pages = {};
  for (const run of runs) for (const room of run.rooms) for (const player of run.participants) {
    const stats = room.player_stats.find(row => String(row.player_id) === String(player.net_id));
    const choices = Array.isArray(stats?.event_choices) ? stats.event_choices : [];
    const keys = choices.filter(choice => choice?.title?.table === 'events')
      .map(choice => choice.title.key).filter(key => typeof key === 'string' && key.startsWith(eventId + '.pages.'));
    if (!keys.length && !room.rooms.some(entry => entry.model_id === 'EVENT.' + eventId)) continue;
    encounters++;
    const last = keys.at(-1);
    if (!last) { missing++; continue; }
    const match = last.match(/\.pages\.([A-Z_0-9]+)\.options\.(ACCEPT_NARAKU|SILENCE_NARAKU|CALL_NARAKU)\.title$/);
    if (!match) { unknown++; continue; }
    if (match[2] === 'CALL_NARAKU') { unfinished++; continue; }
    if (match[2] === 'ACCEPT_NARAKU') accepted++; else silenced++;
    pages[last] = (pages[last] ?? 0) + 1;
  }
  const completed = accepted + silenced;
  return {
    generatedAtUtc: new Date(now).toISOString(), startUtc: days ? new Date(cutoff).toISOString() : null,
    version: version || 'all', sampledRuns: runs.length, encounters, completed,
    choices: [
      { id: 'ACCEPT_NARAKU', name: '接受奈落', count: accepted, rate: completed ? accepted / completed : null },
      { id: 'SILENCE_NARAKU', name: '令其闭嘴', count: silenced, rate: completed ? silenced / completed : null },
    ],
    unfinished, missing, unknown, pages,
    excluded: { rejected: telemetry.rejected, duplicates: telemetry.duplicates, conflicts: telemetry.conflicts },
    truncated: telemetry.truncated ?? false,
    scope: 'All participants recorded in consented, non-abandoned completed Ninja Slayer runs; one final choice per player and event visit. Higher-reload outcomes replace older attempts. Intermediate calls are excluded from the completed-choice denominator. Rates are observed choice shares, not event spawn chances.',
  };
}

async function main() {
  const { values } = parseArgs({ options: {
    input: { type: 'string' }, output: { type: 'string', default: 'build/analytics/naraku-choices.json' },
    days: { type: 'string', default: '0' }, version: { type: 'string', default: '' },
  } });
  const days = Number(values.days);
  if (!Number.isInteger(days) || days < 0 || days > 3650) throw new Error('days must be an integer from 0 to 3650; 0 means all time.');
  const telemetry = values.input ? normalizeEvents(parseData(await readFile(values.input, 'utf8')))
    : await loadTelemetry({ host: process.env.POSTHOG_QUERY_HOST || 'https://us.posthog.com',
      projectId: process.env.POSTHOG_PROJECT_ID, key: process.env.POSTHOG_PERSONAL_API_KEY });
  if (telemetry.truncated) throw new Error('The telemetry query reached its limit; refusing to report a partial sample as complete.');
  const report = summarizeNarakuChoices(telemetry, { days, version: values.version });
  await mkdir(dirname(resolve(values.output)), { recursive: true });
  await writeFile(values.output, JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify(report, null, 2));
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) await main();
