import { summarize } from './data.mjs';
import { choiceCounters, combatCounters } from './public-data.mjs';
import { readFile } from 'node:fs/promises';

export async function readCatalog() {
  const repo = new URL('../../../', import.meta.url);
  const names = JSON.parse(await readFile(new URL('NinjaSlayer/localization/zhs/cards.json', repo), 'utf8'));
  const specs = JSON.parse(await readFile(new URL('Tests/NinjaSlayer.OrbContractTests/card-metadata.json', repo), 'utf8'));
  return specs.filter(card => !card.Upgraded).map(card => ({
    id: card.Id, name: names[`${card.Id.split('.').at(-1)}.title`] ?? card.Id,
    rarity: { Common: '白卡', Uncommon: '蓝卡', Rare: '金卡', Basic: '初始', Token: '衍生', Ancient: '先古', Event: '事件', Special: '特殊' }[card.Rarity] ?? card.Rarity,
    type: { Attack: '攻击', Skill: '技能', Power: '能力', Status: '状态', Curse: '诅咒' }[card.Type],
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
  for (const run of telemetry.runs) {
    const dimensions = { date: new Date(run.at).toISOString().slice(0, 10), version: run.version, gameVersion: run.gameVersion, mode: run.mode,
      party: run.playerCount === 1 ? 'solo' : 'multi', ascension: run.ascension, noReloads: run.reloads === 0 };
    const key = JSON.stringify(dimensions);
    if (!grouped.has(key)) grouped.set(key, { dimensions, runs: [] });
    grouped.get(key).runs.push(run);
  }
  const allCards = new Map(catalog.map(card => [card.id, card]));
  const groups = [...grouped.values()].map(({ dimensions, runs }) => {
    const summary = summarize(runs, []);
    for (const { id, name, rarity, type } of summary.cards) if (!allCards.has(id)) allCards.set(id, { id, name, rarity, type });
    const combats = [...new Set(runs.flatMap(run => run.combats.map(combat => combat.version)))].map(version => {
      const measured = summarize(runs.map(run => ({ ...run, combats: run.combats.filter(combat => combat.version === version) })), []);
      return { version, measuredCombats: measured.measuredCombats,
        cards: measured.cards.filter(card => card.combatSamples).map(card => ({ id: card.id, ...Object.fromEntries(combatCounters.map(key => [key, card[key]])) })) };
    });
    return { ...dimensions, runs: summary.runs, wins: summary.wins, playerSamples: summary.playerSamples,
      totalCombats: summary.totalCombats, combats,
      floorTotal: runs.reduce((sum, run) => sum + run.floor, 0),
      cards: summary.cards.map(card => ({ id: card.id, ...Object.fromEntries(choiceCounters.map(key => [key, card[key]])) })) };
  });
  return { catalog: [...allCards.values()], groups,
    excluded: { rejected: telemetry.rejected, duplicates: telemetry.duplicates, conflicts: telemetry.conflicts, invalidCombats: telemetry.invalidCombats } };
}
