import { useRef, useState } from 'react';
import {
  Badge,
  Button,
  ContextNote,
  Icon,
  SettingRow,
  Switch,
  WorkspaceTabs,
} from '../components/ui';
import type { TabItem } from '../components/ui';
import { Select, Slider } from '../components/form-controls';
import { DemoControl } from '../components/demo-control';
import { useWorkspaceActions } from './home';
import type { SettingsTab } from '../domain/types';
import { projectCommunity, projectUrl } from '../domain/project';
import { prepareBackground } from '../native/background';
import { nativeRequest } from '../native/bridge';
import { isLanguagePreference, useTranslation } from '../i18n';
import { hexToRgb, rgbToHex, tintGradient, tintInk, type TintRGB } from '../domain/background-tint';
import { useNativeFileBrowser } from './native/files';
import { LauncherNoticeContent } from './launcher-notice';
import {
  NativeRuntimeSettings,
  NativeControlsSettings,
  NativeLayoutManagement,
  NativeStorageSettings,
} from './native/settings';

const settingsTabs: TabItem<SettingsTab>[] = [
  {
    value: 'appearance',
    label: '外观',
    id: 'settings-tab-appearance',
    content: <Appearance />,
  },
  {
    value: 'runtime',
    label: '游戏与运行',
    id: 'settings-tab-runtime',
    content: <Runtime />,
  },
  {
    value: 'controls',
    label: '触控与输入',
    id: 'settings-tab-controls',
    content: <Controls />,
  },
  {
    value: 'storage',
    label: '存储与下载',
    id: 'settings-tab-storage',
    content: <Storage />,
  },
  {
    value: 'about',
    label: '关于',
    id: 'settings-tab-about',
    content: <About />,
  },
];

