import { spawn } from 'node:child_process';
import { readFile, writeFile, mkdir, rename } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { validateCopy } from './copy-store.mjs';

const repo = '2223M1/NinjaSlayer';
const contentPath = 'Website/site-copy.json';
const receiptPath = new URL('../../../build/dashboard/copy-publication.json', import.meta.url);
const digest = copy => createHash('sha256').update(JSON.stringify(copy)).digest('hex');

// gh owns authentication. No token or subprocess diagnostics are returned to the browser.
export function github(path, method = 'GET', body) {
  return new Promise((resolve, reject) => {
    const child = spawn('gh', ['api', path, '--method', method, ...(body ? ['--input', '-'] : [])], { windowsHide: true, shell: false });
    const chunks = []; let size = 0;
    const timer = setTimeout(() => child.kill(), 60000);
    child.stdout.on('data', data => { size += data.length; if (size > 8 * 1024 * 1024) child.kill(); else chunks.push(data); });
    child.stderr.resume();
    child.on('error', () => { clearTimeout(timer); reject(new Error('无法运行 GitHub CLI，请确认已安装 gh 并完成 gh auth login。')); });
    child.on('close', code => {
      clearTimeout(timer);
      if (code !== 0) { reject(new Error(`GitHub 请求失败（${method} ${path.split('?')[0]}），请检查登录、权限及网络。`)); return; }
      try { resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : null); }
      catch { reject(new Error('GitHub 返回了无效数据。')); }
    });
    child.stdin.end(body ? JSON.stringify(body) : undefined);
  });
}

export async function readPublication(path = receiptPath) {
  try { return JSON.parse(await readFile(path, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}
async function savePublication(receipt, path = receiptPath) {
  await mkdir(new URL('.', path), { recursive: true });
  await writeFile(new URL('copy-publication.json.tmp', path), JSON.stringify(receipt));
  await rename(new URL('copy-publication.json.tmp', path), path);
  return receipt;
}

export async function publishCopy(value, api = github, path = receiptPath) {
  const copy = validateCopy(value);
  const hash = digest(copy);
  const previous = await readPublication(path);
  if (previous && previous.state !== 'merged') throw new Error('请先完成或关闭当前文案 PR，再发布另一份草稿。');
  const base = await api(`repos/${repo}/git/ref/heads/main`);
  const current = await api(`repos/${repo}/contents/${contentPath}?ref=${base.object.sha}`);
  if (digest(validateCopy(JSON.parse(Buffer.from(current.content, 'base64').toString('utf8')))) === hash)
    return { state: 'unchanged', message: '草稿与官网源码一致，无需发布。' };
  const branch = `codex/site-copy-${Date.now()}`;
  await api(`repos/${repo}/git/refs`, 'POST', { ref: `refs/heads/${branch}`, sha: base.object.sha });
  // Only this path can be committed; never stage or switch the user's working tree.
  const commit = await api(`repos/${repo}/contents/${contentPath}`, 'PUT', {
    message: 'Update Intel website copy', branch, sha: current.sha,
    content: Buffer.from(JSON.stringify(copy, null, 2) + '\n').toString('base64'),
    committer: { name: '2223M1', email: '104559061+2223M1@users.noreply.github.com' },
    author: { name: '2223M1', email: '104559061+2223M1@users.noreply.github.com' },
  });
  // Persist the branch first so a restart after PR creation remains recoverable.
  let receipt = await savePublication({ branch, sha: commit.commit.sha, hash, state: 'creating', at: new Date().toISOString() }, path);
  const pr = await api(`repos/${repo}/pulls`, 'POST', { title: 'Update Intel website copy', head: branch, base: 'main',
    body: 'Updates website copy saved and previewed in the local administrator page. Only Website/site-copy.json changes; game rules and private feedback remain unchanged.\n\nValidation: required repository CI before squash merge.' });
  return savePublication({ ...receipt, number: pr.number, url: pr.html_url, state: 'checks' }, path);
}

export async function continuePublication(api = github, path = receiptPath) {
  let receipt = await readPublication(path);
  if (!receipt || receipt.state === 'merged') return receipt;
  if (!receipt.number) {
    const prs = await api(`repos/${repo}/pulls?head=2223M1:${receipt.branch}&state=all`);
    if (prs.length !== 1) throw new Error(`请在 GitHub 检查文案分支 ${receipt.branch} 的 PR；不会重复创建。`);
    receipt = await savePublication({ ...receipt, number: prs[0].number, url: prs[0].html_url, state: 'checks' }, path);
  }
  const pr = await api(`repos/${repo}/pulls/${receipt.number}`);
  if (pr.merged) return savePublication({ ...receipt, state: 'merged', mergeSha: pr.merge_commit_sha }, path);
  if (pr.state === 'closed') { await savePublication({ ...receipt, state: 'merged', message: 'PR 已关闭，未部署。' }, path); return readPublication(path); }
  if (pr.head.sha !== receipt.sha || pr.base.ref !== 'main') throw new Error('PR 已在后台外修改，请在 GitHub 检查后合并。');
  const files = await api(`repos/${repo}/pulls/${receipt.number}/files`);
  if (files.length !== 1 || files[0].filename !== contentPath) throw new Error('PR 包含文案以外的文件，停止自动合并。');
  const checks = await api(`repos/${repo}/commits/${receipt.sha}/check-runs`);
  if (!checks.check_runs.some(check => check.name === 'validate' && check.conclusion === 'success')
      || checks.check_runs.some(check => check.status !== 'completed' || !['success', 'neutral', 'skipped'].includes(check.conclusion))
      || pr.mergeable_state !== 'clean') return { ...receipt, message: '等待 CI 通过并满足主分支保护；稍后点击继续发布。' };
  const merged = await api(`repos/${repo}/pulls/${receipt.number}/merge`, 'PUT', {
    sha: receipt.sha, merge_method: 'squash', commit_title: `Update Intel website copy (#${receipt.number})`, commit_message: '',
  });
  if (!merged.merged) throw new Error('GitHub 未确认合并，请检查 PR。');
  return savePublication({ ...receipt, state: 'merged', mergeSha: merged.sha, message: '已合并，官网等待 Pages 部署；可在 PR 中查看 Actions。' }, path);
}
