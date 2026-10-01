import { messages } from './translations.mjs';

const inBrowser = typeof document !== 'undefined';
const isAdmin = inBrowser && document.body?.dataset.view === 'admin';
const supported = ['zhs', 'eng', 'jpn'];
let preferred;
if (inBrowser) {
  preferred = new URLSearchParams(location.search).get('lang');
  if (!preferred) {
    try { preferred = localStorage.getItem('ninjaslayer-language'); } catch { /* Storage can be disabled. */ }
  }
}
export const language = !isAdmin && supported.includes(preferred) ? preferred : 'zhs';
export const locale = { zhs: 'zh-CN', eng: 'en-US', jpn: 'ja-JP' }[language];
export function t(key, ...values) {
  const text = language === 'zhs' ? key : messages[key]?.[language === 'eng' ? 0 : 1] ?? key;
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
  select.value = language;
  select.addEventListener('change', () => {
    const url = new URL(location.href);
    url.searchParams.set('lang', select.value);
    try { localStorage.setItem('ninjaslayer-language', select.value); } catch { /* URL remains authoritative. */ }
    location.assign(url);
  });
}
