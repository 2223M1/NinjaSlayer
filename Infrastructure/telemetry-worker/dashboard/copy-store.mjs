import { readFile, writeFile, mkdir, rename } from 'node:fs/promises';
import { dirname } from 'node:path';
import { messages } from './translations.mjs';
import { additionalMessages } from './translations-extra.mjs';
import { languages } from './languages.mjs';
export { languages } from './languages.mjs';

export const committedCopyPath = new URL('../../../Website/site-copy.json', import.meta.url);
export const draftCopyPath = new URL('../../../build/dashboard/site-copy-draft.json', import.meta.url);
export const copyDefaults = Object.fromEntries(Object.entries(messages).map(([key, value]) =>
  [key, { zhs: key, eng: value[0], jpn: value[1], ...Object.fromEntries(
    languages.filter(lang => !['zhs', 'eng', 'jpn'].includes(lang)).map(lang => [lang, additionalMessages[lang][key]])) }]));

export function validateCopy(value) {
  if (value?.schemaVersion !== 1 || !value.overrides || Array.isArray(value.overrides)
      || typeof value.overrides !== 'object' || Object.keys(value).some(k => !['schemaVersion', 'overrides'].includes(k)))
    throw new Error('文案文件格式不正确。');
  const overrides = {};
  for (const [key, texts] of Object.entries(value.overrides)) {
    if (!Object.hasOwn(copyDefaults, key) || !texts || typeof texts !== 'object' || Array.isArray(texts)
        || Object.keys(texts).sort().join() !== [...languages].sort().join())
      throw new Error('文案包含未知条目或缺少受支持语言的内容。');
    const placeholders = text => (text.match(/\{\d+\}/g) ?? []).sort().join();
    for (const lang of languages) {
      if (typeof texts[lang] !== 'string' || !texts[lang].trim() || texts[lang].length > 5000
          || /[<>\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(texts[lang]))
        throw new Error('请输入 1–5000 字的纯文本，不支持 HTML。');
      if (placeholders(texts[lang]) !== placeholders(key)) throw new Error('必须保留原文的 {0} 等动态占位符。');
    }
    overrides[key] = Object.fromEntries(languages.map(lang => [lang, texts[lang]]));
  }
  return { schemaVersion: 1, overrides };
}

export async function readCopy(path = committedCopyPath) {
  return validateCopy(JSON.parse(await readFile(path, 'utf8')));
}
export async function readDraft(path = draftCopyPath) {
  try { return await readCopy(path); }
  catch (error) { if (error.code !== 'ENOENT') throw error; return readCopy(); }
}
export async function saveCopy(path, value) {
  const validated = validateCopy(value);
  const file = path instanceof URL ? (await import('node:url')).fileURLToPath(path) : path;
  await mkdir(dirname(file), { recursive: true });
  await writeFile(file + '.tmp', JSON.stringify(validated, null, 2) + '\n', { mode: 0o600 });
  await rename(file + '.tmp', file);
  return validated;
}
export function copyModule(copy) {
  return `export const siteCopy = ${JSON.stringify(validateCopy(copy).overrides)};\n`;
}
