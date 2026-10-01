const $ = selector => document.querySelector(selector);
const element = (tag, text) => { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; return node; };
const link = (label, href) => { const node = element('a', label); node.href = href; return node; };
async function api(path, method = 'GET', value) {
  const response = await fetch(path, { method, ...(value ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) } : {}) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error ?? `请求失败：${response.status}`);
  return data;
}
const status = text => { $('#status').textContent = text; };
function action(button, operation) {
  button.onclick = async () => {
    button.disabled = true;
    try { await operation(); } catch (error) { status(error.message); }
    finally { button.disabled = false; }
  };
}
let feedback = [], defaults, draft, key, dirty = false, selection = 0;
function showFeedbackList() {
  const query = $('#feedback-search').value.toLowerCase();
  const list = $('#feedback-list'); list.replaceChildren();
  for (const item of feedback.filter(item => JSON.stringify(item).toLowerCase().includes(query))) {
    const button = element('button', `${item.at?.slice(0, 10) ?? ''} · ${item.category ?? item.context?.category ?? '玩家反馈'}`);
    button.append(element('small', String(item.description ?? item.message ?? item.id).slice(0, 120)));
    action(button, () => showFeedback(item)); list.append(button);
  }
  if (!list.children.length) list.append(element('p', '没有符合条件的来信。'));
}
async function loadFeedbackView() {
  const data = await api('/api/view'); feedback = data.feedback;
  $('#feedback-state').textContent = `共 ${feedback.length} 条 · ${data.sources.feedback.state === 'ready' ? '已同步' : data.sources.feedback.message ?? '尚未同步'}${data.feedbackWarnings.length ? ` · ${data.feedbackWarnings.length} 条读取警告` : ''}`;
  if (data.feedbackWarnings.length) status(data.feedbackWarnings.join('\n'));
  showFeedbackList();
}
async function showFeedback(item) {
  const current = ++selection, detail = $('#feedback-detail');
  detail.replaceChildren(element('p', '正在核验反馈及附件…'));
  const base = `/api/feedback/${encodeURIComponent(item.id)}`;
  const metadata = await api(`${base}/metadata`);
  if (current !== selection) return;
  detail.replaceChildren(element('h3', `${item.at?.slice(0, 10) ?? ''} · ${item.id}`));
  const body = element('pre', JSON.stringify(metadata.payload, null, 2)); detail.append(body);
  const attachments = element('div'); attachments.className = 'attachments';
  const screenshot = link('打开原尺寸截图 ↗', `${base}/screenshot`); screenshot.target = '_blank'; screenshot.rel = 'noopener';
  const metadataLink = link('下载完整元数据', `${base}/metadata`); metadataLink.download = `${item.id}.json`;
  attachments.append(screenshot, link('下载完整日志 ZIP', `${base}/logs`), metadataLink); detail.append(attachments);
  const img = element('img'); img.alt = '玩家提交的原始截图'; img.src = `${base}/screenshot`;
  img.onerror = () => { if (current === selection) status('截图读取失败，请确认附件尚未过期后重试。'); }; detail.append(img);
  const context = element('details'); context.append(element('summary', '环境信息与附件清单'), element('pre', JSON.stringify(metadata, null, 2))); detail.append(context);
  const list = element('div'), preview = element('pre', '选择文件以预览。');
  const browse = element('button', '查看日志包内文件'); detail.append(browse, list, preview);
  action(browse, async () => {
    status('正在读取日志包…'); const files = await api(`${base}/files`); if (current !== selection) return;
    list.replaceChildren();
    for (const file of files.filter(file => !file.directory)) {
      const row = element('div'); row.className = 'file-row';
      const button = element('button', `${file.name} (${file.size.toLocaleString()} B)`);
      const path = `${base}/file?index=${file.index}`;
      action(button, async () => { status('正在读取文件…'); const data = await api(path); if (current !== selection) return;
        preview.textContent = data.binary ? '这是二进制文件，请下载查看。' : `${data.truncated ? '预览仅显示前 512 KiB；下载可获取完整文件。\n\n' : ''}${data.text}`;
        status(`已读取 ${data.name}。`); });
      row.append(button, link('下载', `${path}&download=1`)); list.append(row);
    }
    status(`已读取 ${files.length} 个包内条目。`);
  });
  status('反馈已载入。');
}
function setDirty() { dirty = true; $('#dirty').textContent = '有尚未保存的修改。'; }
function capture() {
  if (!key) return;
  const texts = Object.fromEntries(['zhs', 'eng', 'jpn'].map(lang => [lang, $(`#copy-${lang}`).value]));
  if (JSON.stringify(texts) === JSON.stringify(defaults[key])) delete draft.overrides[key]; else draft.overrides[key] = texts;
}
function selectCopy(next) {
  capture(); key = next; $('#copy-key').textContent = `原文：${key}`;
  for (const lang of ['zhs', 'eng', 'jpn']) $(`#copy-${lang}`).value = (draft.overrides[key] ?? defaults[key])[lang];
}
function showCopyList() {
  const query = $('#copy-search').value.toLowerCase(); $('#copy-list').replaceChildren();
  for (const item of Object.keys(defaults).filter(key => JSON.stringify([key, defaults[key]]).toLowerCase().includes(query))) {
    const button = element('button', `${Object.hasOwn(draft.overrides, item) ? '● ' : ''}${item}`);
    button.onclick = () => selectCopy(item); $('#copy-list').append(button);
  }
}
function showPublication(receipt) {
  const panel = $('#publication'); panel.replaceChildren(); if (!receipt) return;
  panel.append(element('span', receipt.message ?? ({ checks: 'PR 已创建，等待 CI。', merged: 'PR 已合并，等待 Pages 部署。' }[receipt.state] ?? receipt.state)));
  if (receipt.url) panel.append(document.createTextNode(' '), link('查看 PR ↗', receipt.url));
}
async function saveDraft() {
  capture(); draft = await api('/api/copy', 'POST', draft); dirty = false;
  $('#dirty').textContent = '草稿已保存在本机。'; showCopyList(); status('草稿已保存，可打开预览。');
}
$('#feedback-search').oninput = showFeedbackList;
$('#copy-search').oninput = showCopyList;
$('#copy-form').onsubmit = event => event.preventDefault();
for (const lang of ['zhs', 'eng', 'jpn']) $(`#copy-${lang}`).oninput = setDirty;
$('#reset').onclick = () => { if (!key) return; for (const lang of ['zhs', 'eng', 'jpn']) $(`#copy-${lang}`).value = defaults[key][lang]; capture(); setDirty(); };
$('#preview').onclick = event => { if (dirty) { event.preventDefault(); status('请先保存草稿，再预览。'); } };
action($('#refresh'), async () => { status('正在同步 Cloudflare 玩家反馈…'); await api('/api/refresh', 'POST'); await loadFeedbackView(); status('同步结束，详情见反馈状态。'); });
action($('#save'), saveDraft);
action($('#publish'), async () => { if (dirty) throw new Error('请先保存并预览草稿，再发布。'); status('正在创建网站文案 PR…'); showPublication(await api('/api/copy/publish', 'POST')); status('发布请求已处理，请查看 PR 状态。'); });
action($('#continue'), async () => { status('正在检查 PR 与 CI…'); showPublication(await api('/api/copy/continue', 'POST')); status('发布状态已更新。'); });
window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
try {
  const data = await api('/api/copy'); defaults = data.defaults; draft = data.draft;
  showCopyList(); selectCopy('DOMO，玩家=SAN。'); showPublication(data.publication);
  await loadFeedbackView(); status('后台已就绪。点击“同步玩家反馈”读取最新来信。');
} catch (error) { status(error.message); }
