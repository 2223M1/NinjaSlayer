import { charts, wilson, selectGroups, chartRows } from "./charts.mjs";
const $ = (selector) => document.querySelector(selector);
const node = (tag, text) => {
  const item = document.createElement(tag);
  if (text !== undefined) item.textContent = text;
  return item;
};
let graph;
export function renderCharts(snapshot, filters, onCard) {
  const groups = selectGroups(snapshot, filters),
    select = $("#chart-select");
  if (!select.options.length)
    for (const group of [...new Set(charts.map((c) => c.group))]) {
      const options = node("optgroup");
      options.label = group;
      for (const chart of charts.filter((c) => c.group === group))
        options.append(new Option(chart.title, chart.id));
      select.append(options);
    }
  const params = new URLSearchParams(location.search);
  if (!select.dataset.initialized) {
    select.value = params.get("chart") ?? charts[0].id;
    select.dataset.initialized = "true";
  }
  const draw = () => {
    const definition = charts.find((c) => c.id === select.value) ?? charts[0];
    const seriesSelect = $("#chart-series");
    const available = [
      ...new Set(
        groups.flatMap((group) =>
          (group.charts ?? [])
            .filter((point) => point.chart === definition.id)
            .map((point) => point.series),
        ),
      ),
    ];
    const selected = seriesSelect.value || params.get("series");
    const entityName = (id) =>
      snapshot.entities?.get(id)?.name ??
      snapshot.entities?.get(id)?.variants?.[0].name ??
      snapshot.catalog.find((c) => c.id === id)?.name;
    seriesSelect.replaceChildren(
      ...(available.length ? available : ["all"]).map(
        (id) =>
          new Option(
            entityName(id) ??
              {
                all: "全部",
                duration: "用时",
                floor: "楼层",
                deck: "牌组张数",
                elites: "精英数",
                smiths: "升级次数",
              }[id] ??
              id,
            id,
          ),
      ),
    );
    if (available.includes(selected)) seriesSelect.value = selected;
    seriesSelect.hidden = available.length <= 1;
    const rows = chartRows(groups, definition.id, seriesSelect.value);
    $("#chart-title").textContent = definition.title;
    const observed = rows.reduce((n, row) => n + row.n, 0),
      runs = groups.reduce((n, group) => n + group.runs, 0);
    $("#chart-note").textContent =
      `${definition.note}。当前筛选 ${runs} 场对局；${observed} 个测量点。缺测不补零。比例附 Wilson 95% 置信区间。`;
    $("#chart-empty").hidden = rows.length > 0;
    $("#chart-canvas").hidden = rows.length === 0;
    graph?.destroy();
    graph = null;
    const value = (row) =>
      definition.metric === "rate"
        ? (100 * row.wins) / row.n
        : definition.metric === "mean"
          ? row.sum / row.n
          : row.n;
    const label = (value) =>
      entityName(value) ?? snapshot.labels?.[value] ?? String(value);
    if (rows.length)
      graph = new globalThis.Chart($("#chart-canvas"), {
        type: definition.type,
        data: {
          labels: rows.map((row) => label(row.x)),
          datasets: [
            {
              label: definition.title,
              data: rows.map((row) =>
                definition.type === "bubble"
                  ? { x: row.x, y: row.y, r: 3 + Math.sqrt(row.n) * 2 }
                  : value(row),
              ),
              backgroundColor: "#bd273b99",
              borderColor: "#ff5269",
              borderWidth: 2,
              pointRadius: 3,
              tension: 0.2,
            },
          ],
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          animation: false,
          plugins: {
            legend: { display: false },
            tooltip: {
              callbacks: {
                afterLabel: (context) => {
                  const row = rows[context.dataIndex],
                    ci = wilson(row.wins, row.n);
                  return definition.metric === "rate"
                    ? `${row.wins}/${row.n} · 95% CI ${(ci[0] * 100).toFixed(1)}–${(ci[1] * 100).toFixed(1)}%`
                    : `n=${row.n}`;
                },
              },
            },
          },
          scales: {
            x: {
              ticks: { color: "#bfb2b3", maxRotation: 60 },
              grid: { color: "#ffffff08" },
            },
            y: {
              beginAtZero: true,
              ticks: { color: "#bfb2b3" },
              grid: { color: "#ffffff0d" },
            },
          },
        },
      });
    const tbody = $("#chart-rows");
    tbody.replaceChildren();
    for (const row of rows) {
      const tr = node("tr"),
        name = node("td", label(row.x));
      if (String(row.x).startsWith("CARD.")) {
        const button = node("button", label(row.x));
        button.onclick = () => onCard(row.x);
        name.replaceChildren(button);
      }
      const ci = wilson(row.wins, row.n);
      tr.append(
        name,
        node(
          "td",
          value(row).toFixed(definition.metric === "count" ? 0 : 1) +
            (definition.metric === "rate" ? "%" : ""),
        ),
        node("td", row.n),
        node(
          "td",
          definition.metric === "rate"
            ? `${(ci[0] * 100).toFixed(1)}–${(ci[1] * 100).toFixed(1)}%`
            : "—",
        ),
      );
      tbody.append(tr);
    }
    const url = new URL(location.href);
    url.searchParams.set("chart", definition.id);
    url.searchParams.set("series", seriesSelect.value);
    history.replaceState(null, "", url);
  };
  select.onchange = draw;
  $("#chart-series").onchange = draw;
  draw();
  renderMechanisms(groups, snapshot, onCard, filters);
}

