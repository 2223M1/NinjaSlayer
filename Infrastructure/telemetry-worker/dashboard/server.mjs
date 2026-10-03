import { createServer } from 'node:http';
import { readFile, readdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { parseData, normalizeEvents } from './data.mjs';
import { summarizePublic } from './public-data.mjs';
import { readCatalog, readCurrentRelease, publishSnapshot, publicFeedback } from './publish.mjs';
import { loadTelemetry, QUERY_HOSTS } from './posthog.mjs';
import { loadFeedback, readCompletedFeedback, readFeedbackObject, loadFeedbackReview, saveFeedbackReview, loadFeedbackDetails } from '../scripts/feedback-reader.js';
import { UUID_PATTERN } from '../src/validation.js';
import { copyDefaults, readDraft, readCopy, saveCopy, draftCopyPath, copyModule } from './copy-store.mjs';
import { publishCopy, continuePublication, readPublication } from './copy-publisher.mjs';
import { inspectFeedbackZip } from './feedback-files.mjs';
import { feedbackPublication, publishFeedback } from './feedback-publisher.mjs';

const publicFiles = new Map([['/', ['index.html', 'text/html']], ['/app.js', ['app.js', 'text/javascript']], ['/styles.css', ['styles.css', 'text/css']], ['/public-data.mjs', ['public-data.mjs', 'text/javascript']]]);
for (const file of ['i18n.mjs', 'translations.mjs', 'charts.mjs', 'chart-view.mjs', 'catalog-view.mjs', 'replay-view.mjs']) publicFiles.set('/' + file, [file, 'text/javascript']);
publicFiles.set('/vendor/chart.umd.js', ['../node_modules/chart.js/dist/chart.umd.js', 'text/javascript']);
for (const [path, file, type] of [['/admin', 'admin.html', 'text/html'], ['/admin.js', 'admin.js', 'text/javascript'], ['/admin.css', 'admin.css', 'text/css']]) publicFiles.set(path, [file, type]);

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

export async function createDashboardServer({ feedbackReader = { loadFeedback, readCompletedFeedback, readFeedbackObject, loadFeedbackReview, saveFeedbackReview, loadFeedbackDetails }, feedbackPublisher = { feedbackPublication, publishFeedback }, draftPath = draftCopyPath } = {}) {
  const catalog = await readCatalog();
  for (const name of await readdir(new URL('assets/', import.meta.url))) {
    const type = { png: 'image/png', jpg: 'image/jpeg', gif: 'image/gif' }[name.split('.').at(-1)];
    if (type) publicFiles.set('/assets/' + name, ['assets/' + name, type]);
  }
  let config = { host: process.env.POSTHOG_QUERY_HOST ?? QUERY_HOSTS[0], projectId: process.env.POSTHOG_PROJECT_ID ?? '', key: process.env.POSTHOG_PERSONAL_API_KEY ?? '' };
  let telemetry = { runs: [], rejected: 0, duplicates: 0 }, feedback = [], feedbackWarnings = [];
  const sources = { telemetry: { state: 'unconnected' }, feedback: { state: 'unloaded' } };
  let refreshing = null;
  let editing = false;
  let reviewing = false, publishingFeedback = false;
  let zipCache = null, zipCacheTimer;

  async function refresh() {
    const jobs = await Promise.allSettled([
      config.key ? loadTelemetry(config).then(result => {
        telemetry = normalizeEvents(result.results);
        sources.telemetry = { state: 'ready', label: 'PostHog', at: new Date().toISOString(), truncated: result.truncated };
      }) : Promise.resolve(),
      feedbackReader.loadFeedback().then(result => {
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
    const preview = url.pathname.startsWith('/preview/');
    if (preview) url.pathname = url.pathname.slice('/preview'.length);
    const send = (status, data, type = 'application/json; charset=utf-8', extra = {}) => {
      response.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff',
        'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self' https://telemetry.feixingwawa.cn; object-src 'none'; frame-ancestors 'none'; base-uri 'none'", ...extra });
      response.end(type.startsWith('application/json') ? JSON.stringify(data) : data);
    };
    try {
      if (preview && (url.pathname.startsWith('/api/') || url.pathname.startsWith('/admin'))) {
        send(404, { error: '预览不包含管理接口。' });
      } else if (request.method === 'GET' && url.pathname === '/site-copy.mjs') {
        send(200, copyModule(preview ? await readDraft(draftPath) : await readCopy()), 'text/javascript; charset=utf-8');
      } else if (preview && request.method === 'GET' && url.pathname === '/data.json') {
        const remote = await fetch('https://2223m1.github.io/NinjaSlayer/data.json', { signal: AbortSignal.timeout(30000) });
        if (!remote.ok) throw new Error('无法读取官网快照。');
        send(200, await remote.json());
      } else if (request.method === 'GET' && publicFiles.has(url.pathname)) {
        const [file, type] = publicFiles.get(url.pathname);
        const bytes = await readFile(new URL(file, import.meta.url));
        let content = bytes;
        if (file === 'index.html') {
          content = bytes.toString('utf8').replaceAll('{{view}}', preview ? 'pages' : 'admin');
          if (preview) content = content.replace(/<!-- ADMIN -->[\s\S]*?<!-- END ADMIN -->/g, '');
        }
        send(200, content, type.startsWith('image/') ? type : `${type}; charset=utf-8`);
      } else if (request.method === 'GET' && url.pathname === '/api/copy') {
        send(200, { defaults: copyDefaults, draft: await readDraft(draftPath), publication: await readPublication() });
      } else if (request.method === 'POST' && ['/api/copy', '/api/copy/publish', '/api/copy/continue'].includes(url.pathname)) {
        if (editing) { send(409, { error: '文案正在保存或发布，请稍候。' }); return; }
        editing = true;
        try {
          if (url.pathname === '/api/copy') send(200, await saveCopy(draftPath, await body(request)));
          else if (url.pathname.endsWith('/publish')) send(200, await publishCopy(await readDraft(draftPath)));
          else send(200, await continuePublication());
        } finally { editing = false; }
      } else if (request.method === 'GET' && /^\/content\/(images\/[a-f0-9]{64}\.webp|versions\/\d+\.\d+\.\d+\/catalog\.json)$/.test(url.pathname)) {
        const bytes = await readFile(new URL('../../../Website' + url.pathname, import.meta.url));
        send(200, url.pathname.endsWith('.json') ? JSON.parse(bytes) : bytes, url.pathname.endsWith('.json') ? 'application/json' : 'image/webp');
      } else if (request.method === 'GET' && url.pathname === '/api/snapshot') {
        send(200, { ...publishSnapshot(telemetry, catalog), currentVersion: (await readCurrentRelease()).version, sources, feedback: publicFeedback(feedback) });
      } else if (request.method === 'GET' && url.pathname === '/api/view') {
        const filters = Object.fromEntries(url.searchParams);
        send(200, { application: 'NinjaSlayerDashboard', ...summarizePublic(publishSnapshot(telemetry, catalog), filters), sources, feedback, feedbackWarnings,
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
        if (reviewing) { send(409, { error: '反馈正在保存，请稍候再同步。' }); return; }
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
      } else if (url.pathname === '/api/feedback-publication' && ['GET', 'POST'].includes(request.method)) {
        if (publishingFeedback) { send(409, { error: '正在提交同步请求，请稍候。' }); return; }
        publishingFeedback = true;
        try { send(200, await (request.method === 'POST' ? feedbackPublisher.publishFeedback() : feedbackPublisher.feedbackPublication())); }
        finally { publishingFeedback = false; }
      } else if (request.method === 'GET' && /^\/api\/feedback\/[^/]+\/details$/.test(url.pathname)) {
        const id = url.pathname.split('/')[3];
        if (!UUID_PATTERN.test(id)) { send(404, { error: '反馈编号无效。' }); return; }
        send(200, await feedbackReader.loadFeedbackDetails(id));
      } else if (/^\/api\/feedback\/[^/]+\/review$/.test(url.pathname) && ['GET', 'PUT'].includes(request.method)) {
        const id = url.pathname.split('/')[3];
        if (!UUID_PATTERN.test(id)) { send(404, { error: '反馈编号无效。' }); return; }
        if (request.method === 'GET') { send(200, await feedbackReader.loadFeedbackReview(id)); return; }
        if (reviewing || refreshing) { send(409, { error: '反馈正在保存或同步，请稍后重试。' }); return; }
        reviewing = true;
        try {
          const review = await feedbackReader.saveFeedbackReview(id, await body(request));
          const item = feedback.find(item => item.id === id);
          if (item) item.review = review;
          send(200, review);
        } finally { reviewing = false; }
      } else if (request.method === 'GET' && url.pathname.startsWith('/api/feedback/')) {
        const [, , , id, kind] = url.pathname.split('/');
        if (!UUID_PATTERN.test(id ?? '') || url.pathname.split('/').length !== 5 || !['screenshot', 'logs', 'metadata', 'files', 'file'].includes(kind)) { send(404, { error: '未找到此附件。' }); return; }
        let metadata;
        try { metadata = await feedbackReader.readCompletedFeedback(id); }
        catch { throw new Error('无法核验此反馈，请检查 Cloudflare 登录和网络，或确认附件尚未过期。'); }
        if (kind === 'metadata') { send(200, metadata); return; }
        const zipKey = kind === 'screenshot' ? null : JSON.stringify(metadata.storage.logs.chunks);
        let bytes;
        try {
          bytes = kind === 'screenshot' ? await feedbackReader.readFeedbackObject(metadata.storage.screenshot.key)
            : zipCache?.key === zipKey ? zipCache.bytes : Buffer.concat(await Promise.all(metadata.storage.logs.chunks.map(feedbackReader.readFeedbackObject)));
        } catch { throw new Error('附件下载失败，请检查网络后重试。已过期的附件无法恢复。'); }
        // Real feedback contains large crash dumps. Re-downloading the same archive per member
        // timed out in local verification. Keep one bounded archive briefly, after revalidating
        // its completion marker on every request, so revoked/expired feedback is never reused.
        if (zipKey !== null && zipCache?.key !== zipKey && bytes.length <= 64 * 1024 * 1024) {
          clearTimeout(zipCacheTimer);
          zipCache = { key: zipKey, bytes };
          zipCacheTimer = setTimeout(() => { zipCache = null; }, 5 * 60 * 1000);
          zipCacheTimer.unref();
        }
        if (kind === 'files') { send(200, await inspectFeedbackZip(bytes)); return; }
        if (kind === 'file') {
          const index = url.searchParams.get('index');
          if (!/^\d{1,4}$/.test(index ?? '')) throw new Error('附件编号无效。');
          const file = await inspectFeedbackZip(bytes, Number(index));
          if (url.searchParams.has('download')) {
            send(200, file.bytes, 'application/octet-stream', { 'Content-Disposition': `attachment; filename="attachment-${index}.bin"; filename*=UTF-8''${encodeURIComponent(file.name.split('/').at(-1)).replaceAll("'", '%27')}` });
          } else {
            const text = file.bytes.subarray(0, 512 * 1024);
            send(200, { name: file.name, size: file.size, truncated: text.length < file.bytes.length,
              binary: text.includes(0), text: text.includes(0) ? '' : text.toString('utf8') });
          }
          return;
        }
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
