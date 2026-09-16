import { loadCatalog } from "./catalog-view.mjs";
const ENDPOINT = "https://telemetry.feixingwawa.cn";
const $ = (selector) => document.querySelector(selector);
const node = (tag, text, className) => {
  const item = document.createElement(tag);
  if (text !== undefined) item.textContent = text;
  if (className) item.className = className;
  return item;
};
const actionNames = {
  combat_start: "战斗开始",
  combat_end: "战斗结束",
  attempt_end: "尝试结束",
  attempt_interrupted: "读档中断",
  snapshot_end: "初始快照完成",
  pile: "牌堆变动",
  creature: "初始生命",
  turn: "回合开始",
  draw: "抽牌",
  play: "打出",
  resolve: "完成结算",
  discard: "弃牌",
  exhaust: "消耗",
  generate: "生成",
  hit: "伤害",
  block: "获得格挡",
  heal: "治疗",
  hp_loss: "实际失血",
  power: "状态变化",
  power_snapshot: "初始状态",
  move: "怪物行动",
  shuffle: "洗牌",
  naraku_absorbed: "奈落吸收",
  naraku_gained: "获得奈落生命",
  karate_gained: "获得空手道",
  karate_lost: "减少空手道",
  scry_discard: "预见弃牌",
  shuriken_stock: "手里剑库存变化",
  potion: "使用药水",
  channel: "生成充能球",
  afflict: "卡牌附加效果",
  chado_breath: "茶道呼吸",
  shuriken_evoked: "激发手里剑",
  shuriken_converted: "转化强手里剑",
};
export function renderReports(snapshot, filters, onCard) {
  const list = $("#report-list");
  list.replaceChildren();
  if (filters.a10)
    list.append(
      node(
        "p",
        "A10 玩家胜率分组仅用于汇总图表，不用于匿名战报筛选。",
        "notice",
      ),
    );
  const cutoff = filters.days
    ? Date.now() - Number(filters.days) * 86400000
    : 0;
  const reports = (snapshot.reports ?? []).filter(
    (report) =>
      Date.parse(report.expires) > Date.now() &&
      Date.parse(report.at) >= cutoff &&
      (!filters.from || report.at.slice(0, 10) >= filters.from) &&
      (!filters.to || report.at.slice(0, 10) <= filters.to) &&
      (!filters.version || report.version === filters.version) &&
      (!filters.gameVersion || report.gameVersion === filters.gameVersion) &&
      (filters.ascension === "" ||
        filters.ascension == null ||
        report.ascension === Number(filters.ascension)) &&
      (!filters.mode || report.mode === filters.mode) &&
      (!filters.party ||
        (filters.party === "solo" ? report.party === 1 : report.party > 1)) &&
      (!filters.outcome || report.won === (filters.outcome === "win")) &&
      (!filters.reloads ||
        (filters.reloads === "none"
          ? report.reloads === 0
          : report.reloads > 0)),
  );
  for (const report of reports.sort((a, b) => b.at.localeCompare(a.at))) {
    const button = node("button", undefined, "report-row");
    button.append(
      node("strong", report.won ? "通关" : "未通关"),
      node(
        "span",
        `A${report.ascension} · ${report.rooms} 层 · ${Math.round(report.duration / 60)} 分钟`,
      ),
      node(
        "small",
        `${report.at.slice(0, 10)} · v${report.version} · ${{ complete: "完整", gapped: "有缺段", truncated: "超出记录上限" }[report.coverage]} · ${report.party} 人`,
      ),
    );
    button.onclick = () => openReport(report, onCard, snapshot);
    list.append(button);
  }
  if (!reports.length)
    list.append(
      node(
        "p",
        "尚无符合筛选的公开战报。只有玩家明确开启“公开完整战报”后采集的新记录会出现在这里。",
        "empty",
      ),
    );
  const url = new URL(location.href),
    id = url.searchParams.get("report"),
    contributor = Number(url.searchParams.get("contributor") ?? 0);
  const selected = reports.find(
    (report) => report.id === id && report.contributor === contributor,
  );
  if (selected && !$("#report-detail").dataset.opened) {
    $("#report-detail").dataset.opened = id;
    openReport(selected, onCard, snapshot);
  }
}

