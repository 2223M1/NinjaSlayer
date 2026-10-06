import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { messages } from '../Infrastructure/telemetry-worker/dashboard/translations.mjs';
import { languages } from '../Infrastructure/telemetry-worker/dashboard/languages.mjs';
const root = resolve(import.meta.dirname, '..');
const slots = text => (text.match(/\{\d+\}/g) ?? []).sort().join();
const additionalMessages = {};
for (const lang of languages.filter(lang => !['zhs','eng','jpn'].includes(lang))) {
  const texts = JSON.parse(readFileSync(join(root, 'Website/i18n', lang + '.json'), 'utf8'));
  if (Object.keys(texts).sort().join() !== Object.keys(messages).sort().join()) throw new Error(lang + ': website key inventory mismatch');
  for (const [key, text] of Object.entries(texts)) {
    if (typeof text !== 'string' || !text.trim() || slots(text) !== slots(key)
        || /__NS\d+__|ZXROW\d+XZ|⟦\d+⟧|941\d{3}|[<>\u0000-\u0008]/.test(text))
      throw new Error(lang + ': invalid website text or placeholder: ' + key);
  }
  additionalMessages[lang] = texts;
}
const output = join(root, 'Infrastructure/telemetry-worker/dashboard/translations-extra.mjs');
const contents = '// Generated from Website/i18n/*.json by tools/sync-website-localization.mjs.\nexport const additionalMessages = ' + JSON.stringify(additionalMessages, null, 2) + ';\n';
if (process.argv.includes('--check')) {
  if (readFileSync(output, 'utf8') !== contents) throw new Error('Website translation module is stale');
} else writeFileSync(output, contents);
console.log(`Website localization checks passed: ${languages.length} languages, ${Object.keys(messages).length} messages.`);
