import test from 'node:test';
import assert from 'node:assert/strict';
import { applyCatalogLocalization } from '../dashboard/catalog-localization.mjs';
const base = { version: '1.0.15', sourceRevision: 'source', dllSha256: 'dll', languages: { eng: [
  { id: 'CARD.A', kind: 'card', mod: true, type: 'Attack', rarity: 'Common', image: 'images/a.webp', variants: [
    { name: 'Strike', description: 'Deal 6 damage.', cost: 1, costsX: false, upgraded: false, keywords: [] },
  ] },
] }, labels: { eng: { 'event.title': 'Event' } } };
const supplement = () => ({ schemaVersion: 1, language: 'fra', version: base.version,
  baseSourceRevision: base.sourceRevision, baseDllSha256: base.dllSha256,
  models: [{ ...structuredClone(base.languages.eng[0]), variants: [
    { ...structuredClone(base.languages.eng[0].variants[0]), name: 'Frappe', description: 'Infligez 6 dégâts.' },
  ] }], labels: { 'event.title': 'Événement' } });
test('supplement changes display copy without mutating the immutable catalog', () => {
  const old = structuredClone(base), output = applyCatalogLocalization(base, supplement(), 'fra');
  assert.equal(output.languages.fra[0].variants[0].name, 'Frappe');
  assert.deepEqual(base, old);
  assert.equal(output.languages.eng[0].variants[0].name, 'Strike');
});
test('supplement rejects mismatched artifacts, missing models, changed rules and art', () => {
  for (const mutate of [
    x => { x.baseDllSha256 = 'wrong'; }, x => { x.language = 'rus'; },
    x => { x.models = []; }, x => { x.models[0].id = 'CARD.B'; },
    x => { x.models[0].variants[0].cost = 0; }, x => { x.models[0].variants[0].keywords = ['Exhaust']; },
    x => { x.models[0].image = 'images/b.webp'; }, x => { x.models[0].type = 'Skill'; },
  ]) { const value = supplement(); mutate(value); assert.throws(() => applyCatalogLocalization(base, value, 'fra')); }
});
