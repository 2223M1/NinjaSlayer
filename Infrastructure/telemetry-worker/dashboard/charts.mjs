import { t } from './i18n.mjs';
// Chart labels and grouping adapted from Spire Codex 69b3c898a1b62fa277359a17970b9061abf60354.
// Required Notice: Copyright © 2025-present Peter Lord and Spire Codex contributors.
// PolyForm Noncommercial 1.0.0; see vendor/LICENSE.Spire-Codex.md.
export const charts = [
  [
    "winrate-by-floor",
    t("按到达层数的胜率"),
    t("胜率"),
    "line",
    "rate",
    t("在到达该层的对局中，最终获胜的比例")
  ],
  [
    "winrate-over-time",
    t("胜率走势"),
    t("胜率"),
    "line",
    "rate",
    t("每天完成对局的胜率")
  ],
  [
    "winrate-by-stat",
    t("胜率与游戏数据"),
    t("胜率"),
    "bar",
    "rate",
    t("按卡组大小、精英战斗数或升级次数统计胜率")
  ],
  [
    "winrate-by-ascension",
    t("各进阶等级胜率"),
    t("胜率"),
    "bar",
    "rate",
    t("每个进阶等级的胜率")
  ],
  [
    "hardest-dailies",
    t("每日挑战按日期的胜率"),
    t("胜率"),
    "bar",
    "rate",
    t("每天每日挑战的胜率")
  ],
  [
    "deaths-by-floor",
    t("按层数的死亡"),
    t("生存"),
    "bar",
    "count",
    t("失败对局结束的楼层，不含放弃的对局")
  ],
  [
    "acts-funnel",
    t("游戏进度漏斗"),
    t("生存"),
    "bar",
    "count",
    t("到达每一幕的对局数")
  ],
  [
    "deaths-by-room",
    t("死亡房间"),
    t("生存"),
    "bar",
    "count",
    t("失败对局最后进入的房间")
  ],
  [
    "hp-trajectory",
    t("生命曲线"),
    t("游戏曲线"),
    "line",
    "mean",
    t("每层结束时，当前生命占最大生命的平均比例")
  ],
  [
    "gold-curve",
    t("金币曲线"),
    t("游戏曲线"),
    "line",
    "mean",
    t("每层结束时持有的平均金币")
  ],
  [
    "deck-growth",
    t("卡组增长"),
    t("游戏曲线"),
    "line",
    "mean",
    t("每层结束时的平均卡组张数")
  ],
  [
    "hp-loss-by-floor",
    t("每层生命损失"),
    t("战斗"),
    "line",
    "mean",
    t("每层平均损失的生命，治疗不抵消失血")
  ],
  [
    "encounter-damage",
    t("遭遇伤害排名"),
    t("战斗"),
    "bar",
    "mean",
    t("每场战斗平均损失的生命")
  ],
  [
    "encounter-turns",
    t("最慢的遭遇（回合数）"),
    t("战斗"),
    "bar",
    "mean",
    t("每场战斗的平均回合数")
  ],
  [
    "encounter-histogram",
    t("遭遇次数"),
    t("战斗"),
    "bar",
    "count",
    t("各遭遇的战斗次数")
  ],
  [
    "avg-win-time-daily",
    t("每日平均胜利用时"),
    t("战斗"),
    "line",
    "mean",
    t("每天获胜对局的平均用时，单位为分钟")
  ],
  [
    "elites-vs-winrate",
    t("精英战数量与胜率"),
    t("策略"),
    "bar",
    "rate",
    t("按精英战斗次数统计胜率")
  ],
  [
    "smiths-vs-winrate",
    t("升级次数与胜率"),
    t("策略"),
    "bar",
    "rate",
    t("按休息处升级次数统计胜率")
  ],
  [
    "event-outcomes",
    t("事件选项结果"),
    t("策略"),
    "bar",
    "rate",
    t("选择各事件选项后的对局胜率")
  ],
  [
    "entity-over-time",
    t("卡牌 / 遗物 / 药水随时间变化"),
    t("卡牌与遗物"),
    "line",
    "count",
    t("每天结束时持有该卡牌或遗物的对局数；药水按使用次数统计")
  ],
  [
    "entity-copies",
    t("卡组中的数量与胜率"),
    t("卡牌与遗物"),
    "bar",
    "rate",
    t("最终卡组持有该牌不同张数时的胜率")
  ],
  [
    "enchant-winrate",
    t("按附魔的胜率"),
    t("卡牌与遗物"),
    "bar",
    "rate",
    t("最终卡组中持有该附魔的胜率")
  ],
  [
    "runs-over-time",
    t("提交的游戏数随时间变化"),
    t("对局数量"),
    "bar",
    "count",
    t("每天完成并提交的对局数")
  ],
  [
    "stat-histogram",
    t("游戏数据分布"),
    t("分布"),
    "bar",
    "count",
    t("当前筛选下的对局数据分布")
  ],
  [
    "time-to-win",
    t("获胜游戏的用时"),
    t("分布"),
    "bar",
    "count",
    t("获胜对局的用时分布，每 5 分钟一组")
  ],
  [
    "stat-scatter",
    t("用时与最终楼层"),
    t("分布"),
    "bubble",
    "count",
    t("用时与最终楼层，气泡大小表示对局数")
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
