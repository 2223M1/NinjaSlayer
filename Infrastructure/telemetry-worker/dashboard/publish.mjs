import { summarize } from './data.mjs';
import { choiceCounters, combatCounters, useCurrentCatalog } from './public-data.mjs';
import { readFile } from 'node:fs/promises';
import { a10Cohorts, chartBins, mechanismBins } from './chart-data.mjs';

const contentRoot = new URL('../../../Website/content/', import.meta.url);
export async function readCurrentRelease() {
  return JSON.parse(await readFile(new URL('current.json', contentRoot), 'utf8'));
}
export async function readCatalog() {
  const { version } = await readCurrentRelease();
  const exported = JSON.parse(await readFile(new URL('versions/' + version + '/catalog.json', contentRoot), 'utf8'));
  return exported.languages.zhs.filter(model => model.kind === 'card' && model.mod).map(card => ({
    id: card.id, name: card.variants[0].name, image: card.image, thumbnail: card.thumbnail,
    rarity: {Common:'白卡',Uncommon:'蓝卡',Rare:'金卡',Basic:'初始',Token:'衍生',Ancient:'先古',Event:'事件',Special:'特殊',Status:'状态'}[card.rarity] ?? card.rarity,
    type: {Attack:'攻击',Skill:'技能',Power:'能力',Status:'状态',Curse:'诅咒'}[card.type] ?? card.type
  }));
}

export function publicFeedback(records) {
  return records.filter(item => item.context.publishDescription === true).map(item => ({
    id: item.id, at: item.at, description: item.description, category: item.category,
    gameVersion: item.gameVersion, context: { modVersion: item.context.modVersion },
  }));
}

export function publishSnapshot(telemetry, catalog) {
  const grouped = new Map();
  const cohorts = a10Cohorts(telemetry.runs);
  for (const run of telemetry.runs) {
    const dimensions = { date: new Date(run.at).toISOString().slice(0, 10), version: run.version, gameVersion: run.gameVersion, mode: run.mode,
      party: run.playerCount === 1 ? 'solo' : 'multi', ascension: run.ascension, noReloads: run.reloads === 0,
      outcome: run.win ? 'win' : 'loss',
      a10: run.playerCount === 1 ? cohorts.get(String(run.players[0].net_id)) ?? 'unranked' : 'multiplayer' };
    const key = JSON.stringify(dimensions);
    if (!grouped.has(key)) grouped.set(key, { dimensions, runs: [] });
    grouped.get(key).runs.push(run);
  }
  const groups = [...grouped.values()].map(({ dimensions, runs }) => {
    const summary = summarize(runs, catalog);
    const combats = [...new Set(runs.flatMap(run => run.combats.map(combat => combat.version)))].map(version => {
      const measured = summarize(runs.map(run => ({ ...run, combats: run.combats.filter(combat => combat.version === version) })), catalog);
      return { version, measuredCombats: measured.measuredCombats,
        cards: measured.cards.filter(card => card.combatSamples).map(card => ({ id: card.id, ...Object.fromEntries(combatCounters.map(key => [key, card[key]])) })) };
    });
    return { ...dimensions, runs: summary.runs, wins: summary.wins, playerSamples: summary.playerSamples,
      charts: chartBins(runs), mechanisms: mechanismBins(runs),
      totalCombats: summary.totalCombats, combats,
      floorTotal: runs.reduce((sum, run) => sum + run.floor, 0),
      cards: summary.cards.filter(card => choiceCounters.some(key => card[key])).map(card => ({ id: card.id, ...Object.fromEntries(choiceCounters.map(key => [key, card[key]])) })) };
  });
  return useCurrentCatalog({ groups,
    excluded: { rejected: telemetry.rejected, duplicates: telemetry.duplicates, conflicts: telemetry.conflicts, invalidCombats: telemetry.invalidCombats } }, catalog);
}
