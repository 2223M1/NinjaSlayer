// Supplemental translations never rewrite an immutable, published game catalog.
export function applyCatalogLocalization(catalog, supplement, lang) {
  if (supplement.schemaVersion !== 1 || supplement.version !== catalog.version
      || supplement.baseSourceRevision !== catalog.sourceRevision
      || supplement.baseDllSha256 !== catalog.dllSha256 || supplement.language !== lang)
    throw new Error('Catalog translation belongs to a different game artifact.');
  const base = catalog.languages.eng;
  if (!Array.isArray(supplement.models) || supplement.models.length !== base.length)
    throw new Error('Catalog translation has missing models.');
  const original = new Map(base.map(model => [model.id, model]));
  const seen = new Set();
  for (const model of supplement.models) {
    const source = original.get(model.id);
    if (!source || source.kind !== model.kind || seen.has(model.id)) throw new Error('Catalog translation model mismatch.');
    seen.add(model.id);
    for (const key of ['mod','rarity','type','image','thumbnail','imagePath','monsters'])
      if (JSON.stringify(source[key]) !== JSON.stringify(model[key]))
        throw new Error('Catalog translation changed model structure or art.');
    if (source.kind === 'card' && (source.variants.length !== model.variants?.length
        || source.variants.some((variant, i) => variant.cost !== model.variants[i].cost
          || variant.costsX !== model.variants[i].costsX
          || variant.upgraded !== model.variants[i].upgraded
          || JSON.stringify(variant.keywords) !== JSON.stringify(model.variants[i].keywords))))
      throw new Error('Catalog translation changed card rules.');
  }
  if (!supplement.labels || typeof supplement.labels !== 'object' || Array.isArray(supplement.labels))
    throw new Error('Catalog translation has no event labels.');
  return { ...catalog, languages: { ...catalog.languages, [lang]: supplement.models },
    labels: { ...catalog.labels, [lang]: supplement.labels } };
}
