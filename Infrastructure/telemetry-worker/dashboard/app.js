import { t, tr, locale, language, localizePage } from './i18n.mjs';
import { summarizePublic } from './public-data.mjs';
import { renderCharts, renderMechanisms } from './chart-view.mjs';
import { renderReports } from './replay-view.mjs';
import { cardPreview, loadCatalog, plain } from './catalog-view.mjs';
const isPages = document.body.dataset.view === 'pages';
const isPublic = document.body.dataset.view !== 'admin';
localizePage();
const $ = selector => document.querySelector(selector);
const el = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
};
const ratio = (n, d) => d ? n / d : null;
const percent = value => value === null ? '—' : `${(100 * value).toFixed(1)}%`;
const time = value => new Date(value).toLocaleString(locale, { hour12: false });
const categoryName = value => ({ bug: t('Bug'), balance: t('平衡建议'), feedback: t('其他想法'), translation: t('文本问题') }[value] ?? value);
const mainRarities = ['白卡', '蓝卡', '金卡', '初始', '先古'];
const rarityClass = rarity => ({ 白卡: 'common', 蓝卡: 'uncommon', 金卡: 'rare', 初始: 'basic', 先古: 'ancient' }[rarity] ?? 'special');
const SMALL_SAMPLE = 20;
const pages = {
  cards: [t('DOMO，玩家=SAN。'), t('这里是忍者杀手模组的情报站DESU。哪张牌被抢着要、哪张牌没人理，一眼就能看清。')],
  charts: [t('尖塔战况'), t('倒在哪、挨谁的揍最疼、牌组多厚最好赢——大家的血泪都在这儿。')],
  reports: [t('战报回放'), t('玩家分享的对局，可以一个回合一个回合地重看。')],
  feedback: [t('玩家来信'), t('发现 Bug 或者有想法？在游戏里按 F2 就能写信给作者。古事记上也是这么写的。')],
};
let view, snapshot, displayedCards = [], direction = -1, rarity = '', layout = 'grid', page = 'cards', toastTimer, viewRequest = 0;
const filterIds = ['days', 'ascension', 'party', 'outcome'];
const selectedVersions = new Set(new URLSearchParams(location.search).getAll('version').filter(Boolean));
const filters = () => ({ ...Object.fromEntries(filterIds.map(key => [key, $(`#${key}`).value])), version: [...selectedVersions] });
function filterParams() {
  const { version, ...single } = filters();
  const params = new URLSearchParams(single);
  for (const value of version) params.append('version', value);
  return params;
}
function renderVersionOptions(versions) {
  const values = [...new Set([...versions, ...selectedVersions])];
  const options = $('#version-options');
  if (options.dataset.versions !== JSON.stringify(values)) {
    options.replaceChildren(...values.map(value => {
      const label = el('label'), input = el('input');
      input.type = 'checkbox'; input.value = value;
      label.append(input, el('span', '', value));
      return label;
    }));
    options.dataset.versions = JSON.stringify(values);
  }
  for (const input of options.querySelectorAll('input')) input.checked = selectedVersions.has(input.value);
  $('#version-summary').textContent = selectedVersions.size === 0 ? t('所有版本')
    : selectedVersions.size === 1 ? [...selectedVersions][0] : tr`已选 ${selectedVersions.size} 个版本`;
}
const metric = {
  pickRate: card => ratio(card.picked, card.offered),
  winRate: card => ratio(card.wins, card.held),
  held: card => ratio(card.held, view.playerSamples),
  plays: card => ratio(card.manual_plays + card.auto_plays, card.combatSamples),
};

