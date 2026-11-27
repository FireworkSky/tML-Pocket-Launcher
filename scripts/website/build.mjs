import { copyFile, cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const [outputDirectory, releaseFile] = process.argv.slice(2);
if (!outputDirectory) {
    throw new Error('Usage: node scripts/website/build.mjs <output-directory> [releases.json]');
}

const repository = 'https://github.com/FireworkSky/tML-Pocket-Launcher';
const site = new URL('../../website/', import.meta.url);
const output = resolve(outputDirectory);
const dateFormat = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
});
const htmlEntities = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

function escapeHtml(value) {
    return value.replace(/[&<>"']/g, character => htmlEntities[character]);
}

function releaseUrl(release) {
    return `${repository}/releases/tag/${encodeURIComponent(release.tag_name)}`;
}

function releaseLabel(release) {
    return release.prerelease ? '预览版' : '正式版';
}

function renderRelease(release) {
    const date = dateFormat.format(new Date(release.published_at));
    return `
                    <article class="news-item">
                        <div class="news-meta">
                            <time datetime="${date}">${date.replaceAll('-', '.')}</time>
                            <span>${releaseLabel(release)}</span>
                        </div>
                        <div>
                            <h3>${escapeHtml(release.name?.trim() || release.tag_name)}</h3>
                            <a class="text-link" href="${releaseUrl(release)}">查看发行说明 <span aria-hidden="true">↗</span></a>
                        </div>
                    </article>`;
}

function replaceSection(html, section, content) {
    const start = `<!-- ${section}:start -->`;
    const end = `<!-- ${section}:end -->`;
    const from = html.indexOf(start);
    const to = html.indexOf(end, from + start.length);
    if (from === -1 || to === -1) {
        throw new Error(`Missing HTML markers for ${section}`);
    }
    return html.slice(0, from) + content + html.slice(to + end.length);
}

const rawReleases = releaseFile
    ? JSON.parse((await readFile(releaseFile, 'utf8')).replace(/^\uFEFF/, '')).flat()
    : [];
const releases = rawReleases
    .filter(release => !release.draft && release.published_at)
    .sort((first, second) => Date.parse(second.published_at) - Date.parse(first.published_at));

let html = await readFile(new URL('index.html', site), 'utf8');
html = replaceSection(html, 'releases', releases.slice(0, 8).map(renderRelease).join(''));

const downloadable = releases
    .map(release => ({
        release,
        apks: release.assets.filter(asset => asset.state === 'uploaded' && /\.apk$/i.test(asset.name)),
    }))
    .filter(({ apks }) => apks.length > 0);
const preferred = downloadable.find(({ release }) => !release.prerelease) ?? downloadable[0];

if (preferred) {
    const { release, apks } = preferred;
    const arm64 = apks.filter(asset => /arm64|aarch64/i.test(asset.name));
    const apk = apks.length === 1 ? apks[0] : arm64.length === 1 ? arm64[0] : undefined;
    const url = apk
        ? `${repository}/releases/download/${encodeURIComponent(release.tag_name)}/${encodeURIComponent(apk.name)}`
        : releaseUrl(release);
    const label = apk ? '下载 APK' : '选择 APK';
    const button = `<a class="button primary" href="${url}">${label} <span aria-hidden="true">${apk ? '↓' : '↗'}</span></a>`;
    const version = `${releaseLabel(release)} · ${escapeHtml(release.tag_name)}`;
    html = replaceSection(html, 'download', button);
    html = replaceSection(html, 'install', `<p>${version}</p>\n                ${button}`);
}

await mkdir(output, { recursive: true });
await cp(new URL('assets/', site), resolve(output, 'assets'), { recursive: true });
await copyFile(new URL('styles.css', site), resolve(output, 'styles.css'));
await writeFile(resolve(output, 'index.html'), html);
