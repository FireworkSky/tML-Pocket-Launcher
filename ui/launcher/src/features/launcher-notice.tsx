import { useLayoutEffect, useState, type ReactNode } from 'react';
import { Button } from '../components/ui';
import { resolveLocale, TranslationProvider, useTranslation } from '../i18n';
import { nativeMode, nativeRequest } from '../native/bridge';
import { readPreferences } from '../state/preferences';
import { projectUrl } from '../domain/project';
import './launcher-notice.css';

export function LauncherNoticeContent() {
  const { t } = useTranslation();
  return (
    <div className="launcher-notice-content">
      <section>
        <h3>{t('法律声明')}</h3>
        <p>
          {t(
            'Terraria 及第三方素材和商标归各自权利人所有。请通过合法渠道取得游戏资源，并遵守相应的使用条款。',
          )}
        </p>
        <p>{t('启动器自有代码采用 GPL-3.0-only 许可，第三方组件适用各自的许可条款。')}</p>
        <div className="launcher-notice-links">
          <a href={projectUrl + '/blob/HEAD/LICENSE'}>{t('开源许可证')}</a>
          <a href={projectUrl + '/blob/HEAD/THIRD_PARTY_NOTICES.md'}>{t('第三方声明')}</a>
        </div>
      </section>
      <section>
        <h3>{t('使用注意事项')}</h3>
        <ul>
          <li>{t('导入游戏和模组前，请确认 Terraria、tModLoader 与模组的版本兼容。')}</li>
          <li>{t('更新游戏、更换模组或清理应用数据前，请先备份角色和世界存档。')}</li>
        </ul>
      </section>
      <section>
        <h3>{t('如何反馈问题')}</h3>
        <ol>
          <li>{t('在项目的 GitHub Issues 搜索是否已有同类问题。找不到时，创建一个新 Issue。')}</li>
          <li>
            {t(
              '写明手机型号、Android 版本、启动器和 tModLoader 版本，附上复现步骤、预期结果和实际结果。若问题与模组有关，请提供模组名称及版本。',
            )}
          </li>
          <li>
            {t(
              '可从「设置 → 关于 → 诊断日志」查看并导出日志。分享前请移除账号、令牌等个人信息，并避免上传游戏资源文件。',
            )}
          </li>
        </ol>
        <a href={projectUrl + '/issues'}>{t('查看 GitHub Issues')}</a>
      </section>
    </div>
  );
}

export function LauncherNoticeGate({ children }: { children: ReactNode }) {
  const [required, setRequired] = useState(
    () =>
      nativeMode && new URLSearchParams(window.location.search).get('notice-required') !== 'false',
  );
  if (!required) return children;
  const preferences = readPreferences();
  return (
    <TranslationProvider locale={resolveLocale(preferences.language, navigator.languages)}>
      <FirstLaunchNotice onAccept={() => setRequired(false)} />
    </TranslationProvider>
  );
}

function FirstLaunchNotice({ onAccept }: { onAccept: () => void }) {
  const { t } = useTranslation();
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);

  useLayoutEffect(() => {
    const preferences = readPreferences();
    const systemTheme = new URLSearchParams(window.location.search).get('system-theme');
    const applyTheme = (systemDark: boolean) => {
      const root = document.documentElement;
      const dark = preferences.theme === 'system' ? systemDark : preferences.theme === 'dark';
      root.dataset.theme = dark ? 'dark' : 'light';
      if (preferences.themeStyle === 'tml') root.dataset.skin = 'tml';
      else delete root.dataset.skin;
      root.dataset.motion = preferences.motion ? 'full' : 'reduced';
    };
    const themeChanged = (event: Event) => {
      const { dark } = (event as CustomEvent<{ dark: boolean }>).detail;
      applyTheme(dark);
    };
    applyTheme(systemTheme === 'dark');
    window.addEventListener('launcher:system-theme', themeChanged);
    return () => window.removeEventListener('launcher:system-theme', themeChanged);
  }, []);

  async function accept() {
    setSaving(true);
    setFailed(false);
    try {
      await nativeRequest('notice.accept');
      onAccept();
    } catch {
      setFailed(true);
      setSaving(false);
    }
  }

  return (
    <main className="launcher-notice-screen">
      <article className="launcher-notice-card panel" aria-labelledby="launcher-notice-title">
        <header>
          <img src="/pocket-launcher.png" width="44" height="44" alt="" />
          <h1 id="launcher-notice-title">{t('使用前请阅读')}</h1>
        </header>
        <div
          className="launcher-notice-scroll"
          role="region"
          aria-label={t('法律声明、注意事项与问题反馈')}
          tabIndex={0}
        >
          <LauncherNoticeContent />
        </div>
        <footer>
          {failed && <p role="alert">{t('无法保存同意状态，请重试。')}</p>}
          <Button
            variant="ghost"
            disabled={saving}
            onClick={() => void nativeRequest('finish').catch(() => setFailed(true))}
          >
            {t('退出启动器')}
          </Button>
          <Button variant="primary" disabled={saving} onClick={() => void accept()}>
            {saving ? t('正在保存…') : t('同意并进入')}
          </Button>
        </footer>
      </article>
    </main>
  );
}
