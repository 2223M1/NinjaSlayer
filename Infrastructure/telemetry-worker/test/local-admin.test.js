import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { crc32 } from 'node:zlib';
import { validateCopy, readDraft, copyModule, saveCopy } from '../dashboard/copy-store.mjs';
import { createDashboardServer } from '../dashboard/server.mjs';
import { inspectFeedbackZip } from '../dashboard/feedback-files.mjs';
import { publishCopy, continuePublication, readPublication } from '../dashboard/copy-publisher.mjs';

const copy = { schemaVersion: 1, overrides: { 'DOMO，玩家=SAN。': { zhs: '欢迎，玩家。', eng: 'Welcome, player.', jpn: 'ようこそ。' } } };
async function temp(t) {
  const dir = await mkdtemp(join(tmpdir(), 'ninja-admin-'));
  t.after(() => rm(dir, { recursive: true, force: true })); return dir;
}
// A real stored ZIP with one entry, including CRC and central directory.
function zipFixture(name, contents) {
  const filename = Buffer.from(name), bytes = Buffer.from(contents), crc = crc32(bytes);
  const local = Buffer.alloc(30); local.writeUInt32LE(0x04034b50); local.writeUInt16LE(20, 4); local.writeUInt32LE(crc, 14);
  local.writeUInt32LE(bytes.length, 18); local.writeUInt32LE(bytes.length, 22); local.writeUInt16LE(filename.length, 26);
  const central = Buffer.alloc(46); central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20, 4); central.writeUInt16LE(20, 6);
  central.writeUInt32LE(crc, 16); central.writeUInt32LE(bytes.length, 20); central.writeUInt32LE(bytes.length, 24); central.writeUInt16LE(filename.length, 28);
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50); end.writeUInt16LE(1, 8); end.writeUInt16LE(1, 10);
  end.writeUInt32LE(central.length + filename.length, 12); end.writeUInt32LE(local.length + filename.length + bytes.length, 16);
  return Buffer.concat([local, filename, bytes, central, filename, end]);
}

test('copy drafts persist without changing published copy and preserve dynamic placeholders', async t => {
  const path = join(await temp(t), 'draft.json');
  const published = await readFile(new URL('../../../Website/site-copy.json', import.meta.url), 'utf8');
  await saveCopy(path, copy); assert.deepEqual(await readDraft(path), copy);
  const module = await import('data:text/javascript,' + encodeURIComponent(copyModule(copy)));
  assert.equal(module.siteCopy['DOMO，玩家=SAN。'].eng, 'Welcome, player.');
  assert.equal(await readFile(new URL('../../../Website/site-copy.json', import.meta.url), 'utf8'), published);
  for (const invalid of [
    { schemaVersion: 1, overrides: { unknown: { zhs: 'x', eng: 'x', jpn: 'x' } } },
    { schemaVersion: 1, overrides: { 'DOMO，玩家=SAN。': { zhs: '<script>', eng: 'x', jpn: 'x' } } },
    { schemaVersion: 1, overrides: { '{0}': { zhs: 'missing', eng: 'x', jpn: 'x' } } },
  ]) assert.throws(() => validateCopy(invalid));
});

test('ZIP viewer returns exact file bytes and rejects traversal, missing members and invalid archives', async () => {
  const zip = zipFixture('logs/game.log', 'first line\n<script>not executable</script>');
  assert.deepEqual((await inspectFeedbackZip(zip)).map(file => file.name), ['logs/game.log']);
  assert.equal((await inspectFeedbackZip(zip, 0)).bytes.toString(), 'first line\n<script>not executable</script>');
  await assert.rejects(inspectFeedbackZip(zip, 1));
  await assert.rejects(inspectFeedbackZip(zipFixture('../outside.txt', 'no extraction')));
  await assert.rejects(inspectFeedbackZip(Buffer.from('invalid')));
});

