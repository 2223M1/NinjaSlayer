import { t, tr, locale } from './i18n.mjs';
import { charts, chartRows, chartValue, selectGroups } from './charts.mjs';
const $ = (selector) => document.querySelector(selector);
const node = (tag, text, className) => {
  const item = document.createElement(tag);
  if (text !== undefined) item.textContent = text;
  if (className) item.className = className;
  return item;
};
const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
let graph, redraw;

function format(chart, value) {
  return chart.metric === 'count' ? value.toLocaleString(locale)
    : `${value.toFixed(1)}${chart.metric === 'rate' || chart.unit === '%' ? '%' : ''}`;
}

export function renderCharts(snapshot, filters) {
  const groups = selectGroups(snapshot, filters);
  const params = new URLSearchParams(location.search);
  const tabs = $('#chart-tabs');
  if (!tabs.childElementCount)
    for (const chart of charts) {
      const tab = node('button', chart.title, 'chip');
      tab.dataset.chart = chart.id;
      tab.onclick = () => { tabs.dataset.selected = chart.id; redraw(); };
      tabs.append(tab);
    }
  tabs.dataset.selected ||= charts.some((chart) => chart.id === params.get('chart')) ? params.get('chart') : charts[0].id;
  const entityName = (id) => snapshot.entities?.get(id)?.name ?? snapshot.labels?.[id] ?? String(id);
  const draw = () => {
    const chart = charts.find((chart) => chart.id === tabs.dataset.selected);
    for (const tab of tabs.children) tab.classList.toggle('active', tab.dataset.chart === chart.id);
    const rows = chartRows(groups, chart);
    const label = (x) => chart.label ? chart.label(x) : entityName(x);
    $('#chart-title').textContent = chart.title;
    $('#chart-note').textContent = chart.note;
    $('#chart-empty').hidden = rows.length > 0;
    $('#chart-canvas').hidden = rows.length === 0;
    graph?.destroy();
    graph = null;
    const horizontal = Boolean(chart.top);
    if (rows.length)
      graph = new globalThis.Chart($('#chart-canvas'), {
        type: chart.type,
        data: {
          labels: rows.map((row) => label(row.x)),
          datasets: [{
            data: rows.map((row) => chartValue(chart, row)),
            backgroundColor: css('--red-soft'), borderColor: css('--red'), borderWidth: 2,
            pointBackgroundColor: css('--red'), pointRadius: 3, tension: 0.25, borderRadius: 3,
          }],
        },
        options: {
          indexAxis: horizontal ? 'y' : 'x',
          responsive: true, maintainAspectRatio: false, animation: false,
          plugins: {
            legend: { display: false },
            tooltip: {
              backgroundColor: css('--ink'), borderColor: css('--red'), borderWidth: 1, cornerRadius: 0,
              titleColor: css('--paper'), bodyColor: css('--paper'), displayColors: false, padding: 10,
              callbacks: {
                label: (context) => format(chart, context.parsed[horizontal ? 'x' : 'y']),
                afterLabel: (context) => {
                  const row = rows[context.dataIndex];
                  return chart.metric === 'rate' ? tr`${row.wins} / ${row.n} 局通关` : tr`${row.n} 条记录`;
                },
              },
            },
          },
          scales: {
            x: { ticks: { color: css('--muted'), maxRotation: 50 }, grid: { color: css('--grid') } },
            y: { beginAtZero: true, ticks: { color: css('--muted') }, grid: { color: css('--grid') } },
          },
        },
      });
    const body = $('#chart-rows');
    body.replaceChildren();
    for (const row of rows) {
      const line = node('tr');
      line.append(node('td', label(row.x)), node('td', format(chart, chartValue(chart, row))), node('td', row.n.toLocaleString(locale)));
      body.append(line);
    }
    const url = new URL(location.href);
    url.searchParams.set('chart', chart.id);
    history.replaceState(null, '', url);
  };
  redraw = draw;
  draw();
}

// Local admin only: mechanic and per-card combat measurements for balance work.
export function renderMechanisms(snapshot, filters, onCard) {
  const groups = selectGroups(snapshot, filters);
  const total = groups.reduce((sum, group) => sum + group.totalCombats, 0);
  const measured = groups
    .flatMap((group) => group.mechanisms ?? [])
    .filter((row) => row.group === 'vitals' && row.id === 'hp_lost' && (!filters.version || row.version === filters.version))
    .reduce((sum, row) => sum + row.n, 0);
  $('#mechanic-coverage').textContent =
    `有详细记录的战斗：${measured} / ${total}${total ? `（${((100 * measured) / total).toFixed(1)}%）` : ''}。`;
  const values = new Map();
  for (const group of groups)
    for (const row of group.mechanisms ?? []) {
      if (filters.version && row.version !== filters.version) continue;
      const key = `${row.version}/${row.group}/${row.id}`,
        sum = values.get(key) ?? { ...row, n: 0 };
      if (!values.has(key))
        for (const field of Object.keys(row))
          if (typeof row[field] === 'number') sum[field] = 0;
      for (const field of Object.keys(row))
        if (typeof row[field] === 'number') sum[field] += row[field];
      values.set(key, sum);
    }
  const body = $('#mechanic-rows');
  body.replaceChildren();
  for (const row of [...values.values()].sort((a, b) => b.n - a.n)) {
    const line = node('tr'),
      id = row.id.split('/')[0],
      model = snapshot.entities.get(id),
      card = model?.kind === 'card' ? model : null;
    const name = node('td', card
      ? card.variants[0].name + (row.group === 'card' && !row.id.endsWith('/0') ? ' +' : '') + (row.group === 'damage_source' ? ' · 直接伤害' : '')
      : ({
          karate: '空手道', black_flame: '黑炎', shuriken: '手里剑', naraku_absorbed: '奈落吸收', naraku_gained: '奈落生命获得',
          karate_gained: '空手道获得', karate_lost: '空手道减少', generate: '生成卡牌', discard: '弃牌', exhaust: '消耗卡牌',
          shuffle: '洗牌', chado_breath: '茶道呼吸', chado_generated: '茶道生成', chado_exhausted: '茶道消耗', scry_discard: '预见弃牌',
          shuriken_evoked: '手里剑激发', shuriken_converted: '转化强手里剑', shuriken_stock: '手里剑层数净变化', hp_lost: '真实生命损失',
          blocked: '实际抵挡伤害', block_generated: '生成格挡', healed: '治疗', damage: '对敌失血伤害', enemy_blocked: '被敌方格挡',
          kills: '击杀', unattributed: '来源未记录',
        }[row.id] ?? row.id));
    if (card) name.onclick = () => onCard(id);
    if (row.group === 'power') name.textContent = (model?.name ?? row.id) + ' · 层数净变化';
    name.append(node('small', ` · v${row.version}`));
    line.append(
      name,
      node('td', row.n),
      node('td', row.group === 'card' ? row.finished : row.sum.toFixed(1)),
      node('td', row.group === 'card' ? `${row.damage} / ${row.block}` : '—'),
      node('td', row.energy_spent > 0 ? (row.damage / row.energy_spent).toFixed(2) : '—'),
    );
    body.append(line);
  }
  $('#mechanic-empty').hidden = values.size > 0;
}
