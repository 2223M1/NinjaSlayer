// Chart labels and grouping adapted from Spire Codex 69b3c898a1b62fa277359a17970b9061abf60354.
// Required Notice: Copyright © 2025-present Peter Lord and Spire Codex contributors.
// PolyForm Noncommercial 1.0.0; see vendor/LICENSE.Spire-Codex.md.
export const charts = [
  [
    "winrate-by-floor",
    "按到达层数的胜率",
    "胜率",
    "line",
    "rate",
    "在到达该层的对局中，最终获胜的比例"
  ],
  [
    "winrate-over-time",
    "胜率走势",
    "胜率",
    "line",
    "rate",
    "每天完成对局的胜率"
  ],
  [
    "winrate-by-stat",
    "胜率与游戏数据",
    "胜率",
    "bar",
    "rate",
    "按卡组大小、精英战斗数或升级次数统计胜率"
  ],
  [
    "winrate-by-ascension",
    "各进阶等级胜率",
    "胜率",
    "bar",
    "rate",
    "每个进阶等级的胜率"
  ],
  [
    "hardest-dailies",
    "每日挑战按日期的胜率",
    "胜率",
    "bar",
    "rate",
    "每天每日挑战的胜率"
  ],
  [
    "deaths-by-floor",
    "按层数的死亡",
    "生存",
    "bar",
    "count",
    "失败对局结束的楼层，不含放弃的对局"
  ],
  [
    "acts-funnel",
    "游戏进度漏斗",
    "生存",
    "bar",
    "count",
    "到达每一幕的对局数"
  ],
  [
    "deaths-by-room",
    "死亡房间",
    "生存",
    "bar",
    "count",
    "失败对局最后进入的房间"
  ],
  [
    "hp-trajectory",
    "生命曲线",
    "游戏曲线",
    "line",
    "mean",
    "每层结束时，当前生命占最大生命的平均比例"
  ],
  [
    "gold-curve",
    "金币曲线",
    "游戏曲线",
    "line",
    "mean",
    "每层结束时持有的平均金币"
  ],
  [
    "deck-growth",
    "卡组增长",
    "游戏曲线",
    "line",
    "mean",
    "每层结束时的平均卡组张数"
  ],
  [
    "hp-loss-by-floor",
    "每层生命损失",
    "战斗",
    "line",
    "mean",
    "每层平均损失的生命，治疗不抵消失血"
  ],
  [
    "encounter-damage",
    "遭遇伤害排名",
    "战斗",
    "bar",
    "mean",
    "每场战斗平均损失的生命"
  ],
  [
    "encounter-turns",
    "最慢的遭遇（回合数）",
    "战斗",
    "bar",
    "mean",
    "每场战斗的平均回合数"
  ],
  [
    "encounter-histogram",
    "遭遇次数",
    "战斗",
    "bar",
    "count",
    "各遭遇的战斗次数"
  ],
  [
    "avg-win-time-daily",
    "每日平均胜利用时",
    "战斗",
    "line",
    "mean",
    "每天获胜对局的平均用时，单位为分钟"
  ],
  [
    "elites-vs-winrate",
    "精英战数量与胜率",
    "策略",
    "bar",
    "rate",
    "按精英战斗次数统计胜率"
  ],
  [
    "smiths-vs-winrate",
    "升级次数与胜率",
    "策略",
    "bar",
    "rate",
    "按休息处升级次数统计胜率"
  ],
  [
    "event-outcomes",
    "事件选项结果",
    "策略",
    "bar",
    "rate",
    "选择各事件选项后的对局胜率"
  ],
  [
    "entity-over-time",
    "卡牌 / 遗物 / 药水随时间变化",
    "卡牌与遗物",
    "line",
    "count",
    "每天结束时持有该卡牌或遗物的对局数；药水按使用次数统计"
  ],
  [
    "entity-copies",
    "卡组中的数量与胜率",
    "卡牌与遗物",
    "bar",
    "rate",
    "最终卡组持有该牌不同张数时的胜率"
  ],
  [
    "enchant-winrate",
    "按附魔的胜率",
    "卡牌与遗物",
    "bar",
    "rate",
    "最终卡组中持有该附魔的胜率"
  ],
  [
    "runs-over-time",
    "提交的游戏数随时间变化",
    "对局数量",
    "bar",
    "count",
    "每天完成并提交的对局数"
  ],
  [
    "stat-histogram",
    "游戏数据分布",
    "分布",
    "bar",
    "count",
    "当前筛选下的对局数据分布"
  ],
  [
    "time-to-win",
    "获胜游戏的用时",
    "分布",
    "bar",
    "count",
    "获胜对局的用时分布，每 5 分钟一组"
  ],
  [
    "stat-scatter",
    "用时与最终楼层",
    "分布",
    "bubble",
    "count",
    "用时与最终楼层，气泡大小表示对局数"
  ]
].map(([id, title, group, type, metric, note]) => ({
  id,
  title,
  group,
  type,
  metric,
  note,
}));

export function wilson(wins, n) {
  if (!n) return null;
  const z = 1.959963984540054,
    p = wins / n,
    d = 1 + (z * z) / n;
  const center = (p + (z * z) / (2 * n)) / d,
    margin = (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / d;
  return [Math.max(0, center - margin), Math.min(1, center + margin)];
}

export function selectGroups(snapshot, filters = {}, now = Date.now()) {
  const cutoff = filters.days
    ? new Date(
        Date.parse(new Date(now).toISOString().slice(0, 10)) -
          (Number(filters.days) - 1) * 86400000,
      )
        .toISOString()
        .slice(0, 10)
    : "";
  return snapshot.groups.filter(
    (group) =>
      (!cutoff || group.date >= cutoff) &&
      (!filters.from || group.date >= filters.from) &&
      (!filters.to || group.date <= filters.to) &&
      (!filters.version || group.version === filters.version) &&
      (!filters.gameVersion || group.gameVersion === filters.gameVersion) &&
      (!filters.mode || group.mode === filters.mode) &&
      (!filters.party || group.party === filters.party) &&
      (!filters.reloads ||
        (filters.reloads === "none" ? group.noReloads : !group.noReloads)) &&
      (filters.ascension === "" ||
        filters.ascension == null ||
        group.ascension === Number(filters.ascension)) &&
      (!filters.outcome || group.outcome === filters.outcome) &&
      (!filters.a10 || group.a10 === filters.a10),
  );
}

export function chartRows(groups, id, series = "all") {
  const rows = new Map();
  for (const group of groups)
    for (const point of group.charts ?? []) {
      if (point.chart !== id || point.series !== series) continue;
      const key = JSON.stringify([point.x, point.y]);
      const row = rows.get(key) ?? {
        x: point.x,
        y: point.y,
        n: 0,
        sum: 0,
        wins: 0,
      };
      row.n += point.n;
      row.sum += point.sum;
      row.wins += point.wins;
      rows.set(key, row);
    }
  return [...rows.values()].sort((a, b) =>
    typeof a.x === "number"
      ? a.x - b.x
      : String(a.x).localeCompare(String(b.x)),
  );
}