test('local admin exposes verified private attachments only behind its loopback origin boundary', async t => {
  const id = '9b3d6f32-f6d4-4ca4-9a34-128763c3154b';
  const zip = zipFixture('game.log', 'private log body'); let reads = 0, downloads = 0, revoked = false;
  const server = await createDashboardServer({ draftPath: join(await temp(t), 'draft.json'), feedbackReader: {
    loadFeedback: async () => ({ feedback: [{ id, at: new Date().toISOString(), description: 'private feedback' }], warnings: [] }),
    readCompletedFeedback: async requested => { assert.equal(requested, id); reads++; if (revoked) throw new Error('revoked'); return { payload: { description: 'private feedback' }, storage: { screenshot: { key: 'image' }, logs: { chunks: ['zip'] } } }; },
    readFeedbackObject: async key => { downloads++; return key === 'zip' ? zip : Buffer.from('image bytes'); },
  } });
  server.listen(0, '127.0.0.1'); await once(server, 'listening'); t.after(() => server.close());
  const base = `http://127.0.0.1:${server.address().port}`;
  const prefix = `/api/feedback/${id}`;
  assert.equal((await fetch(base + prefix + '/metadata', { headers: { Origin: 'https://evil.invalid' } })).status, 403);
  assert.equal(reads, 0);
  assert.equal((await fetch(base + '/preview' + prefix + '/metadata')).status, 404);
  assert.equal((await fetch(base + '/admin')).status, 200);
  const metadata = await (await fetch(base + prefix + '/metadata')).json(); assert.equal(metadata.payload.description, 'private feedback');
  assert.equal((await (await fetch(base + prefix + '/files')).json())[0].name, 'game.log');
  assert.equal((await (await fetch(base + prefix + '/file?index=0')).json()).text, 'private log body');
  const download = await fetch(base + prefix + '/file?index=0&download=1');
  assert.match(download.headers.get('Content-Disposition'), /^attachment/); assert.equal(await download.text(), 'private log body');
  assert.equal(downloads, 1, 'browsing files reuses one in-memory ZIP after verifying each completion marker');
  revoked = true;
  assert.equal((await fetch(base + prefix + '/file?index=0')).status, 400, 'revocation denies even cached bytes');
  assert.equal((await fetch(base + '/api/copy', { method: 'POST', body: JSON.stringify(copy) })).status, 200);
  assert.match(await (await fetch(base + '/preview/site-copy.mjs')).text(), /Welcome, player/);
  assert.doesNotMatch(await (await fetch(base + '/site-copy.mjs')).text(), /Welcome, player/);
  assert.doesNotMatch(await (await fetch(base + '/preview/')).text(), /connection-dialog|screenshot-link/);
});

test('copy publishing changes one file, persists its PR and only merges the exact head after required CI', async t => {
  const path = pathToFileURL(join(await temp(t), 'publication.json'));
  const calls = []; let passed = false, changed = false;
  const api = async (url, method = 'GET', body) => {
    calls.push({ url, method, body });
    if (url.endsWith('/git/ref/heads/main')) return { object: { sha: 'base' } };
    if (url.includes('/contents/') && method === 'GET') return { sha: 'blob', content: Buffer.from('{"schemaVersion":1,"overrides":{}}').toString('base64') };
    if (url.endsWith('/git/refs')) return {};
    if (url.includes('/contents/') && method === 'PUT') return { commit: { sha: 'candidate' } };
    if (url.endsWith('/pulls') && method === 'POST') return { number: 123, html_url: 'https://github.com/2223M1/NinjaSlayer/pull/123' };
    if (url.endsWith('/pulls/123')) return { state: 'open', head: { sha: changed ? 'someone-else' : 'candidate' }, base: { ref: 'main' }, mergeable_state: 'clean' };
    if (url.endsWith('/files')) return [{ filename: 'Website/site-copy.json' }];
    if (url.endsWith('/check-runs')) return { check_runs: [{ name: 'validate', status: passed ? 'completed' : 'in_progress', conclusion: passed ? 'success' : null }] };
    if (url.endsWith('/merge')) return { merged: true, sha: 'merged' };
    throw new Error('Unexpected GitHub request: ' + url);
  };
  await publishCopy(copy, api, path);
  const put = calls.find(call => call.method === 'PUT'); assert.ok(put.url.endsWith('/contents/Website/site-copy.json'));
  assert.deepEqual(JSON.parse(Buffer.from(put.body.content, 'base64').toString()), copy);
  assert.equal((await readPublication(path)).number, 123);
  await assert.rejects(publishCopy(copy, api, path), /当前文案 PR/);
  await continuePublication(api, path); assert.ok(!calls.some(call => call.url.endsWith('/merge')));
  changed = true; await assert.rejects(continuePublication(api, path), /后台外修改/);
  changed = false; passed = true; const result = await continuePublication(api, path);
  assert.equal(result.state, 'merged');
  assert.deepEqual(calls.at(-1).body, { sha: 'candidate', merge_method: 'squash', commit_title: 'Update Intel website copy (#123)', commit_message: '' });
});
