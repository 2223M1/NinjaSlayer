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
let feedbackSource = { state: 'unloaded' }, feedbackWarnings = [];
let feedback = [], defaults, draft, key, dirty = false, selection = 0, reviewDirty = false;
const reviewName = value => value === 'resolved' ? '已解决' : '未解决';
function leaveReview() { return !reviewDirty || window.confirm('此反馈有尚未保存的修改，放弃这些修改？'); }
function showFeedbackList() {
  const query = $('#feedback-search').value.toLowerCase();
  const list = $('#feedback-list'); list.replaceChildren();
  for (const item of feedback.filter(item => JSON.stringify(item).toLowerCase().includes(query)
      && (!$('#feedback-status').value || (item.review?.status ?? 'unresolved') === $('#feedback-status').value))) {
    const button = element('button', `${reviewName(item.review?.status)} · ${item.at?.slice(0, 10) ?? ''} · ${item.category ?? item.context?.category ?? '玩家反馈'}`);
    button.append(element('small', String(item.description ?? item.message ?? item.id).slice(0, 120)));
    action(button, () => showFeedback(item)); list.append(button);
  }
  if (!list.children.length) {
    const message = feedbackSource.state === 'loading' ? '正在读取玩家来信，请稍候…'
      : feedbackSource.state === 'error' ? '来信读取失败，请重试同步；这不代表没有来信。'
      : feedbackSource.state !== 'ready' ? '尚未读取玩家来信。'
      : feedbackWarnings.length ? '有来信未能读取，请查看读取警告后重试。'
      : '没有符合条件的来信。';
    list.append(element('p', message));
  }
}
async function loadFeedbackView() {
  const data = await api('/api/view'); feedback = data.feedback;
  feedbackSource = data.sources.feedback; feedbackWarnings = data.feedbackWarnings;
  $('#feedback-state').textContent = `共 ${feedback.length} 条 · ${data.sources.feedback.state === 'ready' ? '已同步' : data.sources.feedback.message ?? '尚未同步'}${data.feedbackWarnings.length ? ` · ${data.feedbackWarnings.length} 条读取警告` : ''}`;
  if (data.feedbackWarnings.length) status(data.feedbackWarnings.join('\n'));
  showFeedbackList();
  return feedbackSource.state;
}
async function refreshFeedback() {
  if (!leaveReview()) return;
  const button = $('#refresh'); button.disabled = true;
  reviewDirty = false; selection++;
  $('#feedback-detail').replaceChildren(element('p', '请重新选择一封来信。'));
  feedbackSource = { state: 'loading' };
  $('#feedback-state').textContent = '正在同步 Cloudflare 玩家反馈…';
  showFeedbackList(); status('正在同步 Cloudflare 玩家反馈…');
  try {
    await api('/api/refresh', 'POST');
    await loadFeedbackView();
    if (!feedbackWarnings.length) status(feedbackSource.state === 'ready' ? '玩家来信已同步。' : feedbackSource.message ?? '反馈未能同步，请重试。');
  } catch (error) {
    feedbackSource = { state: 'error', message: error.message };
    $('#feedback-state').textContent = '同步失败；已有列表保留。';
    showFeedbackList(); status(error.message);
  } finally { button.disabled = false; }
}
async function showFeedback(item) {
  if (!leaveReview()) return;
  reviewDirty = false;
  const current = ++selection, detail = $('#feedback-detail');
  const loading = element('p', '正在读取处理状态，截图将随后加载…');
  detail.replaceChildren(element('h3', `${item.at?.slice(0, 10) ?? ''} · ${item.id}`),
    element('pre', item.description ?? item.message ?? ''), loading);
  const base = `/api/feedback/${encodeURIComponent(item.id)}`;
  let metadata, review;
  try { ({ metadata, review } = await api(`${base}/details`)); }
  catch (error) { if (current === selection) loading.textContent = `读取失败：${error.message} 请重新选择来信重试。`; throw error; }
  if (current !== selection) return;
  detail.replaceChildren(element('h3', `${item.at?.slice(0, 10) ?? ''} · ${item.id}`));
  const body = element('pre', JSON.stringify(metadata.payload, null, 2)); detail.append(body);
  const reviewForm = element('form'); reviewForm.className = 'review-form';
  const stateLabel = element('label', '解决状态'), state = element('select'); state.id = 'review-status'; stateLabel.htmlFor = state.id;
  for (const value of ['unresolved', 'resolved']) { const option = element('option', reviewName(value)); option.value = value; state.append(option); }
  state.value = review.status;
  const replyLabel = element('label', '作者回应（公开反馈会同步到官网）'), reply = element('textarea');
  reply.id = 'review-reply'; replyLabel.htmlFor = reply.id; reply.rows = 5; reply.maxLength = 5000; reply.value = review.reply;
  const save = element('button', '保存状态与回应'); save.type = 'button';
  const saved = element('p', review.updatedAt ? `上次保存：${review.updatedAt}` : '尚无作者处理记录，默认未解决。'); saved.setAttribute('role', 'status');
  reviewForm.onsubmit = event => event.preventDefault();
  state.onchange = reply.oninput = () => { reviewDirty = true; saved.textContent = '有尚未保存的反馈修改。'; };
  action(save, async () => {
    state.disabled = reply.disabled = true;
    save.textContent = '正在保存…'; saved.textContent = '正在保存到 Cloudflare，请稍候…';
    status('正在保存反馈处理记录…');
    try {
      const result = await api(`${base}/review`, 'PUT', { status: state.value, reply: reply.value });
      item.review = result; showFeedbackList();
      if (current === selection) { reviewDirty = false; saved.textContent = '已保存到 Cloudflare。官网将在定时同步或立即同步部署后更新。'; }
      status('反馈处理记录已保存。');
    } catch (error) {
      if (current === selection) saved.textContent = `保存未获确认：${error.message} 输入内容已保留，可重试。`;
      throw error;
    } finally { state.disabled = reply.disabled = false; save.textContent = '保存状态与回应'; }
  });
  reviewForm.append(stateLabel, state, replyLabel, reply, save, saved); detail.append(reviewForm);
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
$('#feedback-status').onchange = showFeedbackList;
$('#copy-search').oninput = showCopyList;
$('#copy-form').onsubmit = event => event.preventDefault();
for (const lang of ['zhs', 'eng', 'jpn']) $(`#copy-${lang}`).oninput = setDirty;
$('#reset').onclick = () => { if (!key) return; for (const lang of ['zhs', 'eng', 'jpn']) $(`#copy-${lang}`).value = defaults[key][lang]; capture(); setDirty(); };
$('#preview').onclick = event => { if (dirty) { event.preventDefault(); status('请先保存草稿，再预览。'); } };
action($('#refresh'), refreshFeedback);
function showFeedbackPublication(receipt) {
  const panel = $('#feedback-publication'); panel.replaceChildren();
  if (!receipt) return;
  panel.append(element('span', receipt.message));
  if (receipt.url) panel.append(document.createTextNode(' '), link('查看部署任务 ↗', receipt.url));
}
action($('#feedback-publish'), async () => { if (reviewDirty) throw new Error('请先保存反馈修改。'); showFeedbackPublication(await api('/api/feedback-publication', 'POST')); });
action($('#feedback-publication-refresh'), async () => showFeedbackPublication(await api('/api/feedback-publication')));
action($('#save'), saveDraft);
action($('#publish'), async () => { if (dirty) throw new Error('请先保存并预览草稿，再发布。'); status('正在创建网站文案 PR…'); showPublication(await api('/api/copy/publish', 'POST')); status('发布请求已处理，请查看 PR 状态。'); });
action($('#continue'), async () => { status('正在检查 PR 与 CI…'); showPublication(await api('/api/copy/continue', 'POST')); status('发布状态已更新。'); });
window.addEventListener('beforeunload', event => { if (dirty || reviewDirty) { event.preventDefault(); event.returnValue = ''; } });
try {
  const data = await api('/api/copy'); defaults = data.defaults; draft = data.draft;
  showCopyList(); selectCopy('DOMO，玩家=SAN。'); showPublication(data.publication);
} catch (error) { status(error.message); }
try {
  if (await loadFeedbackView() !== 'ready') await refreshFeedback();
} catch (error) {
  feedbackSource = { state: 'error', message: error.message };
  showFeedbackList(); status(error.message);
}
