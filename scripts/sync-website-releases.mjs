import { readFile, writeFile } from 'node:fs/promises';

const releasePage = 'https://github.com/super-eggs/snapline-windows/releases';
const start = '        <!-- releases:start -->';
const end = '        <!-- releases:end -->';
const visibleReleaseCount = 5;
const historicalNotes = {
  'v1.0.0': {
    zh: ['首个 Windows 版本，支持剪贴板图片收集、晾衣绳浏览、单击复制、图片预览和画笔标注。'],
    en: ['The first Windows release included clipboard collection, shelf browsing, click-to-copy, image previews, and pen annotations.'],
  },
  'v1.0.1': {
    en: ['Added the Snapline clothesline icon to the app, preview window, system tray, shortcuts, and installer.'],
  },
  'v1.0.2': {
    en: [
      'Snapline retries copying when the clipboard is busy. If copying still fails, it confirms the screenshot was saved so you can copy it from the shelf.',
      'The tray menu shows the version you’re running.',
      'Installer and portable ZIP filenames include the version number.',
    ],
  },
};

const labels = {
  zh: {
    latest: '最新版本', installer: '下载安装包', portable: '下载免安装版',
    release: tag => `${tag} 发布页`, older: count => `查看更早的更新（${count} 条）`,
  },
  en: {
    latest: 'Latest release', installer: 'Download installer', portable: 'Download portable ZIP',
    release: tag => `${tag} release`, older: count => `View ${count} earlier release${count === 1 ? '' : 's'}`,
  },
};

