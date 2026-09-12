// Public chart data are additive bins. No player IDs, run IDs, routes or individual records leave this projection.
const number = (value) => typeof value === "number" && Number.isFinite(value);
const slot = (player) => String(player.net_id);
const percentBucket = (value) => Math.min(90, Math.floor(value * 10) * 10);
export function a10Cohorts(runs) {
  const players = new Map();
  for (const run of runs.filter((run) => run.ascension === 10))
    for (const player of run.players) {
      const sample = players.get(slot(player)) ?? { n: 0, wins: 0 };
      sample.n++;
      sample.wins += Number(run.win);
      players.set(slot(player), sample);
    }
  return new Map(
    [...players]
      .filter(([, value]) => value.n >= 20)
      .map(([id, value]) => [
        id,
        `${percentBucket(value.wins / value.n)}-${percentBucket(value.wins / value.n) + 10}%`,
      ]),
  );
}

export function chartBins(runs) {
  const bins = new Map();
  const add = (chart, x, value = 1, win = false, series = "all", y = null) => {
    if (value === null || !number(value)) return;
    const key = JSON.stringify([chart, x, series, y]);
    const bin = bins.get(key) ?? { chart, x, series, y, n: 0, sum: 0, wins: 0 };
    bin.n++;
    bin.sum += value;
    bin.wins += Number(win);
    bins.set(key, bin);
  };
  for (const run of runs) {
    const date = run.at.slice(0, 10),
      win = run.win;
    const death = run.rooms.at(-1)?.rooms.at(-1);
    add("winrate-over-time", date, 1, win);
    add("winrate-by-ascension", run.ascension, 1, win);
    add("runs-over-time", date);
    for (let floor = 1; floor <= run.floor; floor++)
      add("winrate-by-floor", floor, 1, win);
    if (run.daily)
      add("hardest-dailies", String(run.daily).slice(0, 10), 1, win);
    if (!win) {
      add("deaths-by-floor", run.floor);
      if (death) add("deaths-by-room", death.model_id ?? death.room_type);
    }
    for (let act = 0; act < (run.actFloors?.length ?? 0); act++)
      if (run.actFloors[act] > 0) add("acts-funnel", act + 1);
    if (win && number(run.winTime) && run.winTime > 0) {
      add("avg-win-time-daily", date, run.winTime / 60);
      add("time-to-win", Math.floor(run.winTime / 300) * 5);
    }
    if (number(run.duration))
      add(
        "stat-histogram",
        Math.floor(run.duration / 300) * 5,
        1,
        win,
        "duration",
      );
    add("stat-histogram", run.floor, 1, win, "floor");
    const elites = run.rooms.reduce(
      (sum, point) =>
        sum +
        point.rooms.filter((room) => room.room_type.toLowerCase() === "elite")
          .length,
      0,
    );
    add("elites-vs-winrate", elites, 1, win);
    add("winrate-by-stat", elites, 1, win, "elites");
    if (number(run.duration))
      add(
        "stat-scatter",
        Math.floor(run.duration / 300) * 5,
        1,
        win,
        "duration-floor",
        run.floor,
      );
    for (const player of run.players) {
      const id = slot(player);
      let smiths = 0;
      const copies = new Map();
      for (const card of player.deck ?? []) {
        copies.set(card.id, (copies.get(card.id) ?? 0) + 1);
        if (card.enchantment?.id)
          add("enchant-winrate", card.enchantment.id, 1, win);
      }
      for (const [model, count] of copies) {
        add("entity-copies", count, 1, win, model);
        add("entity-over-time", date, 1, win, model);
      }
      for (const relic of player.relics ?? []) {
        const model = typeof relic === "string" ? relic : relic.id;
        if (model) add("entity-over-time", date, 1, win, model);
      }
      for (const [index, point] of run.rooms.entries()) {
        const floor = index + 1,
          stats = point.player_stats.find(
            (stats) => String(stats.player_id) === id,
          );
        if (!stats) continue;
        if (
          number(stats.current_hp) &&
          number(stats.max_hp) &&
          stats.max_hp > 0
        )
          add(
            "hp-trajectory",
            floor,
            (100 * stats.current_hp) / stats.max_hp,
            win,
          );
        if (number(stats.current_gold))
          add("gold-curve", floor, stats.current_gold, win);
        if (number(stats.damage_taken))
          add("hp-loss-by-floor", floor, stats.damage_taken, win);
        const measured = run.floorMeasurements?.[`${floor}/${id}`];
        if (
          measured &&
          String(measured.player_id) === id &&
          number(measured.deck_size)
        )
          add("deck-growth", floor, measured.deck_size, win);
        for (const rest of stats.rest_site_choices ?? [])
          if (rest.toLowerCase() === "smith") smiths++;
        for (const option of stats.event_choices ?? []) {
          // LocString serialization stores a table/key, never use its variable values as public text.
          const title =
            typeof option.title === "string" ? option.title : option.title?.key;
          if (typeof title === "string" && /^[A-Za-z0-9_.]+$/.test(title))
            add("event-outcomes", title, 1, win);
        }
        for (const potion of stats.potion_used ?? [])
          add("entity-over-time", date, 1, win, potion);
        const combatRooms = point.rooms.filter((room) =>
          ["monster", "elite", "boss"].includes(room.room_type.toLowerCase()),
        );
        for (const [roomIndex, room] of point.rooms.entries()) {
          if (!combatRooms.includes(room)) continue;
          const combat = run.combats.find(
            (combat) =>
              combat.floor === floor && combat.room_index === roomIndex,
          );
          const metrics = combat?.players.find(
            (p) => String(p.player_id) === id,
          )?.metrics;
          if (
            metrics &&
            combat.measurement_version === 3 &&
            combat.coverage === "complete"
          ) {
            add("encounter-damage", room.model_id, metrics.hp_lost, win);
          } else if (combatRooms.length === 1 && number(stats.damage_taken))
            add("encounter-damage", room.model_id, stats.damage_taken, win);
          if (number(room.turns_taken))
            add("encounter-turns", room.model_id, room.turns_taken, win);
          add("encounter-histogram", room.model_id);
        }
      }
      add("smiths-vs-winrate", smiths, 1, win);
      add("winrate-by-stat", smiths, 1, win, "smiths");
      add("winrate-by-stat", (player.deck ?? []).length, 1, win, "deck");
      add("stat-histogram", (player.deck ?? []).length, 1, win, "deck");
    }
  }
  return [...bins.values()];
}

