import { t, tr } from './i18n.mjs';

// metric: count = runs in the bin, rate = win rate, mean = average value.
// top: rank the bins by value and keep the first N (enemy lists).
export const charts = [
  { id: 'floor-deaths', type: 'bar', metric: 'count', title: t('倒在哪一层'),
    note: t('没能登顶的对局，最后停在了哪一层。撒由那拉！'), label: x => tr`${x} 层` },
  { id: 'act-reach', type: 'bar', metric: 'count', title: t('能走到第几幕'),
    note: t('到达每一幕的对局数。'), label: x => tr`第 ${x} 幕` },
  { id: 'hp-by-floor', type: 'line', metric: 'mean', unit: '%', title: t('每层还剩多少血'),
    note: t('每层结束时，平均剩余生命占上限的比例。'), label: x => tr`${x} 层` },
  { id: 'ascension-wins', type: 'bar', metric: 'rate', title: t('各进阶胜率'),
    note: t('每个进阶等级的通关率。'), label: x => `A${x}` },
  { id: 'enemy-damage', type: 'bar', metric: 'mean', top: 15, title: t('谁下手最狠'),
    note: t('每场战斗平均掉多少血，治疗不抵消。只列前 15 名。') },
  { id: 'enemy-turns', type: 'bar', metric: 'mean', top: 15, title: t('哪场仗最磨蹭'),
    note: t('每场战斗平均打几回合。只列前 15 名。') },
  { id: 'elite-wins', type: 'bar', metric: 'rate', title: t('精英打几个'),
    note: t('按整局打过的精英数量统计通关率。'), label: x => tr`${x} 个精英` },
  { id: 'deck-wins', type: 'bar', metric: 'rate', title: t('牌组多厚最好赢'),
    note: t('按结束时的牌组张数统计通关率，每 5 张一组。'), label: x => tr`${x}–${x + 4} 张` },
];

export function chartValue(chart, row) {
  return chart.metric === 'rate' ? 100 * row.wins / row.n : chart.metric === 'mean' ? row.sum / row.n : row.n;
}

export function selectGroups(snapshot, filters = {}, now = Date.now()) {
  const cutoff = filters.days
    ? new Date(Date.parse(new Date(now).toISOString().slice(0, 10)) - (Number(filters.days) - 1) * 86400000).toISOString().slice(0, 10)
    : '';
  return snapshot.groups.filter(group => (!cutoff || group.date >= cutoff)
    && (!filters.version || group.version === filters.version)
    && (!filters.party || group.party === filters.party)
    && (filters.ascension === '' || filters.ascension == null || group.ascension === Number(filters.ascension))
    && (!filters.outcome || group.outcome === filters.outcome));
}

export function chartRows(groups, chart) {
  const rows = new Map();
  for (const group of groups)
    for (const point of group.charts ?? []) {
      if (point.chart !== chart.id) continue;
      const row = rows.get(point.x) ?? { x: point.x, n: 0, sum: 0, wins: 0 };
      row.n += point.n; row.sum += point.sum; row.wins += point.wins;
      rows.set(point.x, row);
    }
  const sorted = [...rows.values()];
  if (chart.top) return sorted.sort((a, b) => chartValue(chart, b) - chartValue(chart, a)).slice(0, chart.top);
  return sorted.sort((a, b) => typeof a.x === 'number' ? a.x - b.x : String(a.x).localeCompare(String(b.x)));
}
