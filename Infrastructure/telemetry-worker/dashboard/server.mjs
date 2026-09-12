import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { parseData, normalizeEvents, summarize } from './data.mjs';
import { loadTelemetry, QUERY_HOSTS } from './posthog.mjs';
import { loadFeedback, readCompletedFeedback, readFeedbackObject } from '../scripts/feedback-reader.js';
import { UUID_PATTERN } from '../src/validation.js';

const repo = new URL('../../../', import.meta.url);
const publicFiles = new Map([['/', ['index.html', 'text/html']], ['/app.js', ['app.js', 'text/javascript']], ['/styles.css', ['styles.css', 'text/css']]]);

async function body(request) {
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > 64 * 1024 * 1024) throw new Error('JSON 文件超过 64 MiB，请缩小导出范围。');
    chunks.push(chunk);
  }
  return parseData(Buffer.concat(chunks).toString('utf8'));
}

export async function createDashboardServer() {
  const names = JSON.parse(await readFile(new URL('NinjaSlayer/localization/zhs/cards.json', repo), 'utf8'));
  const specs = JSON.parse(await readFile(new URL('Tests/NinjaSlayer.OrbContractTests/card-metadata.json', repo), 'utf8'));
  const catalog = specs.filter(card => !card.Upgraded).map(card => ({
    id: card.Id, name: names[`${card.Id.split('.').at(-1)}.title`] ?? card.Id,
    rarity: { Common: '白卡', Uncommon: '蓝卡', Rare: '金卡', Basic: '初始', Token: '衍生', Ancient: '先古', Event: '事件', Special: '特殊' }[card.Rarity] ?? card.Rarity,
    type: { Attack: '攻击', Skill: '技能', Power: '能力', Status: '状态', Curse: '诅咒' }[card.Type],
  }));
  let config = { host: process.env.POSTHOG_QUERY_HOST ?? QUERY_HOSTS[0], projectId: process.env.POSTHOG_PROJECT_ID ?? '', key: process.env.POSTHOG_PERSONAL_API_KEY ?? '' };
  let telemetry = { runs: [], rejected: 0, duplicates: 0 }, feedback = [], feedbackWarnings = [];
  const sources = { telemetry: { state: 'unconnected' }, feedback: { state: 'unloaded' } };
  let refreshing = null;

  async function refresh() {
    const jobs = await Promise.allSettled([
      config.key ? loadTelemetry(config).then(result => {
        telemetry = normalizeEvents(result.results);
        sources.telemetry = { state: 'ready', label: 'PostHog', at: new Date().toISOString(), truncated: result.truncated };
      }) : Promise.resolve(),
      loadFeedback().then(result => {
        feedback = result.feedback;
        feedbackWarnings = result.warnings;
        sources.feedback = { state: 'ready', label: 'Cloudflare', at: new Date().toISOString() };
      }),
    ]);
    jobs.forEach((result, index) => {
      if (result.status === 'rejected') {
        const source = index === 0 ? 'telemetry' : 'feedback';
        sources[source] = { ...sources[source], state: 'error', message: index === 0 ? result.reason.message : 'Cloudflare 读取失败。请在此项目运行 npx wrangler login 后重试。' };
      }
    });
  }

  return createServer(async (request, response) => {
    const address = response.socket.address();
    const expectedHost = `127.0.0.1:${address.port}`;
    // This process can read private feedback. Reject cross-origin access and DNS rebinding.
    if (request.headers.host !== expectedHost || (request.headers.origin && request.headers.origin !== `http://${expectedHost}`)
        || request.headers['sec-fetch-site'] === 'cross-site') {
      response.writeHead(403).end();
      return;
    }
    const url = new URL(request.url, `http://${expectedHost}`);
    const send = (status, data, type = 'application/json; charset=utf-8', extra = {}) => {
      response.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff',
        'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'", ...extra });
      response.end(type.startsWith('application/json') ? JSON.stringify(data) : data);
    };
    try {
      if (request.method === 'GET' && publicFiles.has(url.pathname)) {
        const [file, type] = publicFiles.get(url.pathname);
        send(200, await readFile(new URL(file, import.meta.url)), `${type}; charset=utf-8`);
      } else if (request.method === 'GET' && url.pathname === '/api/view') {
        const filters = Object.fromEntries(url.searchParams);
        send(200, { application: 'NinjaSlayerDashboard', ...summarize(telemetry.runs, catalog, filters), sources, feedback, feedbackWarnings,
          rejected: telemetry.rejected, duplicates: telemetry.duplicates, conflicts: telemetry.conflicts,
          invalidCombats: telemetry.invalidCombats,
          connection: { host: config.host, projectId: config.projectId, configured: Boolean(config.key) } });
      } else if (request.method === 'POST' && url.pathname === '/api/connect') {
        if (refreshing) { send(409, { error: '请等待当前同步完成后再更换连接。' }); return; }
        const next = await body(request);
        if (!QUERY_HOSTS.includes(next?.host) || !/^\d+$/.test(next.projectId) || typeof next.key !== 'string' || !next.key.trim())
          throw new Error('请填写区域、数字项目 ID 和个人 API key。');
        config = { host: next.host, projectId: next.projectId, key: next.key.trim() };
        send(200, { ok: true });
      } else if (request.method === 'POST' && url.pathname === '/api/refresh') {
        refreshing ??= refresh().finally(() => { refreshing = null; });
        await refreshing;
        send(200, { ok: true });
      } else if (request.method === 'POST' && url.pathname === '/api/import') {
        if (refreshing) { send(409, { error: '请等待当前同步完成后再导入。' }); return; }
        const imported = normalizeEvents(await body(request));
        if (!imported.runs.length) throw new Error('文件中没有可用的忍者杀手已完成对局。需要原生存档、胜负及完整 RitsuLib 事件属性。');
        telemetry = imported;
        sources.telemetry = { state: 'ready', label: '本机导入', at: new Date().toISOString() };
        send(200, { ok: true, runs: imported.runs.length });
      } else if (request.method === 'GET' && url.pathname.startsWith('/api/feedback/')) {
        const [, , , id, kind] = url.pathname.split('/');
        if (!UUID_PATTERN.test(id ?? '') || !['screenshot', 'logs'].includes(kind)) { send(404, { error: '未找到此附件。' }); return; }
        const metadata = await readCompletedFeedback(id);
        const bytes = kind === 'screenshot' ? await readFeedbackObject(metadata.storage.screenshot.key)
          : Buffer.concat(await Promise.all(metadata.storage.logs.chunks.map(readFeedbackObject)));
        send(200, bytes, kind === 'screenshot' ? 'image/png' : 'application/zip',
          { 'Content-Disposition': `${kind === 'screenshot' ? 'inline' : 'attachment'}; filename="${id}.${kind === 'screenshot' ? 'png' : 'zip'}"` });
      } else send(404, { error: '未找到此页面。' });
    } catch (error) { send(400, { error: error.message }); }
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const server = await createDashboardServer();
  const port = Number(process.env.NINJASLAYER_DASHBOARD_PORT ?? 4178);
  server.listen(port, '127.0.0.1', () => console.log(`NinjaSlayer 管理网页：http://127.0.0.1:${server.address().port}`));
  server.on('error', error => { console.error(error.message); process.exitCode = 1; });
}