async function openReport(report, onCard, snapshot) {
  const panel = $("#report-detail");
  panel.hidden = false;
  panel.replaceChildren(node("p", "正在读取战报…"));
  try {
    const response = await fetch(
      `${ENDPOINT}/observatory/replays/${report.id}/${report.contributor}`,
    );
    if (!response.ok)
      throw new Error(
        response.status === 410
          ? "该战报已过期或尚未同步。"
          : "战报暂时无法读取，请稍后重试。",
      );
    const data = await response.json();
    if (Date.parse(data.expires) <= Date.now())
      throw new Error("该战报已过期。");
    const url = new URL(location.href);
    url.searchParams.set("report", report.id);
    url.searchParams.set("contributor", report.contributor);
    history.replaceState(null, "", url);
    panel.replaceChildren(
      node(
        "h2",
        `匿名战报 · ${data.won ? "通关" : "未通关"} · A${data.ascension}`,
      ),
      node(
        "p",
        `版本 ${data.version} · ${data.party > 1 ? "仅展示本贡献者明细，其他玩家未采集。" : "单人对局。"} 保存至 ${data.expires.slice(0, 10)}。`,
      ),
    );
    const download = node("button", "下载匿名战报 JSON", "button");
    download.onclick = () => {
      const url = URL.createObjectURL(
        new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }),
      );
      const link = node("a");
      link.href = url;
      link.download = `忍者杀手_战报_${report.id.slice(0, 12)}.json`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
    panel.append(download);
    if (data.coverage !== "complete")
      panel.append(
        node(
          "p",
          "此记录有未采集或未持久保存的部分，不能视为完整战斗重演。",
          "notice",
        ),
      );
    const controls = node("div", undefined, "replay-controls"),
      previous = node("button", "← 上一步", "button"),
      next = node("button", "下一步 →", "button"),
      counter = node("span");
    const range = node("input");
    range.type = "range";
    range.min = 0;
    range.max = Math.max(0, data.frames.length - 1);
    range.value = 0;
    range.setAttribute("aria-label", "战报行动位置");
    controls.append(previous, range, next, counter);
    const current = node("div", undefined, "current-action"),
      state = node("div", undefined, "replay-state");
    panel.append(controls, current, state);
    const catalog = await loadCatalog(data.version);
    const name = (id) => {
      const model = catalog?.languages.zhs.find((model) => model.id === id);
      return model?.variants?.[0].name ?? model?.name ?? id ?? "";
    };
    const actorName = (id) =>
      id === "self"
        ? "自己"
        : id === "uncollected"
          ? "未采集的队友"
          : name(id?.split("/")[0]);
    const roomNames = {
      Monster: "普通战斗",
      Elite: "精英",
      Boss: "首领",
      Event: "事件",
      Shop: "商店",
      Treasure: "宝箱",
      RestSite: "休息点",
      Ancient: "先古事件",
    };
    const actionText = (frame) => {
      const action = frame.action;
      const target = action.target ? ` → ${actorName(action.target)}` : "";
      return `第${frame.floor}层 · 回合${action.round} · ${actorName(action.actor)} ${actionNames[action.kind] ?? action.kind} ${name(action.model)}${action.upgrade ? " +" : ""}${action.amount !== undefined ? " " + action.amount : ""}${target}${action.kind === "hit" ? `：失血${action.hp_loss} / 格挡${action.blocked}${action.killed ? " · 击杀" : ""}` : ""}${action.kind === "play" ? ` · ${action.auto ? "自动" : "手动"} · 实付${action.energy}能量/${action.stars}星 · 重复序号${action.repeat}` : ""}`;
    };
    let cursor = 0;
    const update = () => {
      range.value = cursor;
      counter.textContent = `${data.frames.length ? cursor + 1 : 0} / ${data.frames.length}`;
      previous.disabled = cursor === 0;
      next.disabled = cursor >= data.frames.length - 1;
      const frame = data.frames[cursor];
      current.replaceChildren(
        node("strong", frame ? actionText(frame) : "没有行动记录"),
      );
      if (frame?.action.model?.startsWith("CARD.")) {
        const preview = node("button", "查看当时版本卡牌", "button");
        preview.onclick = () => onCard(frame.action.model, data.version);
        current.append(preview);
      }
      const cards = new Map(),
        creatures = new Map(),
        powers = new Map();
      for (const prior of data.frames
        .slice(0, cursor + 1)
        .filter((item) => item.attempt === frame?.attempt)) {
        const action = prior.action;
        if (action.instance && action.pile) cards.set(action.instance, action);
        if (action.kind === "power" || action.kind === "power_snapshot")
          powers.set(`${action.target}/${action.model}`, action);
        if (action.hp !== undefined)
          creatures.set(action.kind === "hit" ? action.target : action.actor, {
            hp: action.hp,
            maxHp:
              action.max_hp ??
              creatures.get(
                action.kind === "hit" ? action.target : action.actor,
              )?.maxHp,
          });
      }
      state.replaceChildren(node("h3", "已记录的生命与牌堆"));
      const snapshotComplete = data.frames
        .slice(0, cursor + 1)
        .some(
          (item) =>
            item.attempt === frame?.attempt &&
            item.action.kind === "snapshot_end",
        );
      if (!snapshotComplete || data.coverage !== "complete")
        state.append(
          node(
            "p",
            "牌堆仅列出已采集的卡牌；初始快照未完成或战报缺段时，数量可能不完整。",
            "notice",
          ),
        );
      for (const [actor, creature] of creatures)
        state.append(
          node(
            "p",
            `${actor === "self" ? "自己" : name(actor?.split("/")[0])}：${creature.hp}${creature.maxHp !== undefined ? "/" + creature.maxHp : ""}`,
          ),
        );
      for (const power of powers.values())
        if (power.value !== 0)
          state.append(
            node(
              "p",
              `${power.target === "self" ? "自己" : name(power.target?.split("/")[0])} · ${name(power.model)} ${power.value}`,
            ),
          );
      for (const [pile, label] of [
        ["Hand", "手牌"],
        ["Draw", "抽牌堆"],
        ["Discard", "弃牌堆"],
        ["Exhaust", "消耗堆"],
        ["Play", "结算中"],
      ]) {
        const items = [...cards.values()].filter((card) => card.pile === pile),
          section = node("details");
        section.append(node("summary", `${label} · ${items.length} 张已记录`));
        for (const card of items) {
          const button = node(
            "button",
            name(card.model) + (card.upgrade ? " +" : ""),
            "button",
          );
          button.onclick = () => onCard(card.model, data.version);
          section.append(button);
        }
        state.append(section);
      }
    };
    previous.onclick = () => {
      cursor--;
      update();
    };
    next.onclick = () => {
      cursor++;
      update();
    };
    range.oninput = () => {
      cursor = Number(range.value);
      update();
    };
    update();
    const route = node("div", undefined, "replay-route");
    const floors = new Map(data.floors.map((floor) => [floor.floor, floor]));
    for (const frame of data.frames)
      if (!floors.has(frame.floor))
        floors.set(frame.floor, {
          floor: frame.floor,
          rooms: [],
          missing: true,
        });
    for (const floor of [...floors.values()].sort(
      (a, b) => a.floor - b.floor,
    )) {
      const section = node("details");
      section.append(
        node(
          "summary",
          floor.missing
            ? `第${floor.floor}层 · 房间信息未采集`
            : `第${floor.floor}层 · ${floor.rooms.map((room) => name(room.model) || roomNames[room.type] || room.type).join(" → ")} · 生命 ${floor.hp}/${floor.max_hp} · 金币 ${floor.gold}`,
        ),
      );
      for (const [label, items] of [
        ["奖励", floor.card_choices],
        ["遗物", floor.relic_choices],
        ["药水", floor.potion_choices],
      ])
        if (items?.length)
          section.append(
            node(
              "p",
              `${label}：${items.map((item) => name(item.id) + (item.upgrade ? " +" : "") + (item.picked ? "（选中）" : "（跳过）")).join("、")}`,
            ),
          );
      for (const [label, items] of [
        [
          "获得卡牌",
          floor.cards_gained?.map(
            (item) => name(item.id) + (item.upgrade ? " +" : ""),
          ),
        ],
        ["移除", floor.cards_removed?.map(name)],
        ["升级", floor.upgraded?.map(name)],
        [
          "变换",
          floor.transformed?.map(
            (item) => `${name(item.from)} → ${name(item.to)}`,
          ),
        ],
        [
          "附魔",
          floor.enchanted?.map(
            (item) => `${name(item.card)} · ${name(item.enchantment)}`,
          ),
        ],
        ["购买遗物", floor.bought_relics?.map(name)],
        ["购买药水", floor.bought_potions?.map(name)],
        ["使用药水", floor.potions_used?.map(name)],
      ])
        if (items?.length)
          section.append(node("p", `${label}：${items.join("、")}`));
      if (floor.events?.length)
        section.append(
          node(
            "p",
            `事件：${floor.events.map((key) => catalog?.labels?.zhs[key] ?? key).join("、")}`,
          ),
        );
      if (floor.rests?.length)
        section.append(node("p", `休息点：${floor.rests.join("、")}`));
      for (const roomIndex of [
        ...new Set(
          data.frames
            .filter((frame) => frame.floor === floor.floor)
            .map((frame) => frame.room),
        ),
      ]) {
        const roomNode = node("details");
        roomNode.append(
          node(
            "summary",
            `房间 ${roomIndex + 1} · ${name(floor.rooms[roomIndex]?.model)}`,
          ),
        );
        const attempts = [
          ...new Set(
            data.frames
              .filter(
                (frame) =>
                  frame.floor === floor.floor && frame.room === roomIndex,
              )
              .map((frame) => frame.attempt),
          ),
        ];
        attempts.forEach((attempt, index) => {
          const attemptNode = node("details");
          attemptNode.append(
            node(
              "summary",
              `战斗尝试 ${index + 1}${index === attempts.length - 1 ? " · 最终尝试" : ""}`,
            ),
          );
          const frames = data.frames
            .map((frame, index) => ({ frame, index }))
            .filter((item) => item.frame.attempt === attempt);
          for (const round of [
            ...new Set(frames.map((item) => item.frame.action.round)),
          ]) {
            const turn = node("details");
            turn.append(node("summary", `回合 ${round}`));
            for (const item of frames.filter(
              (item) => item.frame.action.round === round,
            )) {
              const button = node(
                "button",
                actionText(item.frame),
                "action-row",
              );
              button.onclick = () => {
                cursor = item.index;
                update();
                controls.scrollIntoView({ block: "center" });
              };
              turn.append(button);
            }
            attemptNode.append(turn);
          }
          roomNode.append(attemptNode);
        });
        section.append(roomNode);
      }
      route.append(section);
    }
    panel.append(route);
  } catch (error) {
    panel.replaceChildren(node("p", error.message, "notice"));
  }
}
