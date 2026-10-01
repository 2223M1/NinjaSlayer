// Public chart data are additive bins. No player IDs, run IDs, routes or individual records leave this projection.
const number = (value) => typeof value === "number" && Number.isFinite(value);
const slot = (player) => String(player.net_id);
export function chartBins(runs) {
  const bins = new Map();
  const add = (chart, x, value = 1, win = false) => {
    if (!number(value)) return;
    const key = JSON.stringify([chart, x]);
    const bin = bins.get(key) ?? { chart, x, n: 0, sum: 0, wins: 0 };
    bin.n++;
    bin.sum += value;
    bin.wins += Number(win);
    bins.set(key, bin);
  };
  for (const run of runs) {
    const win = run.win;
    add("ascension-wins", run.ascension, 1, win);
    if (!win) add("floor-deaths", run.floor);
    for (let act = 0; act < run.actFloors.length; act++)
      if (run.actFloors[act] > 0) add("act-reach", act + 1);
    const elites = run.rooms.reduce(
      (sum, point) =>
        sum + point.rooms.filter((room) => room.room_type.toLowerCase() === "elite").length,
      0,
    );
    add("elite-wins", elites, 1, win);
    for (const point of run.rooms)
      for (const room of point.rooms)
        if (["monster", "elite", "boss"].includes(room.room_type.toLowerCase()) && number(room.turns_taken))
          add("enemy-turns", room.model_id, room.turns_taken, win);
    for (const player of run.players) {
      const id = slot(player);
      add("deck-wins", Math.floor((player.deck ?? []).length / 5) * 5, 1, win);
      for (const [index, point] of run.rooms.entries()) {
        const floor = index + 1,
          stats = point.player_stats.find((stats) => String(stats.player_id) === id);
        if (!stats) continue;
        if (number(stats.current_hp) && number(stats.max_hp) && stats.max_hp > 0)
          add("hp-by-floor", floor, (100 * stats.current_hp) / stats.max_hp, win);
        const combatRooms = point.rooms.filter((room) =>
          ["monster", "elite", "boss"].includes(room.room_type.toLowerCase()),
        );
        for (const [roomIndex, room] of point.rooms.entries()) {
          if (!combatRooms.includes(room)) continue;
          const combat = run.combats.find(
            (combat) => combat.floor === floor && combat.room_index === roomIndex,
          );
          const metrics = combat?.players.find((p) => String(p.player_id) === id)?.metrics;
          if (metrics && combat.measurement_version === 3 && combat.coverage === "complete")
            add("enemy-damage", room.model_id, metrics.hp_lost, win);
          else if (combatRooms.length === 1 && number(stats.damage_taken))
            add("enemy-damage", room.model_id, stats.damage_taken, win);
        }
      }
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
