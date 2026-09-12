export const choiceCounters = ['offered', 'picked', 'held', 'wins', 'removed', 'upgraded', 'pickFloorTotal', 'chosenRuns', 'chosenWins', 'skippedRuns', 'skippedWins'];
export const combatCounters = ['combatSamples', 'drawn', 'started', 'finished', 'manual_plays', 'auto_plays', 'energy_spent', 'stars_spent'];
const counters = [...choiceCounters, ...combatCounters];

export function summarizePublic(snapshot, filters = {}, now = Date.now()) {
  const { catalog, groups, sources, feedback, excluded } = snapshot;
  const cutoff = filters.days ? new Date(Date.UTC(new Date(now).getUTCFullYear(), new Date(now).getUTCMonth(), new Date(now).getUTCDate())
    - (Number(filters.days) - 1) * 86_400_000).toISOString().slice(0, 10) : '';
  const selected = groups.filter(group => (!cutoff || group.date >= cutoff)
    && (!filters.version || group.version === filters.version) && (!filters.gameVersion || group.gameVersion === filters.gameVersion)
    && (!filters.mode || group.mode === filters.mode) && (!filters.party || group.party === filters.party)
    && (!filters.reloads || group.noReloads) && (filters.ascension === '' || filters.ascension == null || group.ascension === Number(filters.ascension)));
  const cards = new Map(catalog.map(card => [card.id, { ...card, ...Object.fromEntries(counters.map(key => [key, 0])) }]));
  const result = { runs: 0, wins: 0, playerSamples: 0, measuredCombats: 0, totalCombats: 0, floorTotal: 0 };
  const dates = new Map();
  for (const group of selected) {
    for (const key of ['runs', 'wins', 'playerSamples', 'totalCombats', 'floorTotal']) result[key] += group[key];
    const day = dates.get(group.date) ?? { date: group.date, runs: 0, wins: 0 };
    day.runs += group.runs; day.wins += group.wins; dates.set(group.date, day);
    for (const card of group.cards) for (const key of choiceCounters) cards.get(card.id)[key] += card[key];
    for (const combat of group.combats) {
      if (filters.version && combat.version !== filters.version) continue;
      result.measuredCombats += combat.measuredCombats;
      for (const card of combat.cards) for (const key of combatCounters) cards.get(card.id)[key] += card[key];
    }
  }
  return { ...result, averageFloor: result.runs ? result.floorTotal / result.runs : null, cards: [...cards.values()],
    trend: [...dates.values()].sort((a, b) => a.date.localeCompare(b.date)),
    versions: [...new Set(groups.map(group => group.version))].sort((a, b) => b.localeCompare(a, undefined, { numeric: true })),
    gameVersions: [...new Set(groups.map(group => group.gameVersion))].sort(), modes: [...new Set(groups.map(group => group.mode))].sort(),
    ascensions: [...new Set(groups.map(group => group.ascension))].sort((a, b) => a - b),
    sources, feedback, feedbackWarnings: [], ...excluded, view: 'public' };
}

export { counters };
