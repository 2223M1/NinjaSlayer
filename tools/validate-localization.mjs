import { readdirSync, readFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { languages as officialLanguages } from '../Infrastructure/telemetry-worker/dashboard/languages.mjs';
const root = resolve(import.meta.dirname, '..', 'NinjaSlayer', 'localization');
const errors = [];
// Optional language arguments make each translation group independently testable.
const languages = process.argv.length > 2 ? [...new Set(process.argv.slice(2))] : officialLanguages;
if (languages.some(lang => !officialLanguages.includes(lang))) throw new Error('Unknown game language');
if (process.argv.length === 2 && readdirSync(root).sort().join() !== [...officialLanguages].sort().join()) errors.push('Localization folders must match all official game languages');
const tables = {};
for (const lang of new Set(['zhs', ...languages])) {
  try {
    tables[lang] = Object.fromEntries(readdirSync(join(root, lang)).filter(f => f.endsWith('.json'))
      .map(f => [f, JSON.parse(readFileSync(join(root, lang, f), 'utf8'))]));
  } catch (error) { errors.push(lang + ': ' + error.message); tables[lang] = {}; }
}
const variables = text => [...new Set([...text.matchAll(/\{([A-Za-z_]\w*)[}:]/g)].map(m => m[1]))].sort().join(',');
const formatters = text => [...text.matchAll(/\{([A-Za-z_]\w*):(diff\(\)|energyIcons\(\)|show)/g)].map(m => m[1] + ':' + m[2]).sort().join(',');
function validateText(text, label) {
  if (typeof text !== 'string') { errors.push(label + ' is not text'); return; }
  let depth = 0;
  for (const char of text) {
    if (char === '{') depth++;
    if (char === '}' && --depth < 0) errors.push(label + ' has an unmatched }');
  }
  if (depth) errors.push(label + ' has unbalanced format braces');
  const stack = [];
  for (const m of text.matchAll(/\[(\/?)([a-z_]+)(?:=[^\]]+)?\]/g)) {
    const [, close, tag] = m;
    if (['energy', 'star'].includes(tag) || tag.endsWith('_icon')) continue;
    if (!close) stack.push(tag);
    else if (stack.pop() !== tag) errors.push(label + ' has mismatched rich-text tags');
  }
  if (stack.length) errors.push(label + ' has unclosed rich-text tags');
  if (/__NS\d+__|ZXROW\d+XZ|⟦\d+⟧|941\d{3}/.test(text)) errors.push(label + ' contains a discarded translation placeholder');
}
for (const lang of languages) if (Object.keys(tables[lang]).sort().join() !== Object.keys(tables.zhs).sort().join()) errors.push(lang + ' table inventory mismatch');
for (const table of Object.keys(tables.zhs)) {
  const base = tables.zhs[table];
  for (const language of languages) {
    const current = tables[language][table];
    if (!current) { errors.push(language + '/' + table + ' missing'); continue; }
    for (const key of new Set([...Object.keys(base), ...Object.keys(current)])) {
      const label = language + '/' + table + ':' + key;
      if (!(key in base) || !(key in current)) { errors.push(label + ' key mismatch'); continue; }
      validateText(current[key], label);
      if (variables(base[key]) !== variables(current[key])) errors.push(label + ' variable mismatch');
      // New non-Chinese translations follow the approved English Repeat highlight.
      const expectedFormats = language !== 'zhs' && language !== 'jpn' && language !== 'zht' && key === 'NINJA_SLAYER_CARD_DRAGON_ROUNDHOUSE_KICK.description'
        ? 'Damage:diff(),Repeat:diff()' : formatters(base[key]);
      if (expectedFormats !== formatters(current[key])) errors.push(label + ' formatter mismatch');
      if ((base[key] === '') !== (current[key] === '')) errors.push(label + ' intentional-empty mismatch');
      if (key.endsWith('-attack') && current[key] !== base[key]) errors.push(label + ' control value translated');
    }
  }
}
for (const lang of languages) {
  const keys = Object.keys(tables[lang]['ancients.json'] ?? {});
  for (const key of keys.filter(k => k.endsWith('.ancient'))) {
    if (keys.includes(key.replace(/\.ancient$/, '.char'))) errors.push(lang + ':' + key + ' conflicting speakers');
  }
}
if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
else console.log('Localization checks passed: ' + languages.length + ' languages, ' + Object.keys(tables.zhs).length + ' tables, ' +
  Object.values(tables.zhs).reduce((n, table) => n + Object.keys(table).length, 0) + ' keys per language.');
