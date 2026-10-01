import { randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, rename } from 'node:fs/promises';
import { github } from './copy-publisher.mjs';

const workflow = 'repos/2223M1/NinjaSlayer/actions/workflows/observatory.yml';
const receiptPath = new URL('../../../build/dashboard/feedback-publication.json', import.meta.url);
async function readReceipt(path) {
  try { return JSON.parse(await readFile(path, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}
async function saveReceipt(value, path) {
  await mkdir(new URL('.', path), { recursive: true });
  const temporary = new URL('feedback-publication.json.tmp', path);
  await writeFile(temporary, JSON.stringify(value)); await rename(temporary, path); return value;
}
export async function feedbackPublication(api = github, path = receiptPath) {
  const receipt = await readReceipt(path);
  if (!receipt || receipt.state === 'completed' || receipt.state === 'failed') return receipt;
  const { workflow_runs: runs } = await api(`${workflow}/runs?event=workflow_dispatch&per_page=50`);
  const run = runs.find(item => item.display_title === `Feedback sync ${receipt.id}`);
  if (!run) return { ...receipt, message: '同步请求已提交，等待 GitHub 创建任务。' };
  const state = run.status === 'completed' ? run.conclusion === 'success' ? 'completed' : 'failed' : 'running';
  return saveReceipt({ ...receipt, state, url: run.html_url,
    message: state === 'completed' ? '官网部署已完成。' : state === 'failed' ? '同步或部署失败；官网保留此前数据，请查看任务。' : '官网正在同步和部署。' }, path);
}
export async function publishFeedback(api = github, path = receiptPath) {
  const previous = await feedbackPublication(api, path);
  if (previous && !['completed', 'failed'].includes(previous.state)) return previous;
  const id = randomUUID(), at = new Date().toISOString();
  await api(`${workflow}/dispatches`, 'POST', { ref: 'main', inputs: { feedback_sync_id: id } });
  return saveReceipt({ id, at, state: 'queued', message: '同步请求已提交；保存状态已生效，官网尚待部署。' }, path);
}
