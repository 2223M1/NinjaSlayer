import { messages } from './translations.mjs';
import { siteCopy } from './site-copy.mjs';
import { languages, locales } from './languages.mjs';
import { additionalMessages } from './translations-extra.mjs';

const inBrowser = typeof document !== 'undefined';
const isAdmin = inBrowser && document.body?.dataset.view === 'admin';
let preferred;
if (inBrowser) {
  preferred = new URLSearchParams(location.search).get('lang');
  if (!preferred) {
    try { preferred = localStorage.getItem('ninjaslayer-language'); } catch { /* Storage can be disabled. */ }
  }
}
export const language = !isAdmin && languages.includes(preferred) ? preferred : 'zhs';
export const locale = locales[language][0];
export function t(key, ...values) {
  const text = (isAdmin ? undefined : siteCopy[key]?.[language]) ?? (language === 'zhs' ? key
    : language === 'eng' ? messages[key]?.[0] ?? key
    : language === 'jpn' ? messages[key]?.[1] ?? key
    : additionalMessages[language]?.[key] ?? key);
  return String(text).replace(/\{(\d+)\}/g, (token, index) => values[index] === undefined ? token : String(values[index]));
}
export function tr(parts, ...values) {
  return t(parts.map((part, i) => part + (i < values.length ? '{' + i + '}' : '')).join(''), ...values);
}
// Translate only the static shell, before any player-submitted text is rendered.
export function localizePage() {
  if (!inBrowser) return;
  if (isAdmin) {
    document.querySelector('#language').hidden = true;
    return;
  }
  document.documentElement.lang = locale;
  for (const option of document.querySelectorAll('option:not([value])')) option.value = option.textContent;
  const walker = document.createTreeWalker(document.documentElement, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (['SCRIPT', 'STYLE'].includes(node.parentElement?.tagName)) continue;
    const key = node.textContent.trim();
    if (messages[key]) node.textContent = node.textContent.replace(key, t(key));
  }
  for (const node of document.querySelectorAll('[aria-label], [placeholder], [alt]')) {
    for (const attr of ['aria-label', 'placeholder', 'alt']) if (node.hasAttribute(attr)) node.setAttribute(attr, t(node.getAttribute(attr)));
  }
  const select = document.querySelector('#language');
  select.replaceChildren(...languages.map(code => new Option(locales[code][1], code)));
  select.value = language;
  select.addEventListener('change', () => {
    const url = new URL(location.href);
    url.searchParams.set('lang', select.value);
    try { localStorage.setItem('ninjaslayer-language', select.value); } catch { /* URL remains authoritative. */ }
    location.assign(url);
  });
}
