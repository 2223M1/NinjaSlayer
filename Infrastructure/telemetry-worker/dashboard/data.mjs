const CHARACTER = 'CHARACTER.NINJA_SLAYER_CHARACTER_NINJA_SLAYER_CHARACTER';
const COMBAT_ROOMS = new Set(['monster', 'elite', 'boss']);
const USE_FIELDS = ['drawn', 'started', 'finished', 'manual_plays', 'auto_plays', 'energy_spent', 'stars_spent'];

// RitsuLib uploads SerializableRun, not the differently shaped RunHistory UI record.
export function parseData(text) {
  return JSON.parse(text, (key, value, context) =>
    ['net_id', 'player_id'].includes(key) && typeof value === 'number' ? context.source : value);
}

function validPlayer(player) {
  return player && typeof player.character_id === 'string'
    && /^(0|[1-9]\d*)$/.test(player.net_id)
    && (typeof player.net_id === 'string' || Number.isSafeInteger(player.net_id))
    && (player.deck == null || (Array.isArray(player.deck) && player.deck.every(card => typeof card?.id === 'string')));
}

function readCombats(payload, rooms, playerIds) {
  const raw = payload?.combats;
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return { combats: [], invalid: 0 };
  const combats = [];
  let invalid = 0;
  for (const [key, combat] of Object.entries(raw)) {
    const room = rooms[combat?.floor - 1]?.rooms?.[combat?.room_index];
    if (!room || key !== `${combat.floor}/${combat.room_index}` || room.model_id !== combat.encounter
        || !Number.isInteger(combat.rounds) || combat.rounds < 0 || typeof combat.won !== 'boolean'
        || typeof combat.version !== 'string' || !Array.isArray(combat.players)
        || combat.players.some(player => !player || !playerIds.has(String(player.player_id)) || !player.cards
          || Object.entries(player.cards).some(([id, counts]) => !id.startsWith('CARD.')
            || USE_FIELDS.some(field => !Number.isSafeInteger(counts?.[field]) || counts[field] < 0)))
        || new Set(combat.players.map(p => String(p.player_id))).size !== combat.players.length) {
      invalid++; continue;
    }
    combats.push(combat);
  }
  return { combats, invalid };
}

export function normalizeEvents(input) {
  const rows = input?.batch ?? input?.results ?? input;
  if (!Array.isArray(rows)) throw new Error('请选择 RitsuLib batch、PostHog 查询结果或事件数组 JSON。');
  const byRun = new Map(), conflicted = new Set();
  let rejected = 0, duplicates = 0, invalidCombats = 0;
  for (const row of rows) {
    const properties = Array.isArray(row) ? row[2] : row?.properties;
    let p;
    try { p = typeof properties === 'string' ? parseData(properties) : properties; }
    catch { rejected++; continue; }
    const history = p?.payload?.applicant_payload?.run_history;
    if ((!Array.isArray(row) && row?.event !== 'run_history.completed') || p?.applicant_id !== 'NinjaSlayer'
        || p.is_abandoned !== false || typeof p.is_victory !== 'boolean'
        || !Array.isArray(history?.players) || !history.players.every(validPlayer)
        || !Array.isArray(history.map_point_history) || !history.map_point_history.every(Array.isArray)
        || typeof history.rng?.seed !== 'string' || !Number.isFinite(history.start_time)
        || !Number.isInteger(history.ascension)) {
      rejected++; continue;
    }
    const players = history.players.filter(player => player.character_id === CHARACTER);
    const at = p.occurred_at_utc ?? (Array.isArray(row) ? row[1] : row.timestamp);
    const ids = history.players.map(player => String(player.net_id));
    const rooms = history.map_point_history.flat();
    if (!players.length || new Set(ids).size !== ids.length || !Number.isFinite(Date.parse(at))
        || rooms.some(room => !room || !Array.isArray(room.rooms) || room.rooms.some(entry => typeof entry?.room_type !== 'string')
          || !Array.isArray(room.player_stats)
          || room.player_stats.some(stat => !stat || !ids.includes(String(stat.player_id))
            || (stat.card_choices != null && (!Array.isArray(stat.card_choices)
              || stat.card_choices.some(choice => typeof choice?.card?.id !== 'string' || typeof choice.was_picked !== 'boolean')))
            || (stat.cards_removed != null && (!Array.isArray(stat.cards_removed) || stat.cards_removed.some(card => typeof card?.id !== 'string')))
            || (stat.upgraded_cards != null && (!Array.isArray(stat.upgraded_cards) || stat.upgraded_cards.some(id => typeof id !== 'string')))))) {
      rejected++; continue;
    }
    const identity = JSON.stringify([history.rng.seed, history.start_time, [...ids].sort()]);
    const balance = p.payload.private_contributions?.NinjaSlayer?.ninja_slayer_balance_context;
    const metrics = balance?.balance_schema === 'ninja_slayer_run_history_v2'
      ? readCombats(p.payload.applicant_payload.mod_payload, rooms, new Set(ids)) : { combats: [], invalid: 0 };
    invalidCombats += metrics.invalid;
    const run = {
      at, version: balance?.version ?? '未记录', gameVersion: p.game_version ?? '未记录',
      mode: p.run_game_mode ?? history.game_mode, reloads: history.num_reloads ?? null,
      ascension: history.ascension, win: p.is_victory, floor: rooms.length,
      players, playerCount: history.players.length, rooms, combats: metrics.combats,
    };
    const previous = byRun.get(identity);
    if (previous) {
      duplicates++;
      if (previous.win !== run.win) conflicted.add(identity);
      // Prefer a snapshot with combat measurements; never sum repeated uploads of one run.
      if (previous.combats.length > run.combats.length || (previous.combats.length === run.combats.length && previous.at >= run.at)) continue;
    }
    byRun.set(identity, run);
  }
  return { runs: [...byRun].filter(([id]) => !conflicted.has(id)).map(([, run]) => run),
    rejected, duplicates, conflicts: conflicted.size, invalidCombats };
}

