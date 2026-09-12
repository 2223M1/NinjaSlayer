export const charts = [
  [
    "winrate-by-floor",
    "到达楼层与通关率",
    "胜负",
    "line",
    "rate",
    "到达该层的对局；最终通关比例",
  ],
  [
    "winrate-over-time",
    "通关率趋势",
    "胜负",
    "line",
    "rate",
    "当日结束的有效对局",
  ],
  [
    "winrate-by-stat",
    "局内特征与通关率",
    "胜负",
    "bar",
    "rate",
    "特征与胜负的关联，不表示因果",
  ],
  [
    "winrate-by-ascension",
    "进阶通关率",
    "胜负",
    "bar",
    "rate",
    "各进阶的有效对局",
  ],
  [
    "hardest-dailies",
    "每日挑战",
    "胜负",
    "bar",
    "rate",
    "每日挑战日期；不公开种子",
  ],
  ["deaths-by-floor", "死亡楼层", "路线", "bar", "count", "失败对局的最终楼层"],
  ["acts-funnel", "幕次通关漏斗", "路线", "bar", "count", "进入各幕的对局数"],
  ["deaths-by-room", "死亡房间", "路线", "bar", "count", "失败对局的最终房间"],
  [
    "hp-trajectory",
    "生命曲线",
    "成长",
    "line",
    "mean",
    "房间结束生命占最大生命的百分比；不扣除治疗",
  ],
  ["gold-curve", "金币曲线", "成长", "line", "mean", "房间结束时持有金币"],
  [
    "deck-growth",
    "牌组增长",
    "成长",
    "line",
    "mean",
    "新版房间结束的实际牌组张数；旧版缺测",
  ],
  [
    "hp-loss-by-floor",
    "每层累计受伤",
    "成长",
    "line",
    "mean",
    "原生累计受伤；治疗不抵消受伤",
  ],
  [
    "encounter-damage",
    "遭遇受伤",
    "战斗",
    "bar",
    "mean",
    "每场战斗实际生命损失；旧版仅使用单战斗房间",
  ],
  [
    "encounter-turns",
    "遭遇回合数",
    "战斗",
    "bar",
    "mean",
    "原生记录的每场战斗回合数",
  ],
  [
    "encounter-histogram",
    "遭遇分布",
    "战斗",
    "bar",
    "count",
    "角色参与的战斗次数",
  ],
  [
    "avg-win-time-daily",
    "每日平均通关用时",
    "用时",
    "line",
    "mean",
    "通关用时，单位分钟",
  ],
  [
    "elites-vs-winrate",
    "精英数量与通关率",
    "选择",
    "bar",
    "rate",
    "遭遇精英数量与最终结果的关联",
  ],
  [
    "smiths-vs-winrate",
    "升级选择与通关率",
    "选择",
    "bar",
    "rate",
    "休息点升级次数与最终结果的关联",
  ],
  [
    "event-outcomes",
    "事件选项与结果",
    "选择",
    "bar",
    "rate",
    "选择该事件选项的样本及最终通关率",
  ],
  [
    "entity-over-time",
    "卡牌、遗物与药水趋势",
    "持有",
    "line",
    "count",
    "卡牌与遗物为结束时持有；药水为使用次数",
  ],
  [
    "entity-copies",
    "卡牌持有数量",
    "持有",
    "bar",
    "rate",
    "结束牌组持有指定张数时的通关率，仅表示关联",
  ],
  [
    "enchant-winrate",
    "附魔与通关率",
    "持有",
    "bar",
    "rate",
    "结束牌组中持有该附魔的卡牌样本",
  ],
  [
    "runs-over-time",
    "每日样本量",
    "样本",
    "bar",
    "count",
    "成功送达并通过去重的完成对局",
  ],
  [
    "stat-histogram",
    "对局特征分布",
    "样本",
    "bar",
    "count",
    "用时按5分钟分组；其他指标按实际整数分组",
  ],
  [
    "time-to-win",
    "通关用时分布",
    "用时",
    "bar",
    "count",
    "仅通关对局，按5分钟分组",
  ],
  [
    "stat-scatter",
    "用时与到达楼层",
    "样本",
    "bubble",
    "count",
    "相同用时区间和楼层合并；气泡面积表示样本数",
  ],
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
