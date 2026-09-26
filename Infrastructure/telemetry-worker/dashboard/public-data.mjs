import { selectGroups } from './charts.mjs';
export const choiceCounters = ['offered', 'picked', 'held', 'wins', 'removed', 'upgraded', 'pickFloorTotal', 'chosenRuns', 'chosenWins', 'skippedRuns', 'skippedWins'];
export const combatCounters = ['combatSamples', 'drawn', 'started', 'finished', 'manual_plays', 'auto_plays', 'energy_spent', 'stars_spent'];
const counters = [...choiceCounters, ...combatCounters];

// A telemetry row is evidence of a play, not a catalog entry. Apply the same
// current-catalog join to fresh aggregates and to the last successful snapshot.
export function useCurrentCatalog(snapshot, catalog) {
  const ids = new Set(catalog.map(card => card.id));
  const current = id => typeof id !== 'string' || !id.startsWith('CARD.') || ids.has(id.split('/')[0]);
  return { ...snapshot, catalog, groups: snapshot.groups.map(group => ({
    ...group,
    cards: group.cards.filter(card => ids.has(card.id)),
    combats: group.combats.map(combat => ({ ...combat, cards: combat.cards.filter(card => ids.has(card.id)) })),
    charts: (group.charts ?? []).filter(point => current(point.x) && current(point.series)),
    mechanisms: (group.mechanisms ?? []).filter(row => current(row.id)),
  })) };
}

export function summarizePublic(snapshot, filters = {}, now = Date.now()) {
  const { catalog, groups, sources, feedback, excluded } = snapshot;
  const selected = selectGroups(snapshot, filters, now);
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
