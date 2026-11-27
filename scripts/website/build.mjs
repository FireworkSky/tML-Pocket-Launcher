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

const editions = [
    {
        file: 'index.html', stable: '正式版', preview: '预览版', notes: '查看发行说明',
        download: '下载 APK', choose: '选择 APK', date: iso => iso.replaceAll('-', '.'),
    },
    {
        file: 'en.html', stable: 'Stable release', preview: 'Prerelease', notes: 'Release notes',
        download: 'Download APK', choose: 'Choose an APK',
        date: iso => new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(iso)),
    },
];

function renderRelease(release, edition) {
    const date = dateFormat.format(new Date(release.published_at));
    return `
                    <article class="news-item">
                        <div class="news-meta">
                            <time datetime="${date}">${edition.date(date)}</time>
                            <span>${release.prerelease ? edition.preview : edition.stable}</span>
                        </div>
                        <div>
                            <h3>${escapeHtml(release.name?.trim() || release.tag_name)}</h3>
                            <a class="text-link" href="${releaseUrl(release)}">${edition.notes} <span aria-hidden="true">↗</span></a>
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

const downloadable = releases
    .map(release => ({
        release,
        apks: release.assets.filter(asset => asset.state === 'uploaded' && /\.apk$/i.test(asset.name)),
    }))
    .filter(({ apks }) => apks.length > 0);
const preferred = downloadable.find(({ release }) => !release.prerelease) ?? downloadable[0];

function addDownload(html, edition) {
    const { release, apks } = preferred;
    const arm64 = apks.filter(asset => /arm64|aarch64/i.test(asset.name));
    const apk = apks.length === 1 ? apks[0] : arm64.length === 1 ? arm64[0] : undefined;
    const url = apk
        ? `${repository}/releases/download/${encodeURIComponent(release.tag_name)}/${encodeURIComponent(apk.name)}`
        : releaseUrl(release);
    const label = apk ? edition.download : edition.choose;
    const button = `<a class="button primary" href="${url}">${label} <span aria-hidden="true">${apk ? '↓' : '↗'}</span></a>`;
    const version = `${release.prerelease ? edition.preview : edition.stable} · ${escapeHtml(release.tag_name)}`;
    html = replaceSection(html, 'download', button);
    return replaceSection(html, 'install', `<p>${version}</p>\n                ${button}`);
}

await mkdir(output, { recursive: true });
await cp(new URL('assets/', site), resolve(output, 'assets'), { recursive: true });
await copyFile(new URL('styles.css', site), resolve(output, 'styles.css'));
await copyFile(new URL('language.js', site), resolve(output, 'language.js'));
for (const edition of editions) {
    const template = await readFile(new URL(edition.file, site), 'utf8');
    const news = releases.slice(0, 8).map(release => renderRelease(release, edition)).join('');
    const html = replaceSection(template, 'releases', news);
    await writeFile(resolve(output, edition.file), preferred ? addDownload(html, edition) : html);
}
