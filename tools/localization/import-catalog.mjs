import { readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { createHash } from 'node:crypto';
import { languages } from '../../Infrastructure/telemetry-worker/dashboard/languages.mjs';
import { applyCatalogLocalization } from '../../Infrastructure/telemetry-worker/dashboard/catalog-localization.mjs';
const exported = JSON.parse(readFileSync(resolve(process.argv[2]), 'utf8'));
const current = JSON.parse(readFileSync('Website/content/current.json', 'utf8'));
const baseBytes = readFileSync(`Website/content/versions/${current.version}/catalog.json`);
const base = JSON.parse(baseBytes);
if (exported.version !== base.version || exported.sourceRevision !== base.sourceRevision || exported.dllSha256 !== base.dllSha256)
  throw new Error('Translations must use the exact published gameplay DLL.');
if (createHash('sha256').update(baseBytes).digest('hex') !== current.fingerprint) throw new Error('Published catalog fingerprint mismatch.');
const english = new Map(exported.languages.eng.map(model => [model.id, model]));
// The immutable published English catalog captured three native enchantment labels
// from a prior locale and three image readbacks. Do not rewrite that historical file.
const oldNativeLabels = { 'RELIC.PAELS_CLAW': ['黏糊','Goopy'],
  'RELIC.PAELS_GROWTH': ['克隆','Clone'], 'RELIC.ROYAL_STAMP': ['王室认证','Royally Approved'] };
const oldImageReadbacks = new Set(['CARD.NINJA_SLAYER_CARD_ADAPT','CARD.NINJA_SLAYER_CARD_NAVY_HAMMER','RELIC.YUMMY_COOKIE']);
for (const model of base.languages.eng) {
  const actual = structuredClone(english.get(model.id)), expected = structuredClone(model);
  if (oldImageReadbacks.has(model.id)) actual.image = expected.image;
  if (oldNativeLabels[model.id]) expected.description = expected.description.replaceAll(...oldNativeLabels[model.id]);
  if (JSON.stringify(actual) !== JSON.stringify(expected)) throw new Error('The export changed published English rules or unexpected art/text: ' + model.id);
}
const baseModels = new Map(base.languages.eng.map(model => [model.id, model]));
for (const lang of languages.filter(code => !base.languages[code])) {
  const directory = join('NinjaSlayer/localization', lang);
  const hash = createHash('sha256');
  for (const file of readdirSync(directory).filter(name => name.endsWith('.json')).sort()) hash.update(file).update(readFileSync(join(directory, file)));
  const supplement = { schemaVersion: 1, version: base.version, language: lang,
    baseSourceRevision: base.sourceRevision, baseDllSha256: base.dllSha256, baseFingerprint: current.fingerprint,
    localizationSha256: hash.digest('hex'), models: exported.languages[lang].map(model => {
      const source = baseModels.get(model.id), localized = structuredClone(model);
      // This task publishes translations, never a replacement of published art.
      for (const key of ['image','thumbnail','imagePath']) if (Object.hasOwn(source, key)) localized[key] = source[key];
      return localized;
    }), labels: exported.labels[lang] };
  applyCatalogLocalization(base, supplement, lang);
  const destination = `Website/content/localization/${base.version}`;
  mkdirSync(destination, { recursive: true });
  writeFileSync(`${destination}/${lang}.json`, JSON.stringify(supplement) + '\n');
  console.log(`${lang}: ${supplement.models.length} runtime models, published rules unchanged.`);
}