function renderMechanisms(groups, snapshot, onCard, filters) {
  const total = groups.reduce((sum, group) => sum + group.totalCombats, 0);
  const measured = groups
    .flatMap((group) => group.mechanisms ?? [])
    .filter(
      (row) =>
        row.group === "vitals" &&
        row.id === "hp_lost" &&
        (!filters.version || row.version === filters.version),
    )
    .reduce((sum, row) => sum + row.n, 0);
  $("#mechanic-coverage").textContent =
    `新版完整测量覆盖：${measured} / ${total} 个角色战斗${total ? `（${((100 * measured) / total).toFixed(1)}%）` : ""}。`;
  const values = new Map();
  for (const group of groups)
    for (const row of group.mechanisms ?? []) {
      if (filters.version && row.version !== filters.version) continue;
      const key = `${row.version}/${row.group}/${row.id}`,
        sum = values.get(key) ?? { ...row, n: 0 };
      if (!values.has(key))
        for (const field of Object.keys(row))
          if (typeof row[field] === "number") sum[field] = 0;
      for (const field of Object.keys(row))
        if (typeof row[field] === "number") sum[field] += row[field];
      values.set(key, sum);
    }
  const body = $("#mechanic-rows");
  body.replaceChildren();
  for (const row of [...values.values()].sort((a, b) => b.n - a.n)) {
    const tr = node("tr"),
      id = row.id.split("/")[0],
      model = snapshot.measuredContent.get(row.version)?.get(id),
      card = model?.kind === "card" ? model : null;
    const name = node(
      "td",
      card
        ? card.variants[0].name +
            (row.group === "card" && !row.id.endsWith("/0") ? " +" : "") +
            (row.group === "damage_source" ? " · 直接伤害" : "")
        : ({
            karate: "空手道",
            black_flame: "黑炎",
            shuriken: "手里剑",
            naraku_absorbed: "奈落吸收",
            naraku_gained: "奈落生命获得",
            karate_gained: "空手道获得",
            karate_lost: "空手道减少",
            generate: "生成卡牌",
            discard: "弃牌",
            exhaust: "消耗卡牌",
            shuffle: "洗牌",
            chado_breath: "茶道呼吸",
            chado_generated: "茶道生成",
            chado_exhausted: "茶道消耗",
            scry_discard: "预见弃牌",
            shuriken_evoked: "手里剑激发",
            shuriken_converted: "转化强手里剑",
            shuriken_stock: "手里剑库存净变化",
            hp_lost: "真实生命损失",
            blocked: "实际抵挡伤害",
            block_generated: "生成格挡",
            healed: "治疗",
            damage: "对敌失血伤害",
            enemy_blocked: "被敌方格挡",
            kills: "击杀",
            unattributed: "来源未归属",
          }[row.id] ?? row.id),
    );
    if (card) name.onclick = () => onCard(id, row.version);
    if (row.group === "power")
      name.textContent = (model?.name ?? row.id) + " · 层数净变化";
    name.append(node("small", ` · v${row.version}`));
    tr.append(
      name,
      node("td", row.n),
      node("td", row.group === "card" ? row.finished : row.sum.toFixed(1)),
      node("td", row.group === "card" ? `${row.damage} / ${row.block}` : "—"),
      node(
        "td",
        row.energy_spent > 0 ? (row.damage / row.energy_spent).toFixed(2) : "—",
      ),
    );
    body.append(tr);
  }
  $("#mechanic-empty").hidden = values.size > 0;
}