export function summarize(runs, catalog, filters = {}, now = Date.now()) {
  const cutoff = filters.days ? Date.parse(new Date(now).toISOString().slice(0, 10)) - (Number(filters.days) - 1) * 86400000 : -Infinity;
  const selected = runs.filter(run => Date.parse(run.at) >= cutoff
    && (!filters.version || run.version === filters.version)
    && (!filters.gameVersion || run.gameVersion === filters.gameVersion)
    && (!filters.mode || run.mode === filters.mode)
    && (!filters.party || (filters.party === 'solo' ? run.playerCount === 1 : run.playerCount > 1))
    && (!filters.reloads || run.reloads === 0)
    && (filters.ascension === '' || filters.ascension == null || run.ascension === Number(filters.ascension)));
  const empty = card => ({ ...card, offered: 0, picked: 0, held: 0, wins: 0, removed: 0, upgraded: 0,
    pickFloorTotal: 0, chosenRuns: 0, chosenWins: 0, skippedRuns: 0, skippedWins: 0,
    combatSamples: 0, drawn: 0, started: 0, finished: 0, manual_plays: 0, auto_plays: 0, energy_spent: 0, stars_spent: 0 });
  const stats = new Map(catalog.map(card => [card.id, empty(card)]));
  const getCard = id => {
    if (!stats.has(id)) stats.set(id, empty({ id, name: id.split('.').at(-1), rarity: '历史 / 其他', type: '' }));
    return stats.get(id);
  };
  const trend = new Map();
  let playerSamples = 0, measuredCombats = 0, totalCombats = 0;
  for (const run of selected) {
    const day = new Date(run.at).toISOString().slice(0, 10);
    if (!trend.has(day)) trend.set(day, { date: day, runs: 0, wins: 0 });
    trend.get(day).runs++;
    trend.get(day).wins += Number(run.win);
    for (const player of run.players) {
      playerSamples++;
      const offered = new Set(), chosen = new Set();
      for (const id of new Set((player.deck ?? []).map(card => card.id))) {
        const card = getCard(id);
        card.held++; card.wins += Number(run.win);
      }
      for (const [floor, room] of run.rooms.entries()) {
        totalCombats += room.rooms.filter(entry => COMBAT_ROOMS.has(entry.room_type)).length;
        const entry = room.player_stats.find(stat => String(stat.player_id) === String(player.net_id));
        for (const choice of entry?.card_choices ?? []) {
          const card = getCard(choice.card.id);
          card.offered++; offered.add(card.id);
          if (choice.was_picked) { card.picked++; card.pickFloorTotal += floor + 1; chosen.add(card.id); }
        }
        for (const card of entry?.cards_removed ?? []) getCard(card.id).removed++;
        for (const id of entry?.upgraded_cards ?? []) getCard(id).upgraded++;
      }
      for (const id of offered) {
        const card = getCard(id);
        if (chosen.has(id)) { card.chosenRuns++; card.chosenWins += Number(run.win); }
        else { card.skippedRuns++; card.skippedWins += Number(run.win); }
      }
      for (const combat of run.combats) {
        if (filters.version && combat.version !== filters.version) continue;
        const measured = combat.players.find(stat => String(stat.player_id) === String(player.net_id));
        if (!measured) continue;
        measuredCombats++;
        for (const [id, counts] of Object.entries(measured.cards)) {
          const card = getCard(id);
          card.combatSamples++;
          for (const field of USE_FIELDS) card[field] += counts[field];
        }
      }
    }
  }
  return {
    runs: selected.length, wins: selected.filter(run => run.win).length, playerSamples, measuredCombats, totalCombats,
    averageFloor: selected.length ? selected.reduce((sum, run) => sum + run.floor, 0) / selected.length : null,
    cards: [...stats.values()], trend: [...trend.values()].sort((a, b) => a.date.localeCompare(b.date)),
    versions: [...new Set(runs.map(run => run.version))].sort((a, b) => b.localeCompare(a, undefined, { numeric: true })),
    gameVersions: [...new Set(runs.map(run => run.gameVersion))].sort(),
    modes: [...new Set(runs.map(run => run.mode))].sort(),
    ascensions: [...new Set(runs.map(run => run.ascension))].sort((a, b) => a - b),
  };
}
