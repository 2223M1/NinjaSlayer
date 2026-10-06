import test from 'node:test';
import assert from 'node:assert/strict';
import { messages } from '../dashboard/translations.mjs';
import { additionalMessages } from '../dashboard/translations-extra.mjs';
import { languages, locales } from '../dashboard/languages.mjs';

const placeholders = text => [...new Set([...text.matchAll(/\{(\d+)\}/g)].map(match => match[1]))].sort();

test('website translations preserve every interpolated value', () => {
  for (const [key, pair] of Object.entries(messages)) {
    assert.equal(pair.length, 2, key);
    for (const text of pair) {
      assert.equal(typeof text, 'string', key);
      assert.ok(text.trim(), key);
      assert.deepEqual(placeholders(text), placeholders(key), key);
    }
  }
});

test('every official language has complete website text with identical placeholders', () => {
  assert.equal(languages.length, 16);
  for (const lang of languages.filter(code => !['zhs','eng','jpn'].includes(code))) {
    assert.deepEqual(Object.keys(additionalMessages[lang]).sort(), Object.keys(messages).sort(), lang);
    for (const [key, text] of Object.entries(additionalMessages[lang])) {
      assert.equal(typeof text, 'string', `${lang}/${key}`);
      assert.ok(text.trim(), `${lang}/${key}`);
      assert.deepEqual(placeholders(text), placeholders(key), `${lang}/${key}`);
      assert.doesNotMatch(text, /__NS\d+__|ZXROW\d+XZ/, `${lang}/${key}`);
    }
  }
});

test('website language uses URL, then saved choice; admin remains Chinese', async () => {
  const originals = Object.fromEntries(['document', 'location', 'localStorage'].map(key => [key, globalThis[key]]));
  try {
    const cases = [
      ['?lang=jpn', 'eng', 'pages', 'jpn', 'ja-JP'],
      ['', 'eng', 'pages', 'eng', 'en-US'],
      ['?lang=invalid', 'eng', 'pages', 'zhs', 'zh-CN'],
      ['?lang=eng', 'eng', 'admin', 'zhs', 'zh-CN'],
      ['', null, 'pages', 'zhs', 'zh-CN'],
      ...languages.map(code => [`?lang=${code}`, 'zhs', 'pages', code, locales[code][0]]),
    ];
    for (const [index, [search, saved, view, expected, locale]] of cases.entries()) {
      globalThis.document = { body: { dataset: { view } } };
      globalThis.location = { search };
      globalThis.localStorage = { getItem: () => saved };
      const module = await import('../dashboard/i18n.mjs?test=' + index);
      assert.equal(module.language, expected);
      assert.equal(module.locale, locale);
      assert.equal(module.t('untranslated player feedback'), 'untranslated player feedback');
      assert.equal(module.t('{0}', '<player text>'), '<player text>');
      if (expected === 'eng') assert.equal(module.t('手牌'), 'Hand');
    }
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key];
      else globalThis[key] = value;
    }
  }
});