async function api(path, options) {
  const response = await fetch(path, options);
  const data = await response.json();
  if (!response.ok) throw new Error(data.error ?? tr`请求失败（${response.status}）`);
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
  const request = ++viewRequest;
  const query = filters();
  const params = filterParams();
  const nextSnapshot = await api(isPages ? './data.json' : '/api/snapshot', { cache: 'no-cache' });
  const nextView = isPages ? summarizePublic(nextSnapshot, query) : await api(`/api/view?${params}`);
  const versionCatalog = await loadCatalog(nextSnapshot.currentVersion);
  if (request !== viewRequest) return;
  snapshot = nextSnapshot; view = nextView;
  const catalogLanguage = versionCatalog?.languages[language] ? language : 'eng';
  const models = new Map((versionCatalog?.languages[catalogLanguage] ?? []).map(model => [model.id, model]));
  for (const card of view.cards) card.name = models.get(card.id)?.variants?.[0]?.name ?? card.name;
  snapshot.entities = models;
  snapshot.labels = versionCatalog?.labels?.[catalogLanguage] ?? {};
  renderVersionOptions(view.versions);
  fillOptions($('#ascension'), view.ascensions, t('所有进阶'), value => `A${value}`);
  fillOptions($('#feedback-category'), [...new Set(view.feedback.map(item => item.category))].sort(), t('全部分类'), categoryName);
  const connected = view.sources.telemetry.state === 'ready' || Boolean(view.sources.telemetry.at);
  $('#metric-runs').textContent = connected ? view.runs.toLocaleString(locale) : '—';
  $('#metric-win').textContent = connected ? percent(ratio(view.wins, view.runs)) : '—';
  $('#metric-floor').textContent = view.averageFloor === null ? '—' : view.averageFloor.toFixed(1);
  $('#feedback-nav-count').textContent = view.sources.feedback.at ? view.feedback.length : '—';
  const latest = [view.sources.telemetry.at, view.sources.feedback.at].filter(Boolean).sort().at(-1);
  $('#last-sync').textContent = latest ? tr`数据更新于 ${time(latest)}` : t('还没有数据');
  $('#catalog-version').textContent = tr`卡牌内容 v${snapshot.currentVersion}`;
  const notices = Object.values(view.sources).filter(source => source.message).map(source => t(source.message));
  if (versionCatalog && catalogLanguage !== language) notices.push(t('这个版本还没有所选语言的卡牌资料，先用英文顶一下。'));
  if (view.sources.telemetry.truncated) notices.push(t('只载入了最近 50,000 条记录，更早的对局没算进来。'));
  if (!isPublic && view.rejected) notices.push(tr`${view.rejected} 条无效、放弃或非忍者杀手记录未计入统计。`);
  if (!isPublic && view.conflicts) notices.push(tr`${view.conflicts} 场重复上传的胜负冲突，已排除。`);
  if (!isPublic && view.invalidCombats) notices.push(tr`${view.invalidCombats} 条战斗记录不完整，未计入使用次数。`);
  if (view.feedbackWarnings.length) notices.push(tr`${view.feedbackWarnings.length} 条反馈索引未能完整读取，请稍后重试。`);
  $('#notice').textContent = notices.join(' ');
  $('#notice').hidden = !notices.length;
  renderCards(); renderTrend(); renderFeedback();
  if (page === 'charts') {
    renderCharts(snapshot, query);
    if (!isPublic) renderMechanisms(snapshot, query, openCardId);
  }
  if (page === 'reports') renderReports(snapshot, query, openCardId);
}
function variantsOf(card) {
  return snapshot.entities.get(card.id)?.variants ?? [];
}
function costText(variant) {
  return !variant ? '' : variant.costsX ? 'X' : variant.cost < 0 ? '' : String(variant.cost);
}
function renderCards() {
  const sort = $('#card-sort').value;
  const search = $('#card-search').value.trim().toLocaleLowerCase(locale);
  const value = card => sort === 'name' ? 0 : metric[sort](card) ?? -1;
  displayedCards = view.cards.filter(card => (!rarity || (rarity === '其他' ? !mainRarities.includes(card.rarity) : card.rarity === rarity))
    && card.name.toLocaleLowerCase(locale).includes(search))
    .sort((a, b) => (sort === 'name' ? a.name.localeCompare(b.name, locale) : value(a) - value(b)) * direction
      || b.offered - a.offered || a.name.localeCompare(b.name, locale));
  $('#card-count').textContent = tr`${displayedCards.length} 张牌。`;
  $('#card-grid').hidden = layout !== 'grid';
  $('#card-list').hidden = layout !== 'list';
  if (layout === 'grid') renderGrid(); else renderList(sort);
}
function renderGrid() {
  const fragment = document.createDocumentFragment();
  for (const card of displayedCards) {
    const variants = variantsOf(card);
    const tile = el('article', `card-tile ${rarityClass(card.rarity)}${card.offered < SMALL_SAMPLE ? ' thin' : ''}`);
    const open = el('button', 'tile-open');
    const art = el('span', 'tile-art');
    if (card.image) { const image = el('img'); image.src = `./content/${card.image}`; image.alt = ''; image.loading = 'lazy'; image.decoding = 'async'; art.append(image); }
    const name = el('span', 'tile-name', card.name), description = el('span', 'tile-description', plain(variants[0]?.description));
    const cost = el('span', 'tile-cost', costText(variants[0]));
    art.append(cost);
    art.append(description);
    const pick = metric.pickRate(card);
    const bar = el('span', 'tile-bar'); bar.style.setProperty('--fill', `${100 * (pick ?? 0)}%`);
    const stats = el('span', 'tile-stats');
    for (const [label, rate] of [[t('抓取'), pick], [t('胜率'), metric.winRate(card)]]) {
      const stat = el('span', 'stat'); stat.append(el('b', '', percent(rate)), el('small', '', label)); stats.append(stat);
    }
    open.append(art, name, el('span', 'tile-meta', `${t(card.type)} · ${t(card.rarity)}`), bar, stats);
    open.addEventListener('click', () => openCard(card));
    tile.append(open);
    if (variants.length > 1) {
      const upgrade = el('button', 'tile-upgrade', '+');
      upgrade.setAttribute('aria-pressed', 'false');
      upgrade.setAttribute('aria-label', tr`切换 ${card.name} 的升级效果`);
      upgrade.addEventListener('click', () => {
        const upgraded = upgrade.getAttribute('aria-pressed') !== 'true';
        upgrade.setAttribute('aria-pressed', String(upgraded));
        const variant = variants[Number(upgraded)];
        name.textContent = variant.name; description.textContent = plain(variant.description);
        cost.textContent = costText(variant);
      });
      tile.append(upgrade);
    }
    fragment.append(tile);
  }
  if (!displayedCards.length) fragment.append(el('p', 'empty', t('AIEEEEEE!? 为什么一张牌都没有!? 换个筛选试试。')));
  $('#card-grid').replaceChildren(fragment);
}
function renderList(sort) {
  const columns = [[t('卡牌'), 'name'], [t('稀有度')], [t('被提供'), 'offered'], [t('抓取率'), 'pickRate'], [t('持有率'), 'held'], [t('持有胜率'), 'winRate'], [t('每场打出'), 'plays']];
  const head = el('tr');
  for (const [index, [label, field]] of columns.entries()) {
    const cell = el('th', index > 1 ? 'number' : '');
    if (field && field !== 'offered') {
      const button = el('button', field === sort ? 'sorted' : '', label + (field === sort ? direction < 0 ? ' ↓' : ' ↑' : ''));
      button.addEventListener('click', () => { direction = sort === field ? -direction : -1; $('#card-sort').value = field; renderCards(); });
      cell.setAttribute('aria-sort', field === sort ? direction < 0 ? 'descending' : 'ascending' : 'none');
      cell.append(button);
    } else cell.textContent = label;
    head.append(cell);
  }
  $('#card-head').replaceChildren(head);
  const rows = document.createDocumentFragment();
  for (const card of displayedCards) {
    const row = el('tr', card.offered < SMALL_SAMPLE ? 'thin' : '');
    const name = el('td'), link = el('button', 'card-link');
    if (card.thumbnail) { const image = el('img', 'card-thumbnail'); image.src = `./content/${card.thumbnail}`; image.alt = ''; image.loading = 'lazy'; link.append(image); }
    link.append(el('span', '', card.name)); link.addEventListener('click', () => openCard(card)); name.append(link);
    const rarityCell = el('td'); rarityCell.append(el('span', `badge ${rarityClass(card.rarity)}`, t(card.rarity)));
    const plays = metric.plays(card);
    row.append(name, rarityCell, el('td', 'number', card.offered ? card.offered.toLocaleString(locale) : '—'),
      el('td', 'number', percent(metric.pickRate(card))), el('td', 'number', percent(metric.held(card))),
      el('td', 'number', percent(metric.winRate(card))), el('td', 'number', plays === null ? '—' : plays.toFixed(1)));
    rows.append(row);
  }
  if (!displayedCards.length) {
    const cell = el('td', 'empty', t('AIEEEEEE!? 为什么一张牌都没有!? 换个筛选试试。')); cell.colSpan = columns.length;
    const row = el('tr'); row.append(cell); rows.append(row);
  }
  $('#card-rows').replaceChildren(rows);
}
function openCardId(id, version) {
  const card = view.cards.find(card => card.id === id);
  if (card) { openCard(card, version); return; }
  $('#card-detail').replaceChildren(); $('#card-detail-title').textContent = t('卡牌详情');
  cardPreview(id, version || snapshot.currentVersion, $('#card-preview'), openCardId).catch(error => toast(error.message));
  if (!$('#card-dialog').open) $('#card-dialog').showModal();
}
function openCard(card, version) {
  $('#card-detail-title').textContent = card.name;
  const grid = el('dl', 'detail-grid');
  const countRate = (n, d) => d ? `${percent(n / d)}（${n}/${d}）` : '—';
  const plays = metric.plays(card);
  const rows = [
    [t('抓取率'), countRate(card.picked, card.offered)],
    [t('平均第几层拿'), card.picked ? (card.pickFloorTotal / card.picked).toFixed(1) : '—'],
    [t('拿了它的通关率'), countRate(card.chosenWins, card.chosenRuns)],
    [t('见过但没拿的通关率'), countRate(card.skippedWins, card.skippedRuns)],
    [t('持有胜率'), countRate(card.wins, card.held)],
    [t('每场打出'), plays === null ? '—' : plays.toFixed(2)],
    [t('被移除 / 被升级'), `${card.removed} / ${card.upgraded}`],
  ];
  if (!isPublic) rows.push(['抽到', card.drawn], ['手动 / 自动打出', `${card.manual_plays} / ${card.auto_plays}`],
    ['开始 / 完成结算', `${card.started} / ${card.finished}`], ['实付能量 / 星数', `${card.energy_spent} / ${card.stars_spent}`], ['涉及战斗', card.combatSamples]);
  for (const [label, value] of rows) grid.append(el('dt', '', label), el('dd', '', String(value)));
  $('#card-detail').replaceChildren(grid, el('p', 'panel-note', card.offered < SMALL_SAMPLE ? t('样本还不到 20 次，数字仅供参考。') : t('胜率是对局结果，不是强度排行。')));
  cardPreview(card.id, version || snapshot.currentVersion, $('#card-preview'), openCardId).catch(error => toast(error.message));
  if (!$('#card-dialog').open) $('#card-dialog').showModal();
}
function renderTrend() {
  const entries = view.trend.slice(-14);
  if (!entries.length) { $('#trend').replaceChildren(el('p', 'empty', t('这段时间还没有对局。'))); return; }
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 560 115'); svg.setAttribute('role', 'img'); svg.setAttribute('aria-label', t('最近 14 个有记录的日子里的对局与通关数'));
  const max = Math.max(...entries.map(entry => entry.runs));
  const step = 550 / entries.length;
  entries.forEach((entry, index) => {
    for (const [n, className, dx] of [[entry.runs, 'runs', 0], [entry.wins, 'wins', Math.min(step * .28, 12)]]) {
      const rect = document.createElementNS(svg.namespaceURI, 'rect');
      for (const [attr, val] of Object.entries({ x: 8 + index * step + dx, y: 85 - n / max * 68, width: Math.min(step * .26, 11), height: n / max * 68, rx: 2, class: className })) rect.setAttribute(attr, val);
      const title = document.createElementNS(svg.namespaceURI, 'title'); title.textContent = tr`${entry.date}：${entry.runs} 场对局，${entry.wins} 场通关`; rect.append(title); svg.append(rect);
    }
    for (const [y, text] of [[11, entry.runs], [109, entry.date.slice(5)]]) {
      const label = document.createElementNS(svg.namespaceURI, 'text'); label.setAttribute('x', 7 + index * step); label.setAttribute('y', y); label.textContent = text; svg.append(label);
    }
  });
  $('#trend').replaceChildren(svg);
}
function renderFeedback() {
  const query = $('#feedback-search').value.trim().toLocaleLowerCase(locale), category = $('#feedback-category').value;
  const status = $('#feedback-status').value;
  const records = view.feedback.filter(item => (!category || item.category === category)
    && (!status || (item.review?.status ?? 'unresolved') === status)
    && `${item.description} ${item.context.modVersion} ${item.review?.reply ?? ''}`.toLocaleLowerCase(locale).includes(query));
  const fragment = document.createDocumentFragment();
  for (const item of records) {
    const letter = el('button', 'letter');
    letter.append(el('span', `tag ${item.category}`, categoryName(item.category)), el('strong', '', item.description),
      el('small', '', `v${item.context.modVersion} · ${time(item.at)}`));
    letter.append(el('span', `badge review-${item.review?.status === 'resolved' ? 'resolved' : 'unresolved'}`,
      t(item.review?.status === 'resolved' ? '已解决' : '未解决')));
    letter.addEventListener('click', () => openFeedback(item)); fragment.append(letter);
  }
  if (!records.length) {
    const empty = el('div', 'empty');
    empty.append(el('strong', '', view.sources.feedback.at ? t('信箱空空如也') : t('还没收到来信')),
      el('span', '', view.sources.feedback.at ? t('换个筛选试试。新的来信会在下次更新时出现。') : isPublic ? t('在游戏里按 F2 写第一封吧。') : t('点“同步”读取玩家通过 F2 提交的反馈。')));
    fragment.append(empty);
  }
  $('#feedback-list').replaceChildren(fragment);
}
function openFeedback(item) {
  $('#feedback-detail-title').textContent = categoryName(item.category);
  $('#feedback-description').textContent = item.description;
  const review = item.review ?? { status: 'unresolved', reply: '', updatedAt: null };
  $('#feedback-review-status').textContent = t(review.status === 'resolved' ? '已解决' : '未解决');
  $('#feedback-reply').textContent = review.reply || t('暂无作者回应');
  $('#feedback-review-time').textContent = review.updatedAt ? `${t('更新时间')} · ${time(review.updatedAt)}` : '';
  const metadata = [time(item.at), tr`模组 v${item.context.modVersion}`, tr`游戏 ${item.gameVersion ?? '—'}`];
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
function showPage(name) {
  page = name;
  for (const nav of document.querySelectorAll('[data-page]')) nav.classList.toggle('active', nav.dataset.page === page);
  for (const section of Object.keys(pages)) $(`#${section}-page`).hidden = page !== section;
  [$('#page-title').textContent, $('#page-subtitle').textContent] = pages[page];
  $('#statistics-filters').hidden = page === 'feedback';
  $('.metrics').hidden = page === 'feedback';
  document.body.dataset.page = page;
  const url = new URL(location.href); url.searchParams.set('page', page); history.replaceState(null, '', url);
}
for (const button of document.querySelectorAll('[data-close]')) button.addEventListener('click', () => $(`#${button.dataset.close}`).close());
for (const button of document.querySelectorAll('[data-page]')) button.addEventListener('click', () => {
  showPage(button.dataset.page);
  loadView().catch(error => toast(error.message));
});
function filtersChanged() {
  const url = new URL(location.href);
  for (const key of [...filterIds, 'version']) url.searchParams.delete(key);
  for (const [key, value] of filterParams()) if (value) url.searchParams.append(key, value);
  history.replaceState(null, '', url); loadView().catch(error => toast(error.message));
}
for (const id of filterIds) $(`#${id}`).addEventListener('change', filtersChanged);
$('#version-options').addEventListener('change', event => {
  if (event.target.checked) selectedVersions.add(event.target.value);
  else selectedVersions.delete(event.target.value);
  renderVersionOptions(view?.versions ?? []); filtersChanged();
});
$('#all-versions').addEventListener('click', () => {
  selectedVersions.clear(); renderVersionOptions(view?.versions ?? []); filtersChanged();
});
$('#version').addEventListener('keydown', event => {
  if (event.key === 'Escape') { $('#version').open = false; $('#version summary').focus(); }
});
document.addEventListener('click', event => {
  if (!$('#version').contains(event.target)) $('#version').open = false;
});
$('#reset-filters').addEventListener('click', () => {
  const url = new URL(location.href);
  for (const key of filterIds) { $(`#${key}`).value = key === 'days' ? '30' : ''; url.searchParams.delete(key); }
  selectedVersions.clear(); url.searchParams.delete('version'); renderVersionOptions(view?.versions ?? []);
  history.replaceState(null, '', url);
  loadView().catch(error => toast(error.message));
});
for (const chip of document.querySelectorAll('[data-rarity]')) chip.addEventListener('click', () => {
  rarity = chip.dataset.rarity;
  for (const other of document.querySelectorAll('[data-rarity]')) other.classList.toggle('active', other === chip);
  renderCards();
});
for (const button of document.querySelectorAll('[data-layout]')) button.addEventListener('click', () => {
  layout = button.dataset.layout;
  for (const other of document.querySelectorAll('[data-layout]')) other.classList.toggle('active', other === button);
  renderCards();
});
$('#card-sort').addEventListener('change', () => { direction = $('#card-sort').value === 'name' ? 1 : -1; renderCards(); });
$('#card-search').addEventListener('input', renderCards);
for (const id of ['feedback-search', 'feedback-category', 'feedback-status']) $(`#${id}`).addEventListener('input', renderFeedback);
if (!isPublic) {
  const refresh = async () => {
    $('#refresh').disabled = true; $('#refresh-icon').classList.add('spinning');
    try { await api('/api/refresh', { method: 'POST' }); await loadView(); }
    catch (error) { toast(error.message); }
    finally { $('#refresh').disabled = false; $('#refresh-icon').classList.remove('spinning'); }
  };
  $('#refresh').addEventListener('click', refresh);
  $('#connection-button').addEventListener('click', () => {
    const form = $('#connection-form');
    form.elements.host.value = view.connection.host; form.elements.projectId.value = view.connection.projectId;
    $('#connection-error').textContent = ''; $('#connection-dialog').showModal();
  });
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
      selectedVersions.clear();
      $('#connection-dialog').close(); await loadView(); toast(`已导入 ${result.runs} 场对局；当前显示全部导入日期。`);
    } catch (error) { $('#connection-error').textContent = error.message; }
    finally { event.target.value = ''; }
  });
  $('#export').addEventListener('click', () => {
    const cell = value => `"${String(value).replace(/^[=+@-]/, "'$&").replaceAll('"', '""')}"`;
    const rows = page === 'charts' ? [['图表', $('#chart-title').textContent], ['说明', $('#chart-note').textContent], ['分组', '数值', '记录数'],
      ...[...$('#chart-rows').rows].map(row => [...row.cells].map(cell => cell.textContent))]
      : [['卡牌', '模型 ID', '稀有度', '提供次数', '选中次数', '抓取率', '持有角色数', '持有率', '持有通关率', '平均抓取楼层', '曾抓取样本', '曾抓取通关', '只跳过样本', '只跳过通关', '移除', '升级', '涉及战斗', '抽到', '手动打出', '自动打出', '开始结算', '完成结算', '实付能量', '实付星数'],
        ...displayedCards.map(card => [card.name, card.id, card.rarity, card.offered, card.picked, percent(metric.pickRate(card)), card.held, percent(metric.held(card)), percent(metric.winRate(card)),
          card.picked ? card.pickFloorTotal / card.picked : '', card.chosenRuns, card.chosenWins, card.skippedRuns, card.skippedWins, card.removed, card.upgraded,
          card.combatSamples, ...['drawn', 'manual_plays', 'auto_plays', 'started', 'finished', 'energy_spent', 'stars_spent'].map(field => card.combatSamples ? card[field] : '')])];
    const url = URL.createObjectURL(new Blob(['\ufeff', rows.map(row => row.map(cell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' }));
    const link = el('a'); link.href = url; link.download = `忍者杀手_${page === 'charts' ? '图表数据' : '卡池统计'}_${new Date().toISOString().slice(0, 10)}.csv`; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
}
const params = new URLSearchParams(location.search);
for (const key of filterIds) if (params.has(key)) $(`#${key}`).value = params.get(key);
showPage(Object.hasOwn(pages, params.get('page')) ? params.get('page') : 'cards');
await loadView().then(() => isPublic ? undefined : $('#refresh').click()).catch(error => toast(error.message));
setInterval(() => loadView().catch(error => toast(error.message)), 60_000);