function releaseNotes(body, tag) {
  const notes = { zh: [], en: [], original: [] };
  let section = 'original';
  for (const line of (body || '').split(/\r?\n/)) {
    const heading = line.match(/^#{1,6}\s+(.+?)\s*#*\s*$/)?.[1];
    if (heading) {
      if (heading === '中文更新' || /^v?\d+\.\d+\.\d+\s+更新$/.test(heading)) section = 'zh';
      else if (heading === 'English updates') section = 'en';
      else if (/^(下载与安装|主要功能|downloads?( and installation)?|installation|assets)$/i.test(heading)) section = null;
      else section = 'original';
      continue;
    }
    if (!section || /^\s*(?:`{3,}|~{3,}|---+|<!--)/.test(line)) continue;
    const item = line.trim()
      .replace(/^(?:[-*+]|\d+\.)\s+/, '')
      .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
      .replace(/`([^`]+)`|\*\*([^*]+)\*\*/g, (_, code, bold) => code ?? bold);
    if (item) notes[section].push(item);
  }
  for (const locale of ['zh', 'en']) {
    if (!notes[locale].length && historicalNotes[tag]?.[locale]) notes[locale] = historicalNotes[tag][locale];
  }
  return notes;
}

function downloadAsset(release, packageType, extension) {
  const base = `Snapline-${packageType}`;
  const version = release.tag_name.slice(1);
  for (const name of [`${base}-${version}-x64.${extension}`, `${base}-x64.${extension}`]) {
    const url = `${releasePage}/download/${release.tag_name}/${name}`;
    if (release.assets?.some(asset => asset.name === name && asset.state === 'uploaded' && asset.browser_download_url === url)) return url;
  }
  return null;
}

function compareVersions(first, second) {
  const [majorA, minorA, patchA] = first.tag.slice(1).split('.').map(Number);
  const [majorB, minorB, patchB] = second.tag.slice(1).split('.').map(Number);
  return majorB - majorA || minorB - minorA || patchB - patchA;
}

function escapeHtml(text) {
  const entities = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  return text.replace(/[&<>"']/g, character => entities[character]);
}

function releaseItem(release, locale, latest) {
  const label = labels[locale];
  const date = new Intl.DateTimeFormat(locale === 'en' ? 'en-US' : 'zh-CN', {
    year: 'numeric', month: locale === 'en' ? 'short' : 'long', day: 'numeric', timeZone: 'UTC',
  }).format(new Date(release.date));
  const other = locale === 'en' ? 'zh' : 'en';
  const language = release.notes[locale].length ? locale : release.notes[other].length ? other : 'original';
  const lang = language === other ? ` lang="${other === 'zh' ? 'zh-CN' : 'en'}"` : '';
  const lines = [
    '        <li class="changelog-entry">',
    '          <div class="changelog-version">',
    `            <h3>${release.tag}</h3><time datetime="${release.date}">${escapeHtml(date)}</time>`,
  ];
  if (latest) lines.push(`            <span class="changelog-latest">${label.latest}</span>`);
  lines.push('          </div>', '          <div class="changelog-content">');
  if (release.notes[language].length) {
    lines.push(`            <ul class="changelog-notes"${lang}>`);
    for (const note of release.notes[language]) lines.push(`              <li>${escapeHtml(note)}</li>`);
    lines.push('            </ul>');
  }
  lines.push('            <div class="changelog-links">');
  for (const [url, title, icon] of [
    [release.installer, label.installer, '↓'],
    [release.portable, label.portable, '↓'],
    [release.url, label.release(release.tag), '↗'],
  ]) {
    if (url) lines.push(`              <a href="${escapeHtml(url)}">${escapeHtml(title)} <span aria-hidden="true">${icon}</span></a>`);
  }
  lines.push('            </div>', '          </div>', '        </li>');
  return lines;
}

function releaseHistory(releases, locale) {
  const recent = releases.slice(0, visibleReleaseCount);
  const older = releases.slice(visibleReleaseCount);
  const lines = [
    '        <ol class="changelog-entries" role="list">',
    ...recent.flatMap((release, index) => releaseItem(release, locale, index === 0).map(line => '  ' + line)),
    '        </ol>',
  ];
  if (older.length) {
    lines.push(
      '        <details class="changelog-older">',
      `          <summary>${labels[locale].older(older.length)}</summary>`,
      `          <ol class="changelog-entries" role="list" start="${visibleReleaseCount + 1}">`,
      ...older.flatMap(release => releaseItem(release, locale, false).map(line => '    ' + line)),
      '          </ol>',
      '        </details>',
    );
  }
  return lines;
}

const [, , releaseFile, expectedTag] = process.argv;
const input = JSON.parse(await readFile(releaseFile, 'utf8'));
const releases = input.flat()
  .filter(release => release.draft === false && release.prerelease === false &&
    /^v\d+\.\d+\.\d+$/.test(release.tag_name) && Number.isFinite(Date.parse(release.published_at)))
  .map(release => ({
    tag: release.tag_name,
    date: new Date(release.published_at).toISOString().slice(0, 10),
    notes: releaseNotes(release.body, release.tag_name),
    url: `${releasePage}/tag/${release.tag_name}`,
    installer: downloadAsset(release, 'Setup', 'exe'),
    portable: downloadAsset(release, 'Portable', 'zip'),
  }))
  .sort(compareVersions);

if (!releases[0]?.installer || !releases[0]?.portable) throw new Error('The latest stable release is missing its published downloads.');
if (expectedTag && !releases.some(release => release.tag === expectedTag && release.installer && release.portable)) {
  throw new Error(`The published release ${expectedTag} and both downloads must be available before the website is updated.`);
}

const pages = await Promise.all([['zh', 'docs/index.html'], ['en', 'docs/en/index.html']].map(async ([locale, file]) => {
  const page = await readFile(file, 'utf8');
  const newline = page.includes('\r\n') ? '\r\n' : '\n';
  const startAt = page.indexOf(start);
  const endAt = page.indexOf(end);
  if (startAt < 0 || endAt <= startAt || page.lastIndexOf(start) !== startAt || page.lastIndexOf(end) !== endAt) {
    throw new Error(`Missing or duplicate changelog markers in ${file}`);
  }
  const history = releaseHistory(releases, locale).join(newline);
  return [file, page.slice(0, startAt + start.length) + newline + history + newline + page.slice(endAt)];
}));

await Promise.all([...pages, ['docs/releases.json', JSON.stringify(releases, null, 2) + '\n']]
  .map(([file, content]) => writeFile(file, content)));
console.log(`Generated ${releases.length} published releases. Latest: ${releases[0].tag}`);
