const repository = 'super-eggs/snapline-windows';
const releasePage = `https://github.com/${repository}/releases`;
const githubApi = `https://api.github.com/repos/${repository}/releases?per_page=100`;
const cacheLifetimeSeconds = 86400;
const refreshAfterMs = 5 * 60 * 1000;

function releaseNotes(body) {
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
  return notes;
}

function downloadAsset(release, packageType, extension) {
  const basename = `Snapline-${packageType}`;
  const version = release.tag_name.slice(1);
  const names = [`${basename}-${version}-x64.${extension}`, `${basename}-x64.${extension}`];
  for (const name of names) {
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

function websiteReleases(releases) {
  return releases
    .filter(release => !release.draft && !release.prerelease &&
      /^v\d+\.\d+\.\d+$/.test(release.tag_name) && Number.isFinite(Date.parse(release.published_at)))
    .map(release => ({
      tag: release.tag_name,
      date: new Date(release.published_at).toISOString().slice(0, 10),
      notes: releaseNotes(release.body),
      url: `${releasePage}/tag/${release.tag_name}`,
      installer: downloadAsset(release, 'Setup', 'exe'),
      portable: downloadAsset(release, 'Portable', 'zip'),
    }))
    .sort(compareVersions);
}

function clientResponse(response, stale = false) {
  const headers = new Headers(response.headers);
  headers.set('Cache-Control', stale ? 'no-store' : 'public, max-age=60');
  if (stale) headers.set('X-Snapline-Stale', '1');
  return new Response(response.body, { headers });
}

export async function onRequestGet(context) {
  const key = new URL('/api/releases', context.request.url);
  const cached = await caches.default.match(key.href);
  if (cached && Date.now() - Number(cached.headers.get('X-Snapline-Fetched-At')) < refreshAfterMs) {
    return clientResponse(cached);
  }

  try {
    const github = await fetch(githubApi, {
      headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'Snapline-Website' },
      signal: AbortSignal.timeout(4500),
    });
    if (!github.ok) throw new Error(`GitHub returned ${github.status}`);
    const releases = websiteReleases(await github.json());
    if (!releases.length) throw new Error('No published stable releases');
    const response = Response.json(releases, {
      headers: {
        'Cache-Control': `public, max-age=${cacheLifetimeSeconds}`,
        'X-Snapline-Fetched-At': String(Date.now()),
      },
    });
    context.waitUntil(caches.default.put(key.href, response.clone()).catch(() => {}));
    return clientResponse(response);
  } catch {
    if (cached) return clientResponse(cached, true);
    return Response.json({ error: 'Release information is temporarily unavailable.' }, {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  }
}
