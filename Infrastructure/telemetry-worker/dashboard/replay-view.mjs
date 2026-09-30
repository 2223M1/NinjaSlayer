import { t, tr, language } from './i18n.mjs';
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
  combat_start: t("战斗开始"),
  combat_end: t("战斗结束"),
  attempt_end: t("尝试结束"),
  attempt_interrupted: t("读档中断"),
  snapshot_end: t("开局状态"),
  pile: t("牌堆变动"),
  creature: t("初始生命"),
  turn: t("回合开始"),
  draw: t("抽牌"),
  play: t("打出"),
  resolve: t("结算完毕"),
  discard: t("弃牌"),
  exhaust: t("消耗"),
  generate: t("生成"),
  hit: t("伤害"),
  block: t("获得格挡"),
  heal: t("治疗"),
  hp_loss: t("掉血"),
  power: t("状态变化"),
  power_snapshot: t("初始状态"),
  move: t("怪物行动"),
  shuffle: t("洗牌"),
  naraku_absorbed: t("奈落吸收"),
  naraku_gained: t("获得奈落生命"),
  karate_gained: t("获得空手道"),
  karate_lost: t("减少空手道"),
  scry_discard: t("预见弃牌"),
  shuriken_stock: t("手里剑库存变化"),
  potion: t("使用药水"),
  channel: t("生成充能球"),
  afflict: t("卡牌附加效果"),
  chado_breath: t("茶道呼吸"),
  shuriken_evoked: t("激发手里剑"),
  shuriken_converted: t("转化强手里剑"),
};
export function renderReports(snapshot, filters, onCard) {
  const list = $("#report-list");
  list.replaceChildren();
  const cutoff = filters.days
    ? Date.now() - Number(filters.days) * 86400000
    : 0;
  const reports = (snapshot.reports ?? []).filter(
    (report) =>
      Date.parse(report.expires) > Date.now() &&
      Date.parse(report.at) >= cutoff &&
      (!filters.version || report.version === filters.version) &&
      (filters.ascension === "" ||
        filters.ascension == null ||
        report.ascension === Number(filters.ascension)) &&
      (!filters.party ||
        (filters.party === "solo" ? report.party === 1 : report.party > 1)) &&
      (!filters.outcome || report.won === (filters.outcome === "win")),
  );
  for (const report of reports.sort((a, b) => b.at.localeCompare(a.at))) {
    const button = node("button", undefined, `report-row ${report.won ? "won" : "lost"}`);
    button.append(
      node("strong", report.won ? t("通关") : t("撒由那拉")),
      node(
        "span",
        tr`A${report.ascension} · ${report.rooms} 层 · ${Math.round(report.duration / 60)} 分钟`,
      ),
      node(
        "small",
        tr`${report.at.slice(0, 10)} · v${report.version} · ${{ complete: t("完整"), gapped: t("有缺段"), truncated: t("太长没录完") }[report.coverage]} · ${report.party} 人`,
      ),
    );
    button.onclick = () => openReport(report, onCard, snapshot);
    list.append(button);
  }
  if (!reports.length)
    list.append(
      node(
        "p",
        t("还没有战报。在模组设置里打开“公开完整战报”，来当第一个吧！"),
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
  panel.replaceChildren(node("p", t("咿呀——！战报读取中…")));
  try {
    const response = await fetch(
      `${ENDPOINT}/observatory/replays/${report.id}/${report.contributor}`,
    );
    if (!response.ok)
      throw new Error(
        response.status === 410
          ? t("南无三！这份战报已经过期，或者还没同步过来。")
          : t("咕哇——！战报读不出来，等会儿再试试。"),
      );
    const data = await response.json();
    if (Date.parse(data.expires) <= Date.now())
      throw new Error(t("南无三！这份战报已经过期了。"));
    const url = new URL(location.href);
    url.searchParams.set("report", report.id);
    url.searchParams.set("contributor", report.contributor);
    history.replaceState(null, "", url);
    panel.replaceChildren(
      node(
        "h2",
        tr`匿名战报 · ${data.won ? t("通关") : t("撒由那拉")} · A${data.ascension}`,
      ),
      node(
        "p",
        tr`版本 ${data.version} · ${data.party > 1 ? t("仅展示分享者的操作。") : t("单人对局。")} 最晚保留至 ${data.expires.slice(0, 10)}。`,
      ),
    );
    const download = node("button", t("下载战报"), "button");
    download.onclick = () => {
      const url = URL.createObjectURL(
        new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }),
      );
      const link = node("a");
      link.href = url;
      link.download = tr`忍者杀手_战报_${report.id.slice(0, 12)}.json`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
    panel.append(download);
    if (data.coverage !== "complete")
      panel.append(
        node(
          "p",
          t("有些回合没录到，这份战报不完整。"),
          "notice",
        ),
      );
    const controls = node("div", undefined, "replay-controls"),
      previous = node("button", t("← 上一步"), "button"),
      next = node("button", t("下一步 →"), "button"),
      counter = node("span");
    const range = node("input");
    range.type = "range";
    range.min = 0;
    range.max = Math.max(0, data.frames.length - 1);
    range.value = 0;
    range.setAttribute("aria-label", t("战报行动位置"));
    controls.append(previous, range, next, counter);
    const current = node("div", undefined, "current-action"),
      state = node("div", undefined, "replay-state");
    panel.append(controls, current, state);
    const catalog = await loadCatalog(data.version);
    if (catalog && !catalog.languages[language])
      panel.append(node("p", t("这个版本还没有所选语言的卡牌资料，先用英文顶一下。"), "notice"));
    const name = (id) => {
      const model = (catalog?.languages[language] ?? catalog?.languages.eng)?.find((model) => model.id === id);
      return model?.variants?.[0].name ?? model?.name ?? id ?? "";
    };
    const actorName = (id) =>
      id === "self"
        ? t("自己")
        : id === "uncollected"
          ? t("未采集的队友")
          : name(id?.split("/")[0]);
    const roomNames = {
      Monster: t("普通战斗"),
      Elite: t("精英"),
      Boss: t("首领"),
      Event: t("事件"),
      Shop: t("商店"),
      Treasure: t("宝箱"),
      RestSite: t("休息点"),
      Ancient: t("先古事件"),
    };
    const actionText = (frame) => {
      const action = frame.action;
      const target = action.target ? ` → ${actorName(action.target)}` : "";
      return tr`第${frame.floor}层 · 回合${action.round} · ${actorName(action.actor)} ${actionNames[action.kind] ?? action.kind} ${name(action.model)}${action.upgrade ? " +" : ""}${action.amount !== undefined ? " " + action.amount : ""}${target}${action.kind === "hit" ? tr`：失血${action.hp_loss} / 格挡${action.blocked}${action.killed ? t(" · 击杀") : ""}` : ""}${action.kind === "play" && action.auto ? t("（自动）") : ""}`;
    };
    let cursor = 0;
    const update = () => {
      range.value = cursor;
      counter.textContent = `${data.frames.length ? cursor + 1 : 0} / ${data.frames.length}`;
      previous.disabled = cursor === 0;
      next.disabled = cursor >= data.frames.length - 1;
      const frame = data.frames[cursor];
      current.replaceChildren(
        node("strong", frame ? actionText(frame) : t("没有行动记录")),
      );
      if (frame?.action.model?.startsWith("CARD.")) {
        const preview = node("button", t("查看当时版本卡牌"), "button");
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
      state.replaceChildren(node("h3", t("当前生命与牌堆")));
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
            t("牌堆只列出录到的牌，战报有缺段时数量可能对不上。"),
            "notice",
          ),
        );
      for (const [actor, creature] of creatures)
        state.append(
          node(
            "p",
            `${actor === "self" ? t("自己") : name(actor?.split("/")[0])}：${creature.hp}${creature.maxHp !== undefined ? "/" + creature.maxHp : ""}`,
          ),
        );
      for (const power of powers.values())
        if (power.value !== 0)
          state.append(
            node(
              "p",
              `${power.target === "self" ? t("自己") : name(power.target?.split("/")[0])} · ${name(power.model)} ${power.value}`,
            ),
          );
      for (const [pile, label] of [
        ["Hand", t("手牌")],
        ["Draw", t("抽牌堆")],
        ["Discard", t("弃牌堆")],
        ["Exhaust", t("消耗堆")],
        ["Play", t("结算中")],
      ]) {
        const items = [...cards.values()].filter((card) => card.pile === pile),
          section = node("details");
        section.append(node("summary", tr`${label} · ${items.length} 张`));
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
            ? tr`第${floor.floor}层 · 没录到房间信息`
            : tr`第${floor.floor}层 · ${floor.rooms.map((room) => name(room.model) || roomNames[room.type] || room.type).join(" → ")} · 生命 ${floor.hp}/${floor.max_hp} · 金币 ${floor.gold}`,
        ),
      );
      for (const [label, items] of [
        [t("奖励"), floor.card_choices],
        [t("遗物"), floor.relic_choices],
        [t("药水"), floor.potion_choices],
      ])
        if (items?.length)
          section.append(
            node(
              "p",
              `${label}：${items.map((item) => name(item.id) + (item.upgrade ? " +" : "") + (item.picked ? t("（选中）") : t("（跳过）"))).join("、")}`,
            ),
          );
      for (const [label, items] of [
        [
          t("获得卡牌"),
          floor.cards_gained?.map(
            (item) => name(item.id) + (item.upgrade ? " +" : ""),
          ),
        ],
        [t("移除"), floor.cards_removed?.map(name)],
        [t("升级"), floor.upgraded?.map(name)],
        [
          t("变换"),
          floor.transformed?.map(
            (item) => `${name(item.from)} → ${name(item.to)}`,
          ),
        ],
        [
          t("附魔"),
          floor.enchanted?.map(
            (item) => `${name(item.card)} · ${name(item.enchantment)}`,
          ),
        ],
        [t("购买遗物"), floor.bought_relics?.map(name)],
        [t("购买药水"), floor.bought_potions?.map(name)],
        [t("使用药水"), floor.potions_used?.map(name)],
      ])
        if (items?.length)
          section.append(node("p", `${label}：${items.join("、")}`));
      if (floor.events?.length)
        section.append(
          node(
            "p",
            tr`事件：${floor.events.map((key) => (catalog?.labels?.[language] ?? catalog?.labels?.eng)?.[key] ?? key).join("、")}`,
          ),
        );
      if (floor.rests?.length)
        section.append(node("p", tr`休息点：${floor.rests.join("、")}`));
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
            tr`房间 ${roomIndex + 1} · ${name(floor.rooms[roomIndex]?.model)}`,
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
              tr`战斗尝试 ${index + 1}${index === attempts.length - 1 ? t(" · 最终尝试") : ""}`,
            ),
          );
          const frames = data.frames
            .map((frame, index) => ({ frame, index }))
            .filter((item) => item.frame.attempt === attempt);
          for (const round of [
            ...new Set(frames.map((item) => item.frame.action.round)),
          ]) {
            const turn = node("details");
            turn.append(node("summary", tr`回合 ${round}`));
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
