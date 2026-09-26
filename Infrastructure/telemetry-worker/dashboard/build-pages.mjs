import { mkdir, writeFile, readFile, copyFile, cp } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { normalizeEvents } from './data.mjs';
import { useCurrentCatalog } from './public-data.mjs';
import { publishSnapshot, publicFeedback, readCatalog, readCurrentRelease } from './publish.mjs';
import { loadTelemetry } from './posthog.mjs';
import { loadRemoteFeedback } from '../scripts/feedback-reader.js';

const output = resolve(process.argv[2] ?? 'build/pages');
const catalog = await readCatalog();
let snapshot = { schemaVersion: 1, catalog, groups: [], feedback: [], excluded: {},
  sources: { telemetry: { state: 'unconnected' }, feedback: { state: 'unloaded' } } };
if (process.env.OBSERVATORY_PREVIOUS_URL) {
  const response = await fetch(process.env.OBSERVATORY_PREVIOUS_URL, { signal: AbortSignal.timeout(30_000) });
  if (response.ok) {
    snapshot = await response.json();
    if (snapshot.schemaVersion !== 1) throw new Error('Unsupported published snapshot version.');
  } else if (response.status !== 404) throw new Error(`Cannot preserve the last public snapshot: HTTP ${response.status}`);
}
snapshot = useCurrentCatalog(snapshot, catalog);
const config = { host: process.env.POSTHOG_QUERY_HOST || 'https://us.posthog.com',
  projectId: process.env.POSTHOG_PROJECT_ID, key: process.env.POSTHOG_PERSONAL_API_KEY };
if (config.key && config.projectId) {
  try {
    const result = await loadTelemetry(config);
    Object.assign(snapshot, publishSnapshot(normalizeEvents(result.results), catalog));
    snapshot.sources.telemetry = { state: 'ready', label: 'PostHog', at: new Date().toISOString(), truncated: result.truncated };
  } catch (error) {
    if (!snapshot.sources.telemetry.at) throw error;
    snapshot.sources.telemetry = { ...snapshot.sources.telemetry, state: 'error', message: '统计暂时无法更新，显示上次更新的数据。' };
  }
}
if (process.env.OBSERVATORY_READ_TOKEN) {
  try {
    const result = await loadRemoteFeedback(process.env.OBSERVATORY_READ_TOKEN);
    if (result.warnings.length) throw new Error('Feedback export is incomplete.');
    snapshot.feedback = publicFeedback(result.feedback);
    snapshot.sources.feedback = { state: 'ready', label: '玩家提交', at: new Date().toISOString() };
  } catch (error) {
    if (!snapshot.sources.feedback.at) throw error;
    snapshot.sources.feedback = { ...snapshot.sources.feedback, state: 'error', message: '反馈暂时无法更新，显示上次更新的内容。' };
  }
}
snapshot.generatedAt = new Date().toISOString();
snapshot.currentVersion = (await readCurrentRelease()).version;
try {
  const reports = []; let cursor;
  do {
    const url = new URL('https://telemetry.feixingwawa.cn/observatory/replays');
    if (cursor) url.searchParams.set('cursor', cursor);
    const response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
    if (!response.ok) throw new Error(`Replay index: ${response.status}`);
    const page = await response.json(); reports.push(...page.reports); cursor = page.cursor;
  } while (cursor);
  snapshot.reports = reports;
  snapshot.sources.replays = { state: 'ready', at: snapshot.generatedAt };
} catch {
  snapshot.sources.replays = { ...snapshot.sources.replays, state: 'error', message: '对局记录暂时无法更新，显示上次更新的内容。' };
}
snapshot.reports = (snapshot.reports ?? []).filter(report => Date.parse(report.expires) > Date.now());
snapshot.feedback = snapshot.feedback.filter(item => Date.parse(item.at) >= Date.now() - 180 * 86_400_000);
await mkdir(output, { recursive: true });
const template = await readFile(new URL('index.html', import.meta.url), 'utf8');
await writeFile(join(output, 'index.html'), template.replaceAll('{{view}}', 'pages').replace(/<!-- ADMIN -->[\s\S]*?<!-- END ADMIN -->/g, ''));
await writeFile(join(output, 'data.json'), JSON.stringify(snapshot));
for (const file of ['app.js', 'styles.css', 'public-data.mjs', 'charts.mjs', 'chart-view.mjs', 'catalog-view.mjs', 'replay-view.mjs']) await copyFile(new URL(file, import.meta.url), join(output, file));
await mkdir(join(output, 'vendor'), { recursive: true });
await copyFile(new URL('../node_modules/chart.js/dist/chart.umd.js', import.meta.url), join(output, 'vendor/chart.umd.js'));
await copyFile(new URL('../node_modules/chart.js/LICENSE.md', import.meta.url), join(output, 'vendor/LICENSE.Chart.js.md'));
await copyFile(new URL('vendor/LICENSE.Spire-Codex.md', import.meta.url), join(output, 'vendor/LICENSE.Spire-Codex.md'));
await cp(new URL('../../../Website/content/', import.meta.url), join(output, 'content'), { recursive: true });
await cp(new URL('assets/', import.meta.url), join(output, 'assets'), { recursive: true });
await writeFile(join(output, '.nojekyll'), '');
console.log(`Pages artifact ready: ${snapshot.catalog.length} cards, ${snapshot.groups.reduce((sum, group) => sum + group.runs, 0)} runs, ${snapshot.feedback.length} public feedback entries.`);
