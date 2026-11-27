# tML Pocket Launcher 官网

站点使用 GitHub Pages 托管。将 `website/` 或 `scripts/website/` 的改动推送到 `main`，GitHub Actions 会构建并部署官网。

- 发布 APK 时，在 GitHub Releases 填写标题和发行说明，然后上传 `.apk` 附件并发布。官网自动列出发行记录并链接至完整说明。有正式版 APK 时优先展示最近发布的正式版，否则展示最近发布的预览版。
- 中文和英文开发公告分别写在 `index.html` 和 `en.html` 的 `news-list` 中。GitHub Release 的标题和发行说明沿用发布时填写的语言。若在发版后单独增删附件，可从 Actions 手动运行 **Deploy launcher website** 刷新下载入口。

本地可直接打开 `index.html` 预览首页。若要带发行数据预览完整构建，可在仓库根目录使用 Node.js 和 GitHub CLI：

```powershell
gh api --paginate --slurp 'repos/FireworkSky/tML-Pocket-Launcher/releases?per_page=100' | Set-Content -Encoding utf8 "$env:TEMP/tml-launcher-releases.json"
node scripts/website/build.mjs "$env:TEMP/tml-launcher-site" "$env:TEMP/tml-launcher-releases.json"
```

构建产物的入口是临时目录下的 `tml-launcher-site/index.html`。预览图使用仓库中已公开的启动器截图，站点图标来自现有品牌素材。
