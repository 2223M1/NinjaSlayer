// Offline conversion of the author's approved Chinese copy, followed by host terminology.
// npm install --prefix build/localization-tools --no-save --ignore-scripts opencc-js@1.4.2
import { readFileSync, writeFileSync, readdirSync, mkdirSync } from 'node:fs';
import OpenCC from '../../build/localization-tools/node_modules/opencc-js/dist/esm/full.js';
import { messages } from '../../Infrastructure/telemetry-worker/dashboard/translations.mjs';
const convert = OpenCC.Converter({ from: 'cn', to: 'twp' });
const reference = process.argv[2] ?? '../Slay the Spire 2/Slay the Spire 2 v0.111.0/localization';
const glossary = new Map();
for (const table of ['powers','card_keywords','static_hover_tips','gameplay_ui']) {
  const read = lang => JSON.parse(readFileSync(`${reference}/${lang}/${table}.json`, 'utf8'));
  const cn = read('zhs'), tw = read('zht');
  for (const [key, text] of Object.entries(cn)) if ((key.endsWith('.title') || key.startsWith('CARD_TYPE.')) && tw[key] && !text.includes('{') && /[\u3400-\u9fff]/.test(text)) glossary.set(convert(text), tw[key]);
}
function localize(text) {
  let result = convert(text);
  for (const [from, to] of [...glossary].sort((a,b) => b[0].length-a[0].length)) result = result.replaceAll(from, to);
  return result.replaceAll('澤渡', '沢渡').replaceAll('手裡劍', '手裏劍')
    .replaceAll('反饋', '回饋').replaceAll('設置', '設定').replaceAll('默認', '預設')
    .replaceAll('回覆', '恢復').replaceAll('訂單隻', '訂單只').replaceAll('賬號', '帳號')
    .replaceAll('Mother UNIX訪問密鑰', 'Mother UNIX存取金鑰');
}
mkdirSync('NinjaSlayer/localization/zht', { recursive: true });
for (const table of readdirSync('NinjaSlayer/localization/zhs').filter(f => f.endsWith('.json'))) {
  const original = JSON.parse(readFileSync(`NinjaSlayer/localization/zhs/${table}`, 'utf8'));
  const result = Object.fromEntries(Object.entries(original).map(([key, value]) => [key, key.endsWith('-attack') ? value : localize(value)]));
  writeFileSync(`NinjaSlayer/localization/zht/${table}`, JSON.stringify(result, null, 2) + '\n');
}
mkdirSync('Website/i18n', { recursive: true });
writeFileSync('Website/i18n/zht.json', JSON.stringify(Object.fromEntries(Object.keys(messages).map(key => [key, localize(key)])), null, 2) + '\n');
console.log('Traditional Chinese: approved Chinese source retained; host terminology applied.');
