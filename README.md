<div align="center">

<img src="docs/branding/tml-pocket-launcher/current/pocket-launcher-192.png" width="112" alt="tML Pocket Launcher 像素齿轮与树木图标" />

# tML Pocket Launcher

**把 tModLoader 装进口袋。**

面向 Android 的 tModLoader 启动与管理工具：导入 Terraria PC 版资源，或从已安装的 Terraria PE 转换兼容资源，选择 tML 版本、管理模组并启动游戏。

[English](README.en.md) · [安装使用](docs/getting-started.md) · [Terraria PE 支持](docs/terraria-pe.md) · [构建开发](docs/development.md) · [文档目录](docs/README.md) · [反馈问题](https://github.com/FireworkSky/tML-Pocket-Launcher/issues)

![Platform](https://img.shields.io/badge/Android-9%2B%20%7C%20arm64-3DDC84?logo=android&logoColor=white)
![Status](https://img.shields.io/badge/status-experimental-orange)
![Runtime](https://img.shields.io/badge/runtime-.NET%2010-512BD4?logo=dotnet&logoColor=white)
![UI](https://img.shields.io/badge/UI-React%20%2B%20TypeScript-3178C6?logo=typescript&logoColor=white)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-blue)](LICENSE)

</div>

> **项目处于开发阶段。** 已在 arm64 Android 真机上使用 tML `2026.09.3.1` 和 Terraria `1.4.4.9` 资源进入主菜单并加载内嵌触控补丁。其他版本、设备 GPU、完整世界游玩与多人联机仍需要单独验证。该项目为独立社区工具。

## 项目定位

tML Pocket Launcher 为桌面版 tModLoader 提供 Android 启动环境。游戏通过 SDL、FNA 原生后端和 Android CoreCLR 运行，启动器提供资源安装、账号授权、版本获取、模组管理和移动端适配。

缺少可用的 tModLoader 时，点击「准备游戏」可选择版本并下载安装。中国大陆使用 Gitee，其他地区使用 GitHub 官方源。其他 tML 版本仍可下载或导入，Terraria 原版资源单独获取。

## 功能概览

| 模块     | 当前能力                                                                   |
| -------- | -------------------------------------------------------------------------- |
| 游戏资源 | 自定义文件浏览器；导入 Steam ZIP、GOG Linux `.sh` 或转换 Terraria PE；安装校验与事务回滚 |
| tML 版本 | 下载官方发行版或导入本地发行 ZIP，支持安装多个版本并选择启动版本 |
| 游戏实例 | 为不同玩法独立保存角色、世界、模组、控件布局和启动设置，共用游戏发行文件 |
| 存档备份 | 按实例创建、恢复及导入或导出 ZIP，可开启启动前备份 |
| 下载任务 | 官方 tML 发行包支持排队、暂停与断点续传 |
| Steam    | 使用 SteamKit2 登录、Steam Guard 验证、Terraria 所有权检查与资源下载       |
| GOG      | 官方网页登录授权、资源版本选择与 Linux 安装包下载                          |
| 模组管理 | 读取游戏模组信息、搜索、启停、依赖检查、删除和批量管理；无需加载游戏    |
| 模组包   | 保存启用清单、导入／导出 JSON、应用预设与缺失依赖检查                      |
| 创意工坊 | 在“模组”浏览 tModLoader 模组，在“资源”浏览 Terraria 世界和资源包，可筛选、查看详情并下载安装 |
| Terraria PE | 从设备上已安装的 Terraria PE 提取资源，转换为 PC 版尺寸和 XNB/XACT 格式；首次转换需预留约 2 GiB 空间 |
| 触控     | 内嵌 MobileJoystick 补丁，提供双摇杆、触摸菜单、物品栏、全屏控制和界面编辑，默认使用从 Terraria PE 提取的控件纹理 |
| 补丁     | 内置触摸、全屏、平台兼容与实验性联机托管，可选 120 FPS 呈现；触控面板可显示帧率 |
| 外观     | 默认横屏、可选竖屏；主题、玻璃效果、自定义背景、动效和减少动态效果         |
| 启动诊断 | 分阶段启动动画、首帧反馈、启动日志与错误提示                               |

Steam 登录已获真机验证；**GOG 登录与完整付费资源传输尚未完成自有账号实测**。实现状态、浏览器演示和真机验证范围见 [Android 功能说明](docs/android-launcher.md)。

## 开始使用

1. 在 **Android 9 / API 28 及以上的 arm64-v8a 设备**安装发布 APK，或使用下文的源码构建步骤。[Releases](https://github.com/FireworkSky/tML-Pocket-Launcher/releases)
2. 在“游戏”页导入 Steam Terraria ZIP、GOG Linux `.sh`，或检测并转换本机安装的 Terraria PE。请使用自己拥有的游戏。
3. 点击「准备游戏」，选择 tModLoader 版本并下载安装。需要其他版本时，从官方发行列表下载，或导入包含依赖及 Content 的完整发行 ZIP。
4. 检查资源和版本，按需导入游戏模组。启动游戏时会加载内嵌触控补丁。
5. 在“设置 → 控制管理”全屏编辑布局，从悬浮菜单切换游戏控制和界面。点击控件查看属性，拖动调整位置。保存后，布局在下次启动时生效。

通过启动器从 Steam 下载并安装后即可离线启动。手动导入 Steam ZIP 时，每次启动都需要联网验证 Terraria 所有权。GOG 资源导入后启动无需登录。更详细的步骤、包结构和数据保护建议见 [安装使用指南](docs/getting-started.md)。

## 已知边界

- 可同时安装多个 tML 发行版，启动时使用所选版本。各版本共用 Terraria 资源、存档、模组和配置。
- 下载支持取消与重试，暂不支持跨进程的断点续传。
- 创意工坊下载支持匿名公开访问或复用已登录 Steam 会话；受 Steam 项目权限限制，下载能力尚待真机验证。
- Terraria PE 转换器仅包含在启用了转换配置的 arm64 APK 中，并要求选择配置指定的 tML 版本。实测 14,468 项资源转换结果约 704 MiB，实际用量随游戏版本而变化。
- 默认触控纹理由带转换器的启动器从本机 Terraria PE 提取。使用 Steam 或 GOG 资源启动时，也可以从本机安装的 Terraria PE 提取纹理。缺失的默认纹理会跳过绘制并记入日志。
- GOG 分卷安装包暂不支持。
- 120 FPS 默认关闭，实际帧率取决于设备。联机托管补丁自动加载，开房支持 tModLoader 2026.09.3.1。
- 浏览器普通模式是交互预览，包含示例数据；Android 模式使用设备真实状态。
- 构建、单元测试或进入主菜单不等于所有模组、存档、版本和联机方式均兼容。

## 从源码构建

推荐使用 **Windows + PowerShell**；当前脚本按该环境组织，其他主机环境尚未完整验证。

### 环境

| 工具        | 要求                                                           |
| ----------- | -------------------------------------------------------------- |
| .NET SDK    | 10，具体策略见 [`global.json`](global.json)                    |
| Java        | JDK 21 或兼容的 Android Studio JBR                             |
| Android SDK | Platform 37                                                    |
| Android NDK | `30.0.14904198 rc1`，见 Gradle 配置                            |
| CMake       | `3.22.1`                                                       |
| Node.js     | 22+，配合 npm                                                  |
| Python      | 3.10+，用于编译触控界面的几何绘图指令                       |
| Git         | 支持子模块；首次克隆需要 `--recurse-submodules`                |
| 编译参考    | 已安装的预编译 Terraria `1.4.4.9` / tML `2026.09.3.1` 发行目录 |

```powershell
git clone --recurse-submodules https://github.com/FireworkSky/tML-Pocket-Launcher.git tML-Pocket-Launcher
Set-Location tML-Pocket-Launcher

# 路径替换成自己的本地安装目录；只准备编译引用，不打包游戏本体。
.\scripts\build\prepare-game-references.ps1 -TmlRoot '<预编译 tModLoader 安装目录>'

# 配置 JAVA_HOME、ANDROID_HOME，或在 android/local.properties 中设置 sdk.dir。
.\scripts\checks\verify.ps1 -Android -Assemble
```

首次构建需要下载 Gradle、Maven、NuGet 和 npm 依赖。详见 [开发指南](docs/development.md)，不要把本机 SDK 路径、账号凭据或游戏资源加入仓库。

调试 APK 输出到 `android/app/build/outputs/apk/debug/app-arm64-v8a-debug.apk`。

### 单独预览界面

```powershell
Set-Location ui/launcher
npm ci
npm run dev
```

打开 `http://127.0.0.1:8769/preview.html` 可查看横竖屏手机预览。生产 APK 离线内嵌 React + TypeScript 界面，游戏渲染在独立 SDL/CoreCLR 进程中运行。

### 验证

```powershell
# 布局、命名规范与托管构建
.\scripts\checks\verify.ps1

# UI 类型检查、构建与格式检查
Push-Location ui/launcher
npm run check
npm run build
npm run format:check
Pop-Location

# APK 的必要文件与游戏本体排除检查
.\scripts\checks\check-apk.ps1 -ApkPath android/app/build/outputs/apk/debug/app-arm64-v8a-debug.apk
```

Android Studio 和命令行的 Debug、Release 构建默认包含 Terraria PE 转换器。转换时从已安装的 tModLoader `2026.09.3.1` 读取贴图清单和 tModLoader UI。

真机启动、账号资源传输和触控游玩需在设备上验证。

## 项目结构

```text
tML-Pocket-Launcher/
├── android/                  Android 应用、Gradle、JNI 与原生运行宿主
│   └── app/src/              Android Kotlin/Java 源码
├── managed/                  C# 宿主、平台兼容与 Steam/GOG 服务
│   ├── MobileLauncher.Bootstrap/、Host/Stores/ 启动器托管组件
│   ├── MobileLauncher.Controls/ 游戏触控补丁与资源
├── ui/launcher/              React + TypeScript 启动器界面
├── scripts/
│   ├── build/                启动器构建准备脚本
│   ├── checks/               结构、运行时与 APK 校验
│   └── design/               图标源文件与导出工具
├── external/                 SDL、FNA3D、FAudio、补丁源码子模块及 MonoMod 依赖
│   ├── tML-Pocket-Patches/   移动端补丁独立项目
├── docs/                     使用、架构、开发与设计文档
└── MobileLauncher.slnx       C# 解决方案入口
```

Android、C# 与 Web UI 源码分别归位；`build/`、`bin/`、`obj/` 和缓存不上传。架构与数据目录见 [架构说明](docs/architecture.md)。

## 文档与反馈

- [安装使用与备份](docs/getting-started.md)
- [开发与构建](docs/development.md)
- [架构与模块职责](docs/architecture.md)
- [Android 功能和验证边界](docs/android-launcher.md)
- [代码规范](docs/code-style.md) · [贡献指南](CONTRIBUTING.md)
- [常见问题](docs/troubleshooting.md) · [安全问题](SECURITY.md)

提交问题时请提供启动器版本、设备型号、Android 版本、GPU、tML 版本、Terraria 资源来源、复现步骤和脱敏日志。不要提交密码、验证码、令牌、完整资源包或个人存档。

## 许可证

启动器自有代码采用 GNU General Public License v3.0（`GPL-3.0-only`）。[完整许可证](LICENSE)

第三方组件适用各自的授权条款。随附的 MobileJoystick 上游源码缺少独立许可证文件，相关来源见第三方声明。Terraria 资源和第三方标志归各自权利人所有。[第三方声明](THIRD_PARTY_NOTICES.md)

## 致谢

感谢 [tModLoader](https://github.com/tModLoader/tModLoader)、[FNA](https://github.com/FNA-XNA/FNA)、Re-Logic、[RotatingArtDev](https://github.com/RotatingArtDev)、SteamKit、SDL、FNA3D、FAudio、MonoMod、MobileJoystick 作者 **yu**，以及 React、Animate UI、Inspira UI、Lucide 等依赖的贡献者。

启动器和 README 使用用户选定的像素齿轮与树木图标。当前图标原图、各尺寸导出和 Android 裁切预览见 [`docs/branding/tml-pocket-launcher/current`](docs/branding/tml-pocket-launcher/current)；其他方案保留为历史设计。