export function SettingsPage() {
  const { t } = useTranslation();
  const { state, update, nativeMode } = useWorkspaceActions();
  const items: TabItem<SettingsTab>[] = nativeMode
    ? [
        ...settingsTabs.slice(0, 3),
        {
          value: 'control-management',
          label: '控制管理',
          id: 'settings-tab-control-management',
          content: <NativeLayoutManagement />,
        },
        ...settingsTabs.slice(3),
      ]
    : settingsTabs;
  return (
    <WorkspaceTabs
      value={state.settingsTab}
      onChange={(settingsTab) => update((current) => ({ ...current, settingsTab }))}
      label={t('设置分类')}
      items={items.map((item) => ({ ...item, label: t(item.label) }))}
    />
  );
}
function Appearance() {
  const { t } = useTranslation();
  const {
    state,
    setPreferences,
    customize,
    activity,
    notify,
    nativeMode,
    openDialog,
    closeDialog,
  } = useWorkspaceActions();
  const prefs = state.preferences;
  const file = useRef<HTMLInputElement>(null);
  const { browse } = useNativeFileBrowser();
  const selectImage = () => {
    if (!nativeMode) {
      file.current?.click();
      return;
    }
    browse({
      kind: 'image',
      choose: async (files) => {
        const image = await nativeRequest<{ data: string }>('files.image', files[0]);
        return prepareBackground(image.data);
      },
      complete: (image) => {
        setPreferences({ background: 'custom', customImage: image as string });
        notify(t('背景已保存。'));
      },
    });
  };
  const openTint = () =>
    openDialog({
      title: t('自定义色调'),
      description: t('调整红、绿、蓝通道，确认后应用到启动器背景。'),
      body: (
        <TintPalette
          value={prefs.backgroundTint}
          onCancel={closeDialog}
          onApply={(tint) => {
            setPreferences({ background: 'diy', backgroundTint: tint });
            closeDialog();
          }}
        />
      ),
    });
  return (
    <>
      <section className="settings-section">
        <h2>{t('界面外观')}</h2>
        <div className="panel">
          <SettingRow title={t('界面语言')} description={t('选择启动器的显示语言。')}>
            <Select
              label={t('界面语言')}
              action="frontend-language"
              options={[
                ['system', t('跟随系统')],
                ['zh-CN', '简体中文'],
                ['en', 'English'],
              ]}
              value={prefs.language}
              onChange={(language) => {
                if (isLanguagePreference(language)) setPreferences({ language });
              }}
            />
          </SettingRow>
          <SettingRow title={t('主题模式')} description={t('选择浅色、深色或跟随系统。')}>
            <div className="theme-options">
              {(
                [
                  ['light', '浅色', 'sun'],
                  ['dark', '深色', 'moon'],
                  ['system', '跟随系统', 'settings'],
                ] as const
              ).map(([theme, label, symbol]) => (
                <Button
                  key={theme}
                  className={`theme-choice ${prefs.theme === theme ? 'active' : ''}`}
                  aria-pressed={prefs.theme === theme}
                  data-theme-choice={theme}
                  onClick={() => setPreferences({ theme })}
                >
                  <Icon name={symbol} />
                  {t(label)}
                </Button>
              ))}
            </div>
          </SettingRow>
          <SettingRow title={t('主题样式')} description={t('选择启动器使用的主题视觉样式。')}>
            <Select
              label={t('主题样式')}
              action="frontend-style"
              options={[
                ['tml', t('泰拉风')],
                ['default', t('玻璃风')],
              ]}
              value={prefs.themeStyle}
              onChange={(themeStyle) => {
                if (themeStyle === 'default' || themeStyle === 'tml')
                  setPreferences({ themeStyle });
              }}
            />
          </SettingRow>
          <SettingRow title={t('启动器方向')} description={t('默认横屏，切换后保留当前页面。')}>
            <Select
              label={t('启动器方向')}
              options={[
                ['landscape', t('横屏')],
                ['portrait', t('竖屏')],
              ]}
              value={prefs.orientation}
              onChange={(orientation) => {
                if (orientation === 'landscape' || orientation === 'portrait')
                  setPreferences({ orientation });
              }}
            />
          </SettingRow>
          <SettingRow title={t('界面动效')} description={t('减少动态效果，保留操作反馈。')}>
            <Switch
              label={t('启用界面动效')}
              action="motion"
              checked={prefs.motion}
              onChange={(motion) => setPreferences({ motion })}
            />
          </SettingRow>
        </div>
      </section>
      <section className="settings-section">
        <h2>{t('自定义背景')}</h2>
        <div className="panel">
          <SettingRow title={t('背景样式')} description={t('纯色、自定义色调或本地图片。')}>
            {prefs.background === 'custom' ? (
              <Button
                variant="ghost"
                data-action="remove-background"
                onClick={() => setPreferences({ background: 'none', customImage: undefined })}
              >
                {t('移除图片')}
              </Button>
            ) : (
              <Badge>{t('可选设置')}</Badge>
            )}
          </SettingRow>
          <div className="background-options">
            {(
              [
                ['none', '纯色'],
                ['diy', '自定色'],
              ] as const
            ).map(([background, label]) => (
              <Button
                key={background}
                className={`background-option ${background === 'diy' ? 'diy-bg' : ''} ${prefs.background === background ? 'active' : ''}`}
                data-background={background}
                style={
                  background === 'diy'
                    ? {
                        background: tintGradient(prefs.backgroundTint),
                        color: tintInk(prefs.backgroundTint),
                      }
                    : undefined
                }
                onClick={() => (background === 'diy' ? openTint() : setPreferences({ background }))}
              >
                {t(label)}
                {prefs.background === background && (
                  <span className="chosen-dot">
                    <Icon name="check" />
                  </span>
                )}
              </Button>
            ))}
            <Button
              className={`background-option ${prefs.background === 'custom' ? 'active' : ''}`}
              data-action="upload-background"
              onClick={selectImage}
              style={
                prefs.background === 'custom' && prefs.customImage
                  ? {
                      backgroundImage: `linear-gradient(#0000 45%, #000000a6), url(${prefs.customImage})`,
                      backgroundSize: 'cover',
                      backgroundPosition: 'center',
                      color: '#fff',
                    }
                  : undefined
              }
            >
              {!(prefs.background === 'custom' && prefs.customImage) && (
                <Icon name="plus" className="background-add" />
              )}
              {t('图片背景')}
            </Button>
          </div>
          <p className="setting-caption">
            {t('可选像素场景、游戏截图或本地图片。背景仅用于启动器界面。')}
          </p>
        </div>
      </section>
      <section className="settings-section">
        <h2>{t('工作空间')}</h2>
        <div className="panel">
          <SettingRow
            title={t('首页快捷入口')}
            description={t('固定四项常用操作，按你的习惯选择。')}
          >
            <Button
              variant="ghost"
              symbol="sliders"
              data-action="extra-shortcuts"
              onClick={customize}
            >
              {t('自定义')}
            </Button>
          </SettingRow>
          <SettingRow title={t('操作记录')} description={t('查看本次预览的配置、任务与管理操作。')}>
            <Button variant="ghost" symbol="file" data-action="extra-activity" onClick={activity}>
              {t('查看记录')}
            </Button>
          </SettingRow>
        </div>
        <p className="setting-caption">
          {t('主题、屏幕方向、动效和快捷入口偏好保存在此浏览器；操作记录仅保留在本次会话。')}
        </p>
      </section>
      {!nativeMode && (
        <input
          type="file"
          id="background-file"
          ref={file}
          accept="image/*"
          hidden
          onChange={(event) => {
            const image = event.target.files?.[0];
            event.target.value = '';
            if (!image) return;
            if (!image.type.startsWith('image/')) return notify(t('请选择图片文件。'));
            if (image.size > 10 * 1024 * 1024) return notify(t('请选择 10 MB 以内的图片。'));
            const reader = new FileReader();
            reader.onerror = () => notify(t('无法读取图片，请重新选择。'));
            reader.onload = () => {
              if (typeof reader.result === 'string') {
                if (nativeMode) {
                  void prepareBackground(reader.result)
                    .then((customImage) => {
                      setPreferences({ background: 'custom', customImage });
                      notify(t('背景已保存。'));
                    })
                    .catch((error: Error) => notify(t(error.message)));
                } else {
                  setPreferences({ background: 'custom', customImage: reader.result });
                  notify(t('背景已用于本次原型预览。'));
                }
              }
            };
            reader.readAsDataURL(image);
          }}
        />
      )}
    </>
  );
}
function TintPalette({
  value,
  onApply,
  onCancel,
}: {
  value: string;
  onApply: (tint: string) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [rgb, setRgb] = useState<TintRGB>(() => hexToRgb(value));
  const tint = rgbToHex(rgb);
  return (
    <div className="tint-palette">
      <div className="tint-preview" style={{ background: tintGradient(tint) }} />
      <div className="tint-value">{tint.toUpperCase()}</div>
      {(['R', 'G', 'B'] as const).map((channel, index) => (
        <div className="tint-channel" key={channel}>
          <span>{channel}</span>
          <Slider
            id={`tint-${channel.toLowerCase()}`}
            label={t('{channel} 通道', { channel })}
            value={rgb[index]}
            min={0}
            max={255}
            unit=""
            onChange={(next) =>
              setRgb(
                (current) =>
                  current.map((component, position) =>
                    position === index ? next : component,
                  ) as TintRGB,
              )
            }
          />
        </div>
      ))}
      <div className="dialog-actions">
        <Button variant="ghost" data-action="cancel-tint" onClick={onCancel}>
          {t('取消')}
        </Button>
        <Button data-action="apply-tint" onClick={() => onApply(tint)}>
          {t('确定')}
        </Button>
      </div>
    </div>
  );
}
function LocalSwitch({
  title,
  description,
  label,
  action,
}: {
  title: string;
  description: string;
  label: string;
  action: string;
}) {
  const { t } = useTranslation();
  const [checked, setChecked] = useState(true);
  const { notify } = useWorkspaceActions();
  return (
    <SettingRow title={title} description={description}>
      <Switch
        checked={checked}
        label={label}
        action={action}
        onChange={(value) => {
          setChecked(value);
          notify(t('已更新原型中的设置状态。'));
        }}
      />
    </SettingRow>
  );
}
function Runtime() {
  const { t } = useTranslation();
  const { showInfo, nativeMode } = useWorkspaceActions();
  if (nativeMode) return <NativeRuntimeSettings />;
  return (
    <section className="settings-section">
      <h2>{t('游戏与运行')}</h2>
      <div className="panel">
        <LocalSwitch
          title={t('启动前校验')}
          description={t('检查资源、模组依赖与补丁兼容状态。')}
          label={t('启动前校验')}
          action="setting-validation"
        />
        <SettingRow title={t('渲染模式')} description={t('只展示当前设备可用的渲染选项。')}>
          <Select
            label={t('渲染模式')}
            defaultValue="auto"
            options={[
              ['auto', t('自动选择')],
              ['gles', 'OpenGL ES'],
            ]}
          />
        </SettingRow>
        <SettingRow title={t('启动参数')} description={t('高级选项，启动时应用。')}>
          <Button variant="ghost" symbol="chevron" onClick={() => showInfo('launch-args')}>
            {t('编辑参数')}
          </Button>
        </SettingRow>
      </div>
      <ContextNote>{t('设置项在正式接入运行时后生效；不提供无效的内存上限滑块。')}</ContextNote>
    </section>
  );
}
function Controls() {
  const { t } = useTranslation();
  const { showInfo, nativeMode } = useWorkspaceActions();
  const [opacity, setOpacity] = useState(80);
  if (nativeMode) return <NativeControlsSettings />;
  return (
    <section className="settings-section">
      <h2>{t('触控与输入')}</h2>
      <div className="panel">
        <LocalSwitch
          title={t('屏幕虚拟按键')}
          description={t('显示游戏内的虚拟控制布局。')}
          label={t('显示屏幕虚拟按键')}
          action="setting-touch"
        />
        <SettingRow title={t('按键布局')} description={t('布局编辑与预设单独管理。')}>
          <Button variant="ghost" symbol="chevron" onClick={() => showInfo('controls-layout')}>
            {t('编辑布局')}
          </Button>
        </SettingRow>
        <SettingRow title={t('按键不透明度')} description={t('在游戏内使用。')}>
          <Slider
            label={t('按键不透明度')}
            min={20}
            max={100}
            value={opacity}
            onChange={setOpacity}
          />
        </SettingRow>
        <SettingRow title={t('文本输入')} description={t('使用系统键盘进行游戏内文本输入。')}>
          <Badge>{t('系统键盘')}</Badge>
        </SettingRow>
      </div>
    </section>
  );
}
function Storage() {
  const { t } = useTranslation();
  const { showInfo, tasks, nativeMode } = useWorkspaceActions();
  if (nativeMode) return <NativeStorageSettings />;
  return (
    <section className="settings-section">
      <h2>{t('存储与下载')}</h2>
      <div className="panel">
        <SettingRow title={t('游戏与用户数据')} description={t('资源、存档与模组分开保存。')}>
          <Button variant="ghost" symbol="folder" onClick={() => showInfo('storage-paths')}>
            {t('查看目录')}
          </Button>
        </SettingRow>
        <LocalSwitch
          title={t('仅在 Wi-Fi 下下载')}
          description={t('移动网络下载时再次确认。')}
          label={t('仅在 Wi-Fi 下下载')}
          action="setting-wifi"
        />
        <SettingRow title={t('下载任务')} description={t('查看进度、暂停、重试与安装校验。')}>
          <Button variant="ghost" symbol="chevron" data-action="tasks" onClick={tasks}>
            {t('查看任务')}
          </Button>
        </SettingRow>
        <SettingRow title={t('缓存管理')} description={t('只清理临时下载和可重建缓存。')}>
          <Button variant="ghost" symbol="chevron" onClick={() => showInfo('cache')}>
            {t('管理缓存')}
          </Button>
        </SettingRow>
      </div>
    </section>
  );
}
function AboutLink({ href, label }: { href: string; label: string }) {
  const { t } = useTranslation();
  return href ? (
    <a className="about-link" href={href}>
      {label}
    </a>
  ) : (
    <span className="muted">{t('链接待公布')}</span>
  );
}

function About() {
  const { t } = useTranslation();
  const { showInfo, nativeMode, nativeState } = useWorkspaceActions();
  return (
    <section className="settings-section">
      <h2>{t('关于启动器')}</h2>
      <div className="panel">
        <div className="resource-row">
          <span className="resource-icon">
            <img src="/pocket-launcher.png" alt="" />
          </span>
          <div className="resource-copy">
            <h3>tML Pocket Launcher</h3>
            <p>{t('在 Android 上玩 tModLoader。导入自己的 Terraria 资源，为不同玩法建立实例，各自保存角色、世界、模组和触控布局。')}</p>
            <Badge>
              {nativeMode ? `Android · ${nativeState?.appVersion || ''}` : t('界面框架设计稿')}
            </Badge>
          </div>
        </div>
        <SettingRow title={t('项目作者')} description={t('FireworkSky 开发和维护这款启动器，将桌面版 tModLoader 适配到 Android。')}>
          <AboutLink href="https://github.com/FireworkSky" label="FireworkSky" />
        </SettingRow>
        <SettingRow title={t('项目地址')} description={t('源代码、构建步骤和改动记录都在 GitHub 仓库中。')}>
          <AboutLink href={projectUrl} label="GitHub" />
        </SettingRow>
        <SettingRow title={t('贡献者与致谢')} description={t('Aalt-hy 参与了工作区标签、页面过渡和玻璃面板的设计。游戏内触控基于 yu 的 MobileJoystick，部分 Android 适配来自 RotatingartLauncher。')}>
          <AboutLink href={projectUrl + '/graphs/contributors'} label={t('查看贡献记录')} />
        </SettingRow>
        <SettingRow title={t('第一次使用')} description={t('从选择 Steam、GOG 或手机版资源开始，跟着指南完成首次启动，再添加想玩的模组。')}>
          <AboutLink href={projectUrl + '/blob/HEAD/docs/getting-started.md'} label={t('阅读入门指南')} />
        </SettingRow>
        <SettingRow title={t('版本与更新')} description={t('在发布页查看各版本的更新说明和可下载的 APK。')}>
          <AboutLink href={projectUrl + '/releases'} label={t('查看发布版本')} />
        </SettingRow>
      </div>
      <h2>{t('社区交流')}</h2>
      <div className="panel">
        <SettingRow title={t('QQ 群')} description={t('在「tML Pocket Launcher 一群」聊模组搭配、分享触控布局，或请群友一起排查安装问题。')}>
          <AboutLink href={projectCommunity.qqInvite} label={t('加入 QQ 群')} />
        </SettingRow>
        <SettingRow title="Discord" description={t('来和其他玩家交流游玩体验。分享设备型号、模组版本和截图，方便大家比较同一模组在不同手机上的表现。')}>
          <AboutLink href={projectCommunity.discordInvite} label={t('加入 Discord')} />
        </SettingRow>
        <SettingRow title={t('报告问题或提出建议')} description={t('遇到能重复出现的问题，可以在 GitHub Issues 留下操作步骤和诊断日志，并在同一条记录里跟进进展。')}>
          <AboutLink href={projectUrl + '/issues'} label={t('查看 GitHub Issues')} />
        </SettingRow>
      </div>
      <h2>{t('支持我们')}</h2>
      <div className="panel">
        <SettingRow title={t('赞助地址')} description={t('如果你喜欢用这款启动器玩模组，可以通过 FireworkSky 的爱发电主页自愿赞助项目。')}>
          <AboutLink href={projectCommunity.sponsorshipUrl} label={t('前往爱发电')} />
        </SettingRow>
        <SettingRow title={t('参与项目')} description={t('发现文档漏了一步，或已经修好了一个问题？可以提交文档修改、代码补丁和设备测试结果。')}>
          <AboutLink href={projectUrl + '/blob/HEAD/CONTRIBUTING.md'} label={t('阅读贡献指南')} />
        </SettingRow>
        <SettingRow title={t('支持者名单')} description={t('感谢支持开发的玩家。这里展示愿意公开署名的支持者昵称。')}>
          {projectCommunity.supporters.length ? (
            <ul className="about-supporters">
              {projectCommunity.supporters.map((name, index) => (
                <li key={index}>{name}</li>
              ))}
            </ul>
          ) : (
            <span className="muted">{t('暂无公开名单')}</span>
          )}
        </SettingRow>
      </div>
      <h2>{t('日志与许可')}</h2>
      <div className="panel">
        <SettingRow title={t('诊断日志')} description={t('查看并分享最近的游戏运行日志。')}>
          <Button variant="ghost" symbol="chevron" onClick={() => showInfo('logs')}>
            {t('查看日志')}
          </Button>
        </SettingRow>
        <SettingRow title={t('开源与许可')} description={t('依赖、项目来源及授权信息。')}>
          <Button variant="ghost" symbol="chevron" onClick={() => showInfo('licenses')}>
            {t('查看说明')}
          </Button>
        </SettingRow>
        <details className="launcher-notice-settings">
          <summary>{t('法律声明、注意事项与问题反馈')}</summary>
          <LauncherNoticeContent />
        </details>
        <div className="about-preview">
          <DemoControl />
        </div>
      </div>
    </section>
  );
}
