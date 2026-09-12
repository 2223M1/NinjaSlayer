import { summarizePublic } from './public-data.mjs';
import { renderCharts } from './chart-view.mjs';
import { renderReports } from './replay-view.mjs';
import { cardPreview, loadCatalog } from './catalog-view.mjs';
import { wilson } from './charts.mjs';
const isPages = document.body.dataset.view === 'pages';
const isPublic = document.body.dataset.view !== 'admin';
const $ = selector => document.querySelector(selector);
const el = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
};
const percent = (n, d) => d ? `${(100 * n / d).toFixed(1)}%` : '—';
const time = value => new Date(value).toLocaleString('zh-CN', { hour12: false });
const categoryName = value => ({ bug: '问题报告', balance: '平衡建议', feedback: '其他反馈', translation: '文本问题' }[value] ?? value);
let view, snapshot, displayedCards = [], sort = 'pickRate', direction = -1, page = 'cards', toastTimer;
const filterIds = ['days', 'version', 'ascension', 'gameVersion', 'mode', 'party', 'reloads', 'outcome', 'a10', 'from', 'to'];

async function api(path, options) {
  const response = await fetch(path, options);
  const data = await response.json();
  if (!response.ok) throw new Error(data.error ?? `请求失败（${response.status}）`);
  return data;
}
function toast(message) {
  clearTimeout(toastTimer);
  $('#toast').textContent = message;
  $('#toast').hidden = false;
  toastTimer = setTimeout(() => { $('#toast').hidden = true; }, 6000);
}
function fillOptions(select, values, first, label = value => value) {
  const previous = select.value;
  select.replaceChildren(new Option(first, ''), ...values.map(value => new Option(label(value), value)));
  if (values.map(String).includes(previous)) select.value = previous;
}
async function loadView() {
  const query = new URLSearchParams(Object.fromEntries(filterIds.map(key => [key, $(`#${key}`).value])));
  snapshot = await api(isPages ? './data.json' : '/api/snapshot');
  view = isPages ? summarizePublic(snapshot, Object.fromEntries(query)) : await api(`/api/view?${query}`);
  const versionCatalog = await loadCatalog(query.get('version') || snapshot.currentVersion);
  const models = new Map((versionCatalog?.languages.zhs ?? []).map(model => [model.id, model]));
  snapshot.entities = models;
  snapshot.labels = versionCatalog?.labels?.zhs ?? {};
  const measuredVersions = [...new Set(snapshot.groups.flatMap(group => (group.mechanisms ?? []).map(row => row.version)))];
  snapshot.measuredContent = new Map(await Promise.all(measuredVersions.map(async version => {
    const catalog = await loadCatalog(version);
    return [version, new Map((catalog?.languages.zhs ?? []).map(model => [model.id, model]))];
  })));
  const cardLabels = card => {
    const model = models.get(card.id);
    return { ...card, name: model?.variants?.[0].name ?? card.id, image: model?.image, thumbnail: model?.thumbnail };
  };
  view.cards = view.cards.map(cardLabels);
  snapshot.catalog = snapshot.catalog.map(cardLabels);
  fillOptions($('#version'), view.versions, '所有版本');
  fillOptions($('#ascension'), view.ascensions, '所有进阶', value => `进阶 ${value}`);
  fillOptions($('#gameVersion'), view.gameVersions, '所有宿主');
  fillOptions($('#mode'), view.modes, '所有模式');
  fillOptions($('#feedback-category'), [...new Set(view.feedback.map(item => item.category))].sort(), '全部分类', categoryName);
  const connected = view.sources.telemetry.state === 'ready' || Boolean(view.sources.telemetry.at);
  $('#metric-runs').textContent = connected ? view.runs.toLocaleString() : '—';
  $('#metric-win').textContent = connected ? percent(view.wins, view.runs) : '—';
  $('#metric-floor').textContent = view.averageFloor === null ? '—' : view.averageFloor.toFixed(1);
  $('#metric-feedback').textContent = view.sources.feedback.at ? view.feedback.length.toLocaleString() : '—';
  $('#feedback-nav-count').textContent = view.sources.feedback.at ? view.feedback.length : '—';
  $('#sample-detail').textContent = connected ? `${view.playerSamples} 个忍者杀手角色样本 · 已去重` : '等待连接对局数据';
  $('#win-detail').textContent = connected ? `${view.wins} 场通关 / ${view.runs} 场完成对局` : '已完成且未放弃的对局';
  for (const [key, name] of [['telemetry', '对局统计'], ['feedback', '玩家反馈'], ['replays', '公开战报']]) {
    const source = view.sources[key] ?? {state:'unloaded'};
    $(`#${key}-state`).textContent = `${name} · ${{ ready: source.label ?? '已同步', error: '同步失败', unconnected: '未连接', unloaded: '等待同步' }[source.state]}`;
    $(`#${key}-state`).className = `source-pill ${source.state}`;
  }
  const latest = [view.sources.telemetry.at, view.sources.feedback.at].filter(Boolean).sort().at(-1);
  $('#last-sync').textContent = latest ? `最近读取 ${time(latest)}` : '连接后显示真实数据';
  $('#footer-time').textContent = new Date().toLocaleDateString('zh-CN');
  const notices = Object.values(view.sources).filter(source => source.message).map(source => source.message);
  if (view.sources.telemetry.truncated) notices.push('当前载入最近 50,000 条事件，未覆盖全部历史；筛选只作用于已载入范围。');
  if (view.rejected) notices.push(`${view.rejected} 条无效、放弃或非忍者杀手记录未计入统计。`);
  if (view.conflicts) notices.push(`${view.conflicts} 场重复上传的胜负冲突，已排除。`);
  if (view.invalidCombats) notices.push(`${view.invalidCombats} 条战斗测量不完整或与对局不匹配，未计入使用次数。`);
  if (view.feedbackWarnings.length) notices.push(`${view.feedbackWarnings.length} 条反馈索引未能完整读取，请稍后重试。`);
  $('#notice').textContent = notices.join(' ');
  $('#notice').hidden = !notices.length;
  $('#combat-coverage').textContent = `战斗测量覆盖：${view.measuredCombats} / ${view.totalCombats} 个角色战斗。旧版或未采集记录不补零；跨版本读档时，版本筛选也会过滤测量时的版本。`;
  renderCards(); renderTrend(); renderFeedback();
  if (page === 'charts') renderCharts(snapshot, Object.fromEntries(query), openCardId);
  if (page === 'reports') renderReports(snapshot, Object.fromEntries(query), openCardId);
}
function renderCards() {
  const use = $('#table-mode').value === 'use';
  const headings = use
    ? [['卡牌名称', 'name'], ['稀有度'], ['抽到', 'drawn'], ['手动打出', 'manual_plays'], ['自动打出', 'auto_plays'], ['完成结算', 'finished'], ['实付能量', 'energy_spent'], ['涉及战斗', 'combatSamples']]
    : [['卡牌名称', 'name'], ['稀有度'], ['提供次数', 'offered'], ['选中次数', 'picked'], ['抓取率', 'pickRate'], ['持有率', 'held'], ['持有通关率', 'winRate'], ['样本']];
  const head = el('tr');
  for (const [index, [label, field]] of headings.entries()) {
    const cell = el('th', index > 1 ? 'number' : '');
    if (field) {
      const button = el('button', field === sort ? 'sorted' : '', label + (field === sort ? direction < 0 ? ' ↓' : ' ↑' : ''));
      button.addEventListener('click', () => { direction = sort === field ? -direction : -1; sort = field; renderCards(); });
      cell.setAttribute('aria-sort', field === sort ? direction < 0 ? 'descending' : 'ascending' : 'none');
      cell.append(button);
    } else cell.textContent = label;
    head.append(cell);
  }
  $('#card-head').replaceChildren(head);
  const search = $('#card-search').value.trim().toLocaleLowerCase();
  const rarity = $('#rarity').value;
  const value = card => sort === 'pickRate' ? (card.offered ? card.picked / card.offered : -1)
    : sort === 'winRate' ? (card.held ? card.wins / card.held : -1) : card[sort];
  displayedCards = view.cards.filter(card => (!rarity || card.rarity === rarity)
    && `${card.name} ${card.id}`.toLocaleLowerCase().includes(search))
    .sort((a, b) => (sort === 'name' ? a.name.localeCompare(b.name, 'zh-CN') : value(a) - value(b)) * direction || b.offered - a.offered || a.name.localeCompare(b.name, 'zh-CN'));
  const rows = document.createDocumentFragment();
  for (const card of displayedCards) {
    const row = el('tr');
    const name = el('td'), link = el('button', 'card-link');
    if (card.thumbnail) { const image = el('img', 'card-thumbnail'); image.src = `./content/${card.thumbnail}`; image.alt = ''; image.loading = 'lazy'; link.append(image); }
    link.append(el('span', 'card-name', card.name), el('span', 'card-type', card.type));
    link.addEventListener('click', () => openCard(card)); name.append(link); name.title = card.id;
    const rarityCell = el('td'); rarityCell.append(el('span', `badge ${card.rarity === '蓝卡' ? 'blue' : card.rarity === '金卡' ? 'gold' : ''}`, card.rarity));
    if (use) {
      row.append(name, rarityCell, ...['drawn', 'manual_plays', 'auto_plays', 'finished', 'energy_spent', 'combatSamples']
        .map(field => el('td', 'number', card.combatSamples ? card[field].toLocaleString() : '—')));
      rows.append(row); continue;
    }
    const rateCell = el('td', 'number');
    const rate = el('span', 'rate', percent(card.picked, card.offered));
    if (card.offered) { const ci = wilson(card.picked, card.offered); rate.title = `Wilson 95% CI ${(ci[0] * 100).toFixed(1)}–${(ci[1] * 100).toFixed(1)}% · n=${card.offered}`; }
    if (card.offered) {
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('viewBox', '0 0 100 10'); svg.setAttribute('aria-hidden', 'true');
      for (const [width, color] of [[100, '#302727'], [100 * card.picked / card.offered, '#ff0202']]) {
        const rect = document.createElementNS(svg.namespaceURI, 'rect');
        rect.setAttribute('width', width); rect.setAttribute('height', '10'); rect.setAttribute('rx', '3'); rect.setAttribute('fill', color); svg.append(rect);
      }
      rate.prepend(svg);
    }
    rateCell.append(rate);
    const held = el('td', 'number', percent(card.held, view.playerSamples)); held.title = `${card.held} / ${view.playerSamples} 个角色结束牌组持有`;
    const win = el('td', 'number', percent(card.wins, card.held)); win.title = `${card.wins} / ${card.held} 个持有样本通关`;
    row.append(name, rarityCell, el('td', 'number', card.offered || '—'), el('td', 'number', card.picked || (card.offered ? '0' : '—')), rateCell, held, win,
      el('td', card.offered > 0 && card.offered < 20 ? 'small-sample' : 'muted', card.offered === 0 ? '暂无提供' : card.offered < 20 ? '小样本' : '≥ 20 次'));
    rows.append(row);
  }
  if (!displayedCards.length) {
    const cell = el('td', 'empty', '没有符合筛选的卡牌。'); cell.colSpan = 8;
    const row = el('tr'); row.append(cell); rows.append(row);
  }
  $('#card-rows').replaceChildren(rows);
  $('#card-count').textContent = displayedCards.length;
  $('#table-summary').textContent = `${displayedCards.length} 张卡牌 · ${view.playerSamples} 个角色样本`;
}
function openCardId(id, version) {
  const card = view.cards.find(card => card.id === id);
  if (card) openCard(card, version);
  else { $('#card-detail').replaceChildren(); $('#card-detail-title').textContent = id;
    cardPreview(id, version || $('#version').value || snapshot.currentVersion, $('#card-preview'), openCardId).catch(error => toast(error.message));
    if (!$('#card-dialog').open) $('#card-dialog').showModal(); }
}
function openCard(card, version) {
  $('#card-detail-title').textContent = card.name;
  const grid = el('div', 'detail-grid');
  const countRate = (n, d) => `${percent(n, d)} (${n}/${d})`;
  const use = field => card.combatSamples ? card[field].toLocaleString() : '—';
  for (const [label, value] of [
    ['抓取率', countRate(card.picked, card.offered)], ['平均抓取楼层', card.picked ? (card.pickFloorTotal / card.picked).toFixed(1) : '—'],
    ['曾选中此牌的对局通关率', countRate(card.chosenWins, card.chosenRuns)], ['提供过但从未选中的通关率', countRate(card.skippedWins, card.skippedRuns)],
    ['结束牌组持有通关率', countRate(card.wins, card.held)], ['移除 / 升级记录', `${card.removed} / ${card.upgraded}`],
    ['抽到次数', use('drawn')], ['手动 / 自动打出', `${use('manual_plays')} / ${use('auto_plays')}`],
    ['开始 / 完成结算（含重复）', `${use('started')} / ${use('finished')}`], ['实付能量 / 星数', `${use('energy_spent')} / ${use('stars_spent')}`],
  ]) {
    const box = el('div'); box.append(el('small', '', label), el('strong', '', value)); grid.append(box);
  }
  $('#card-detail').replaceChildren(grid);
  cardPreview(card.id, version || $('#version').value || snapshot.currentVersion, $('#card-preview'), openCardId).catch(error => toast(error.message));
  if (!$('#card-dialog').open) $('#card-dialog').showModal();
}
function renderTrend() {
  const entries = view.trend.slice(-14);
  if (!entries.length) { $('#trend').replaceChildren(el('span', '', '当前范围暂无对局，连接数据或调整筛选后查看。')); return; }
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 560 115'); svg.setAttribute('role', 'img'); svg.setAttribute('aria-label', '最近十四个有记录日期的对局与通关数量');
  const max = Math.max(...entries.map(entry => entry.runs));
  const step = 550 / entries.length;
  entries.forEach((entry, index) => {
    for (const [n, color, dx] of [[entry.runs, '#625454', 0], [entry.wins, '#ff0202', Math.min(step * .28, 12)]]) {
      const rect = document.createElementNS(svg.namespaceURI, 'rect');
      for (const [attr, val] of Object.entries({ x: 8 + index * step + dx, y: 85 - n / max * 68, width: Math.min(step * .26, 11), height: n / max * 68, fill: color, rx: 2 })) rect.setAttribute(attr, val);
      const title = document.createElementNS(svg.namespaceURI, 'title'); title.textContent = `${entry.date}：${entry.runs} 场对局，${entry.wins} 场通关`; rect.append(title); svg.append(rect);
    }
    for (const [y, text] of [[11, entry.runs], [109, entry.date.slice(5)]]) {
      const label = document.createElementNS(svg.namespaceURI, 'text'); label.setAttribute('x', 7 + index * step); label.setAttribute('y', y); label.textContent = text; svg.append(label);
    }
  });
  $('#trend').replaceChildren(svg);
}
function renderFeedback() {
  const query = $('#feedback-search').value.trim().toLocaleLowerCase(), category = $('#feedback-category').value;
  const records = view.feedback.filter(item => (!category || item.category === category)
    && `${item.description} ${item.context.modVersion}`.toLocaleLowerCase().includes(query));
  const fragment = document.createDocumentFragment();
  for (const item of records) {
    const row = el('button', 'feedback-row');
    const body = el('span', 'feedback-body');
    body.append(el('strong', '', item.description), el('small', '', `${categoryName(item.category)} · v${item.context.modVersion} · ${item.gameVersion ?? '游戏版本未记录'}`));
    row.append(el('span', 'feedback-icon', '◫'), body, el('time', '', time(item.at)), el('span', 'muted', '↗'));
    row.addEventListener('click', () => openFeedback(item)); fragment.append(row);
  }
  if (!records.length) {
    const empty = el('div', 'empty');
    empty.append(el('strong', '', view.sources.feedback.at ? '暂时没有反馈' : '还未读取玩家反馈'),
      el('span', '', view.sources.feedback.at ? '当前筛选中没有已完成的反馈。新反馈将在下次同步时出现。' : isPublic ? '等待网站同步玩家通过 F2 提交的公开反馈。' : '点击“同步数据”，读取玩家通过 F2 提交的反馈。'));
    fragment.append(empty);
  }
  $('#feedback-list').replaceChildren(fragment);
}
function openFeedback(item) {
  $('#feedback-detail-title').textContent = categoryName(item.category);
  $('#feedback-description').textContent = item.description;
  const metadata = [time(item.at), `模组 ${item.context.modVersion}`, `游戏 ${item.gameVersion ?? '—'}`];
  if (!isPublic) metadata.push(`进阶 ${item.context.ascensionLevel ?? '—'}`, `楼层 ${item.context.totalFloor ?? '—'}`);
  $('#feedback-detail-meta').replaceChildren(...metadata.map(text => el('span', 'badge', text)));
  if (!isPublic) {
  const path = `/api/feedback/${encodeURIComponent(item.id)}`;
  $('#screenshot-link').href = `${path}/screenshot`; $('#logs-link').href = `${path}/logs`;
  $('#screenshot-error').hidden = true; $('#feedback-screenshot').hidden = false;
  $('#feedback-screenshot').src = `${path}/screenshot`;
  }
  $('#feedback-dialog').showModal();
}
async function refresh() {
  $('#refresh').disabled = true; $('#refresh-icon').classList.add('spinning');
  try { if (!isPublic) await api('/api/refresh', { method: 'POST' }); await loadView(); }
  catch (error) { toast(error.message); }
  finally { $('#refresh').disabled = false; $('#refresh-icon').classList.remove('spinning'); }
}
function openConnection() {
  const form = $('#connection-form');
  form.elements.host.value = view.connection.host; form.elements.projectId.value = view.connection.projectId;
  $('#connection-error').textContent = ''; $('#connection-dialog').showModal();
}
if (!isPublic) for (const id of ['connect-nav', 'connection-button']) $(`#${id}`).addEventListener('click', openConnection);
for (const button of document.querySelectorAll('[data-close]')) button.addEventListener('click', () => $(`#${button.dataset.close}`).close());
for (const button of document.querySelectorAll('[data-page]')) button.addEventListener('click', () => {
  page = button.dataset.page;
  for (const nav of document.querySelectorAll('[data-page]')) nav.classList.toggle('active', nav === button);
  for (const name of ['cards', 'charts', 'reports', 'feedback']) $(`#${name}-page`).hidden = page !== name;
  $('#export').hidden = !['cards','charts'].includes(page);
  $('#page-title').textContent = { cards: '卡池观察', charts: '平衡图表', reports: '公开战报', feedback: '玩家来信' }[page];
  $('#page-subtitle').textContent = '真实样本 · 原生记录 · 忍者杀手';
  const url = new URL(location.href); url.searchParams.set('page', page); history.replaceState(null, '', url);
  loadView().catch(error => toast(error.message));
});
for (const id of filterIds) $(`#${id}`).addEventListener('change', () => {
  const url = new URL(location.href);
  for (const key of filterIds) { if ($(`#${key}`).value) url.searchParams.set(key, $(`#${key}`).value); else url.searchParams.delete(key); }
  history.replaceState(null, '', url); loadView().catch(error => toast(error.message));
});
$('#table-mode').addEventListener('change', () => { sort = $('#table-mode').value === 'use' ? 'finished' : 'pickRate'; direction = -1; renderCards(); });
for (const id of ['card-search', 'rarity']) $(`#${id}`).addEventListener('input', renderCards);
for (const id of ['feedback-search', 'feedback-category']) $(`#${id}`).addEventListener('input', renderFeedback);
$('#refresh').addEventListener('click', refresh);
if (!isPublic) {
$('#feedback-screenshot').addEventListener('error', () => { $('#feedback-screenshot').hidden = true; $('#screenshot-error').hidden = false; });
$('#connection-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget, button = form.querySelector('[type="submit"]'); button.disabled = true;
  try {
    await api('/api/connect', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.fromEntries(new FormData(form))) });
    form.elements.key.value = ''; $('#connection-dialog').close(); await refresh();
  } catch (error) { $('#connection-error').textContent = error.message; }
  finally { button.disabled = false; }
});
$('#import-file').addEventListener('change', async event => {
  const file = event.target.files[0]; if (!file) return;
  try {
    const result = await api('/api/import', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: await file.text() });
    for (const id of filterIds) $(`#${id}`).value = '';
    $('#connection-dialog').close(); await loadView(); toast(`已导入 ${result.runs} 场对局；当前显示全部导入日期。`);
  } catch (error) { $('#connection-error').textContent = error.message; }
  finally { event.target.value = ''; }
});
}
$('#export').addEventListener('click', () => {
  const cell = value => `"${String(value).replace(/^[=+@-]/, "'$&").replaceAll('"', '""')}"`;
  const rows = page==='charts' ? [['图表', $('#chart-title').textContent],['说明', $('#chart-note').textContent],['分组','统计值','样本 n','95% 置信区间'],
    ...[...$('#chart-rows').rows].map(row=>[...row.cells].map(cell=>cell.textContent))] : [['卡牌', '模型 ID', '稀有度', '提供次数', '选中次数', '抓取率', '持有角色数', '持有率', '持有通关率', '平均抓取楼层', '曾抓取样本', '曾抓取通关', '只跳过样本', '只跳过通关', '移除', '升级', '涉及战斗', '抽到', '手动打出', '自动打出', '开始结算', '完成结算', '实付能量', '实付星数'],
    ...displayedCards.map(card => [card.name, card.id, card.rarity, card.offered, card.picked, percent(card.picked, card.offered), card.held, percent(card.held, view.playerSamples), percent(card.wins, card.held),
      card.picked ? card.pickFloorTotal / card.picked : '', card.chosenRuns, card.chosenWins, card.skippedRuns, card.skippedWins, card.removed, card.upgraded,
      card.combatSamples, ...['drawn', 'manual_plays', 'auto_plays', 'started', 'finished', 'energy_spent', 'stars_spent'].map(field => card.combatSamples ? card[field] : '')])];
  const url = URL.createObjectURL(new Blob(['\ufeff', rows.map(row => row.map(cell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' }));
  const link = el('a'); link.href = url; link.download = `忍者杀手_${page==='charts'?'图表数据':'卡池统计'}_${new Date().toISOString().slice(0, 10)}.csv`; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});
await loadView().then(async () => {
  const params = new URLSearchParams(location.search);
  for (const key of filterIds) if (params.has(key)) $(`#${key}`).value = params.get(key);
  const requested = params.get('page');
  if (['cards', 'charts', 'reports', 'feedback'].includes(requested)) document.querySelector(`[data-page="${requested}"]`).click();
  else await loadView();
  if (!isPublic) return refresh();
}).catch(error => toast(error.message));
setInterval(() => loadView().catch(error => toast(error.message)), 60_000);
