import { useEffect, useState } from 'react';
import { LabeledSelect } from '../../components/form-controls';
import { Badge, Button, WorkspaceTabs } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import type { TerrariaSource } from '../../native/models';
import { useLauncher } from '../../state/launcher';
import { useGameActions } from '../game-actions';
import { NativeVersionManager } from './versions';
import { NativeAcquireAction } from './controls';
import { useNativeActions } from './actions';

const editions = [
  { source: 'steam', label: 'Steam' },
  { source: 'gog', label: 'GOG' },
  { source: 'android', label: 'Terraria PE' },
] as const;

export function NativeGamesPage() {
  const { t } = useTranslation();
  const { state, update, nativeState, versions, importInfo, accounts } = useGameActions();
  const launcher = useLauncher();
  const { openDialog, closeDialog, notify } = launcher;
  const { importFile } = useNativeActions(launcher);
  const tab = state.gameTab === 'accounts' ? 'sources' : state.gameTab;
  const installedEditions = nativeState?.installedTerraria ?? [];
  const activeEdition = installedEditions.find((edition) => edition.active);
  const editionOptions = editions.flatMap(({ source, label }) => {
    const installed = installedEditions.find((edition) => edition.source === source);
    return installed ? [[source, `${label} · ${installed.version}`] as const] : [];
  });
  const install = (source: TerrariaSource) => {
    if (source === 'android') {
      openDialog({
        title: 'Terraria PE',
        description: t('本机 Terraria 资源'),
        body: <MobileTerrariaInspection />,
      });
      return;
    }
    openDialog({
      title: t('安装 {source} 版 Terraria', { source: source === 'steam' ? 'Steam' : 'GOG' }),
      description: t(source === 'steam' ? 'Steam ZIP' : 'GOG Linux .sh'),
      actions: (
        <>
          <Button
            onClick={() => {
              closeDialog(false);
              importFile(source === 'steam' ? 'terraria-steam' : 'terraria-gog');
            }}
          >
            {t('导入资源')}
          </Button>
          <Button
            variant="primary"
            onClick={() => {
              closeDialog(false);
              accounts(source);
            }}
          >
            {t('登录并下载')}
          </Button>
        </>
      ),
    });
  };
  const changeEdition = (
    method: 'terraria.select' | 'terraria.uninstall',
    source: TerrariaSource,
  ) => void nativeRequest(method, { source }).catch((error: Error) => notify(t(error.message)));

  return (
    <div className="native-games-workspace">
      <WorkspaceTabs
        value={tab}
        onChange={(gameTab) => update((current) => ({ ...current, gameTab }))}
        label={t('游戏资源分类')}
        items={[
          {
            value: 'installed',
            label: t('已安装'),
            content: (
              <div className="native-game-library">
                <section
                  className="panel native-version-panel"
                  aria-labelledby="terraria-library-title"
                >
                  <div className="panel-header">
                    <h2 id="terraria-library-title">{t('Terraria 游戏管理')}</h2>
                  </div>
                  <div className="native-versions version-list">
                    {editionOptions.length > 0 && (
                      <LabeledSelect
                        className="version-selector"
                        label={t('切换 Terraria 版本')}
                        value={activeEdition?.source ?? ''}
                        options={[
                          ...(!activeEdition ? ([['', t('选择版本'), true]] as const) : []),
                          ...editionOptions,
                        ]}
                        disabled={
                          nativeState?.busy || (!!activeEdition && editionOptions.length === 1)
                        }
                        onChange={(source) =>
                          changeEdition('terraria.select', source as TerrariaSource)
                        }
                      />
                    )}
                    {editions.map(({ source, label }) => {
                      const installed = installedEditions.find(
                        (edition) => edition.source === source,
                      );
                      const disabled = !nativeState || nativeState.busy;
                      return (
                        <div className="version-item" key={source}>
                          <div className="version-summary">
                            <strong>{label}</strong>
                            <span>{installed?.version || t('未安装')}</span>
                            {installed?.active && <Badge tone="green">{t('当前使用')}</Badge>}
                            {installed?.hasPeTextures && <Badge>{t('带 PE 纹理')}</Badge>}
                          </div>
                          <div className="version-actions">
                            {installed && !installed.active && (
                              <Button
                                disabled={disabled}
                                onClick={() => changeEdition('terraria.select', source)}
                              >
                                {t('使用此版本')}
                              </Button>
                            )}
                            <Button disabled={disabled} onClick={() => install(source)}>
                              {t(installed ? '重新安装' : '安装游戏')}
                            </Button>
                            {installed && (
                              <Button
                                disabled={disabled}
                                onClick={() =>
                                  openDialog({
                                    title: t('卸载 {source} 版 Terraria', { source: label }),
                                    description: t('删除此版本的游戏资源。存档和模组会保留。'),
                                    actions: (
                                      <>
                                        <Button onClick={() => closeDialog()}>{t('取消')}</Button>
                                        <Button
                                          onClick={() => {
                                            closeDialog(false);
                                            changeEdition('terraria.uninstall', source);
                                          }}
                                        >
                                          {t('卸载')}
                                        </Button>
                                      </>
                                    ),
                                  })
                                }
                              >
                                {t('卸载')}
                              </Button>
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </section>
                <section
                  className="panel native-version-panel"
                  aria-labelledby="installed-versions-title"
                >
                  <div className="panel-header">
                    <h2 id="installed-versions-title">{t('tModLoader 版本')}</h2>
                  </div>
                  <NativeVersionManager compact />
                </section>
              </div>
            ),
          },
          {
            value: 'sources',
            label: t('添加游戏'),
            content: (
              <div className="native-game-sources">
                <section className="panel native-source-section" aria-label={t('添加 tModLoader')}>
                  <div className="native-section-heading">
                    <img src="/tmodloader.png" alt="" />
                    <div>
                      <h2>tModLoader</h2>
                      <p>{t('各版本独立安装，模组与存档共用。')}</p>
                    </div>
                  </div>
                  <div className="native-acquire-options">
                    <NativeAcquireAction
                      title={t('下载版本')}
                      description={t('浏览官方稳定版与预览版')}
                      symbol="download"
                      action="versions"
                      onClick={versions}
                    />
                    <NativeAcquireAction
                      title={t('导入 ZIP')}
                      description={t('安装本地预编译发行包')}
                      symbol="folder"
                      onClick={() => importInfo('tml')}
                    />
                  </div>
                </section>
                <section
                  className="panel native-source-section"
                  aria-label={t('获取 Terraria 资源')}
                >
                  <div className="native-section-heading">
                    <img src="/terraria.png" alt="" />
                    <div>
                      <h2>{t('Terraria 原版资源')}</h2>
                      <p>{t('Steam 和 GOG 版可直接使用内置触控纹理。')}</p>
                    </div>
                  </div>
                  <div className="native-acquire-options">
                    <NativeAcquireAction
                      title="Steam"
                      label={t('Steam 登录与下载')}
                      description={t('账户登录、所有权验证与下载')}
                      symbol="user"
                      onClick={() => accounts('steam')}
                    />
                    <NativeAcquireAction
                      title="GOG"
                      label={t('GOG 登录与下载')}
                      description={t('官方网页授权与已购资源下载')}
                      symbol="user"
                      onClick={() => accounts('gog')}
                    />
                    <NativeAcquireAction
                      title={t('导入资源')}
                      description={t('Steam ZIP 或 GOG Linux .sh')}
                      symbol="folder"
                      onClick={() => importInfo('terraria')}
                    />
                    <NativeAcquireAction
                      title="Terraria PE"
                      label={t('检测 Terraria PE')}
                      description={t('从本机安装包转换游戏资源')}
                      symbol="game"
                      onClick={() => install('android')}
                    />
                  </div>
                </section>
              </div>
            ),
          },
        ]}
      />
    </div>
  );
}

type MobileTerrariaCandidate = {
  status: 'unity' | 'unsupported' | 'signature-mismatch';
  packageName: string;
  channel: 'google-play';
  version: string;
  apkCount: number;
};
type MobileTerrariaResult = {
  status: 'not-installed' | MobileTerrariaCandidate['status'];
  packageName?: string;
  packages: MobileTerrariaCandidate[];
  targetTml: string | null;
};

function MobileTerrariaInspection() {
  const { t, locale } = useTranslation();
  const { nativeState, closeDialog, navigate, notify } = useLauncher();
  const [result, setResult] = useState<MobileTerrariaResult>();
  const [error, setError] = useState('');
  const [conversionError, setConversionError] = useState('');
  const [inspectionAttempt, setInspectionAttempt] = useState(0);
  const [cancelling, setCancelling] = useState(false);
  const converting = nativeState?.busy && nativeState.operation === 'terraria.mobile.convert';
  useEffect(() => {
    let active = true;
    void nativeRequest<MobileTerrariaResult>('terraria.mobile.inspect').then(
      (value) => {
        if (active) setResult(value);
      },
      (failure: Error) => {
        if (active) setError(failure.message);
      },
    );
    return () => {
      active = false;
    };
  }, [inspectionAttempt]);
  if (error) return <p role="alert">{t('安装包检查失败。{error}', { error })}</p>;
  if (!result) return <p role="status">{t('正在检查本机安装包…')}</p>;
  if (result.status === 'not-installed') {
    return (
      <div className="native-stack">
        <p role="status">{t('本机未检测到 Google Play 版 Terraria PE。')}</p>
        <div className="dialog-actions">
          <Button
            onClick={() => {
              setResult(undefined);
              setInspectionAttempt((attempt) => attempt + 1);
            }}
          >
            {t('重新检测')}
          </Button>
          <Button
            variant="primary"
            onClick={() =>
              void nativeRequest('terraria.mobile.openStore').catch(() =>
                notify(t('无法打开 Google Play。')),
              )
            }
          >
            {t('前往 Google Play')}
          </Button>
        </div>
      </div>
    );
  }

  const selected = result.packages[0];
  if (!selected) return <p role="alert">{t('请重新检测 Terraria PE 安装包。')}</p>;

  const conversionMessage = nativeState?.message ?? '';
  const conversionStep = /^正在转换(贴图、字体和音效|音乐)（(\d+)\/(\d+)）…$/.exec(
    conversionMessage,
  );
  const conversionStatus = conversionStep
    ? t(
        conversionStep[1] === '音乐'
          ? '正在转换音乐（{current}/{total}）…'
          : '正在转换贴图、字体和音效（{current}/{total}）…',
        {
          current: Number(conversionStep[2]).toLocaleString(locale),
          total: Number(conversionStep[3]).toLocaleString(locale),
        },
      )
    : t(conversionMessage);

  return (
    <div className="native-stack">
      {selected.status !== 'signature-mismatch' && <p>{t('Google Play 版签名已验证')}</p>}
      <p>
        {t('已检测到 Terraria {version}，共 {count} 个 APK。', {
          version: selected.version || t('未知版本'),
          count: selected.apkCount.toLocaleString(locale),
        })}
      </p>
      {selected.status === 'signature-mismatch' ? (
        <p role="alert">{t('已安装的 Terraria 签名与 Google Play 版不匹配。')}</p>
      ) : selected.status === 'unsupported' ? (
        <p>{t('安装包缺少受支持的 Unity 资源容器。')}</p>
      ) : !result.targetTml ? (
        <p>{t('此 APK 未包含 Terraria PE 转换配置。')}</p>
      ) : converting ? (
        <>
          <p role="status">{conversionStatus}</p>
          <progress
            aria-label={t('资源转换进度')}
            max={100}
            value={nativeState.progress >= 0 ? nativeState.progress : undefined}
          />
          <Button
            disabled={cancelling}
            onClick={() => {
              setCancelling(true);
              void nativeRequest('cancel').catch((failure: Error) => {
                setCancelling(false);
                setConversionError(failure.message);
              });
            }}
          >
            {t('取消')}
          </Button>
        </>
      ) : (
        <Button
          variant="primary"
          disabled={!nativeState || nativeState.busy}
          onClick={() => {
            setCancelling(false);
            setConversionError('');
            void nativeRequest('terraria.mobile.convert', {
              packageName: selected.packageName,
            }).then(
              () => {
                closeDialog(false);
                navigate('home');
                notify(t('Terraria PE 资源已就绪。'));
              },
              (failure: Error) => setConversionError(failure.message),
            );
          }}
        >
          {t('转换资源')}
        </Button>
      )}
      {conversionError && <p role="alert">{conversionError}</p>}
    </div>
  );
}
