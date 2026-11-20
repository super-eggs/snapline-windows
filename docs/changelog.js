(() => {
  'use strict';

  const list = document.querySelector('.changelog-entries');
  if (!list || location.protocol === 'file:') return;
  const english = document.documentElement.lang === 'en';
  const locale = english ? 'en' : 'zh';
  const dateFormat = new Intl.DateTimeFormat(english ? 'en-US' : 'zh-CN', {
    year: 'numeric', month: english ? 'short' : 'long', day: 'numeric', timeZone: 'UTC',
  });
  const labels = english
    ? { latest: 'Latest release', installer: 'Download installer', portable: 'Download portable ZIP', release: tag => `${tag} release` }
    : { latest: '最新版本', installer: '下载安装包', portable: '下载免安装版', release: tag => `${tag} 发布页` };
  const savedNotes = new Map([...list.children].map(entry => [
    entry.querySelector('h3').textContent,
    entry.querySelector('.changelog-notes'),
  ]));

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text) node.textContent = text;
    return node;
  }

  function addNotes(content, release) {
    let items = release.notes[locale];
    if (!items.length && savedNotes.has(release.tag)) {
      content.append(savedNotes.get(release.tag).cloneNode(true));
      return;
    }
    const otherLocale = english ? 'zh' : 'en';
    const notes = element('ul', 'changelog-notes');
    if (!items.length) {
      items = release.notes[otherLocale];
      if (items.length) notes.lang = otherLocale;
      else items = release.notes.original;
    }
    for (const item of items) notes.append(element('li', '', item));
    if (notes.childElementCount) content.append(notes);
  }

  function releaseEntry(release, latest) {
    const entry = element('li', 'changelog-entry');
    const version = element('div', 'changelog-version');
    const date = element('time', '', dateFormat.format(new Date(release.date)));
    date.dateTime = release.date;
    version.append(element('h3', '', release.tag), date);
    if (latest) version.append(element('span', 'changelog-latest', labels.latest));

    const content = element('div', 'changelog-content');
    addNotes(content, release);
    const links = element('div', 'changelog-links');
    for (const [url, label, arrow] of [
      [release.installer, labels.installer, '↓'],
      [release.portable, labels.portable, '↓'],
      [release.url, labels.release(release.tag), '↗'],
    ]) {
      if (!url) continue;
      const link = element('a', '', label);
      const icon = element('span', '', arrow);
      icon.setAttribute('aria-hidden', 'true');
      link.href = url;
      link.append(icon);
      links.append(link);
    }
    content.append(links);
    entry.append(version, content);
    return entry;
  }

  async function sync() {
    try {
      const response = await fetch('/api/releases', { signal: AbortSignal.timeout(6500) });
      if (!response.ok) return;
      const releases = await response.json();
      if (!Array.isArray(releases) || !releases.length) return;
      const fresh = !response.headers.has('X-Snapline-Stale');
      const entries = releases.map((release, index) => releaseEntry(release, fresh && index === 0));
      list.replaceChildren(...entries);
    } catch {
      // The saved HTML remains usable while live releases are unavailable.
    }
  }

  sync();
})();