export function mechanismBins(runs) {
  const rows = new Map();
  for (const run of runs)
    for (const combat of run.combats) {
      if (combat.measurement_version !== 3 || combat.coverage !== "complete")
        continue;
      for (const player of combat.players) {
        if (!player.metrics) continue;
        const vitals = Object.fromEntries(
          [
            "hp_lost",
            "blocked",
            "block_generated",
            "healed",
            "damage",
            "enemy_blocked",
            "kills",
          ].map((key) => [key, player.metrics[key]]),
        );
        for (const [group, values] of Object.entries({
          damage_source: player.metrics.damage_by_source,
          mechanic: player.metrics.mechanics,
          power: player.metrics.power_changes,
          vitals,
        }))
          for (const [id, sum] of Object.entries(values ?? {})) {
            if (!number(sum)) continue;
            const key = `${combat.version}/${group}/${id}`;
            const row = rows.get(key) ?? {
              version: combat.version,
              group,
              id,
              n: 0,
              sum: 0,
            };
            row.n++;
            row.sum += sum;
            rows.set(key, row);
          }
        for (const [id, counts] of Object.entries(
          player.metrics.card_variants ?? {},
        )) {
          const key = `${combat.version}/card/${id}`;
          const row = rows.get(key) ?? {
            version: combat.version,
            group: "card",
            id,
            n: 0,
            drawn: 0,
            manual_plays: 0,
            auto_plays: 0,
            started: 0,
            finished: 0,
            energy_spent: 0,
            stars_spent: 0,
            damage: 0,
            enemy_blocked: 0,
            block: 0,
            generated: 0,
            discarded: 0,
            exhausted: 0,
          };
          row.n++;
          for (const field of Object.keys(row).filter(
            (field) => !["version", "group", "id", "n"].includes(field),
          ))
            row[field] += counts[field] ?? 0;
          rows.set(key, row);
        }
      }
    }
  return [...rows.values()];
}
