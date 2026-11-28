import { useEffect, useState } from 'react';
import { Button } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { useLauncher, type LauncherContextValue } from '../../state/launcher';
import { nativeRequest } from '../../native/bridge';
import { NativeGogPanel } from './gog';
import { useSteam } from './steam/state';
import { useNativeFileBrowser } from './files';
import { NativeVersionManager } from './versions';
import { NativeAcquireAction } from './controls';
import { NativeInstances, NativeSaveBackups } from './instances';

export function useNativeActions(launcher: LauncherContextValue) {
  const { t } = useTranslation();
  const { openDialog, closeDialog, navigate, notify, openWorkspace } = launcher;
  const { openSteam } = useSteam();
  const { browse } = useNativeFileBrowser();
  const notice = (title: string, description: string, body?: React.ReactNode) =>
    openDialog({ title, description, body });
  const run = (method: string, params: object = {}) => {
    closeDialog(false);
    void nativeRequest(method, params).catch((error: Error) =>
      notice(t('操作未完成'), error.message),
    );
  };
  const tasks = () => openWorkspace('downloads');
  const versions = () => openWorkspace('resource-versions');
  const importFile = (
    kind: 'terraria-steam' | 'terraria-gog' | 'tml' | 'mod' | 'patch' | 'pack',
  ) => {
    browse({
      kind,
      choose: (files) => nativeRequest('import', { kind, files }),
      failed: (failure) =>
        openDialog({
          title: t('导入失败'),
          description: t('无法导入所选文件，请查看具体原因后重新选择。'),
          body: (
            <pre className="native-log" role="alert" tabIndex={0}>
              {failure instanceof Error && failure.message.trim()
                ? failure.message
                : t('操作失败，未提供错误详情。')}
            </pre>
          ),
          actions: (
            <>
              <Button onClick={() => closeDialog()}>{t('关闭')}</Button>
              <Button onClick={() => importFile(kind)}>{t('重新选择')}</Button>
            </>
          ),
        }),
      complete: (imported) => {
        if (!imported) return;
        if (kind === 'terraria-steam') {
          openDialog({
            title: t('Steam 资源已导入'),
            description: t(
              '启动前需要登录拥有 Terraria 的 Steam 账户。每次启动都会在线验证游戏所有权。',
            ),
            actions: (
              <Button
                onClick={() => {
                  closeDialog(false);
                  openSteam();
                }}
              >
                {t('前往 Steam')}
              </Button>
            ),
          });
        } else
          notify(
            kind === 'terraria-gog'
              ? t('GOG 资源已导入并校验，无需登录即可启动。')
              : t('文件已导入并校验。'),
            '配置',
          );
      },
    });
  };
  const importInfo = (kind: 'terraria' | 'tml' | 'mod' | 'patch' | 'pack') => {
    if (kind !== 'terraria') {
      importFile(kind);
      return;
    }
    openDialog({
      title: t('导入 Terraria 资源'),
      description: t('选择资源来源。安装时保留模组与存档。'),
      body: (
        <div className="native-stack">
          <div className="resource-row">
            <div className="resource-copy">
              <h3>Steam · ZIP</h3>
              <p>{t('启动前登录 Steam，并在线验证账户拥有 Terraria。')}</p>
            </div>
            <Button onClick={() => importFile('terraria-steam')}>{t('选择 ZIP')}</Button>
          </div>
          <div className="resource-row">
            <div className="resource-copy">
              <h3>GOG · Linux .sh</h3>
              <p>{t('导入离线安装包，无需登录或验证游戏所有权。')}</p>
            </div>
            <Button onClick={() => importFile('terraria-gog')}>{t('选择 .sh')}</Button>
          </div>
        </div>
      ),
    });
  };
  const accounts = (platform: 'steam' | 'gog') =>
    platform === 'steam'
      ? openSteam()
      : openDialog({
          title: t('GOG 登录与下载'),
          description: t(
            '登录拥有 Terraria 的账户，下载并校验游戏资源。安装时保留现有模组与存档。',
          ),
          body: <NativeGogPanel />,
          drawer: true,
        });
  const addMods = () =>
    openDialog({
      title: t('添加模组'),
      description: t('选择获取方式。安装后可在「已安装」中管理启用状态。'),
      body: (
        <div className="native-acquire-options">
          <NativeAcquireAction
            title={t('下载模组')}
            description={t('进入 Steam 创意工坊下载中心')}
            symbol="download"
            onClick={() => openWorkspace('resource-mods')}
          />
          <NativeAcquireAction
            title={t('导入游戏模组')}
            description={t('选择本地模组文件，支持多选')}
            symbol="folder"
            onClick={() => importInfo('mod')}
          />
        </div>
      ),
    });
  const installedVersions = () =>
    openDialog({
      title: t('已安装版本'),
      description: t('为当前实例选择启动版本。已安装的 tML 发行文件由各实例共用。'),
      body: <NativeVersionManager />,
      actions: (
        <>
          <Button onClick={versions}>{t('下载版本')}</Button>
          <Button onClick={() => importInfo('tml')}>{t('导入 ZIP')}</Button>
        </>
      ),
      drawer: true,
    });
  const instances = () =>
    openDialog({
      title: t('游戏实例'),
      description: t('每个实例独立保存角色、世界、模组和启动设置，共用已安装的游戏资源。'),
      body: <NativeInstances />,
      actions: <Button onClick={installedVersions}>{t('管理已安装版本')}</Button>,
      drawer: true,
    });
  const environment = () => {
    void nativeRequest('refresh')
      .then(() => {
        openDialog({
          title: t('运行环境检查'),
          description: t('根据设备中实际安装的资源检查。'),
          body: <NativeEnvironment />,
        });
      })
      .catch((error: Error) => notice(t('检查失败'), error.message));
  };
  const showInfo = (id: string) => {
    if (id === 'logs')
      openDialog({
        title: t('诊断日志'),
        description: t('读取本机最近的运行日志。'),
        body: <NativeLogs />,
        drawer: true,
      });
    else if (id === 'save-backups')
      openDialog({
        title: t('存档备份'),
        description: t('备份或恢复当前实例中的角色和世界。'),
        body: <NativeSaveBackups />,
        drawer: true,
      });
    else if (['storage-paths', 'mod-folder', 'cache'].includes(id))
      openDialog({
        title: id === 'cache' ? t('缓存管理') : t('数据目录'),
        description: t('查看游戏资源、存档和模组的位置。'),
        body: <NativeStorage clear={id === 'cache'} />,
        drawer: true,
      });
    else if (id === 'launch-args')
      openDialog({
        title: t('启动参数'),
        description: t('每行一个参数，下一次启动生效。数据目录由启动器管理。'),
        body: <NativeArguments />,
        drawer: true,
      });
    else if (id === 'controls-layout')
      notice(
        t('触控布局'),
        t('在启动器的「设置 → 触控与输入」编辑控制和界面布局。游戏内也可以继续调整。'),
      );
    else if (id === 'licenses')
      openDialog({
        title: t('开源与许可'),
        description: t(
          'React、Motion、Radix、Animate UI、Inspira UI、AndroidX WebKit、SteamKit2 与游戏运行依赖。',
        ),
        body: (
          <>
            <p>{t('Animate UI: MIT + Commons Clause。')}</p>
            <p>{t('Inspira UI: MIT。')}</p>
            <p>{t('SteamKit2: LGPL-2.1-only。未修改的独立程序集，源码来自 SteamRE/SteamKit。')}</p>
            <p>
              {t(
                'RotatingartLauncher 触摸、显示、帧率、控制台与联机适配：GPL-3.0，源码位于 external/tML-Pocket-Patches。MobileJoystick 作者为 yu，游戏补丁源码和素材来源见 managed/MobileLauncher.Controls。',
              )}
            </p>
            <p>{t('完整许可文件随 APK 提供。')}</p>
          </>
        ),
      });
    else notice(t('启动器'), t('请从相应页面执行操作。'));
  };
  return {
    notice,
    tasks,
    versions,
    importFile,
    importInfo,
    accounts,
    addMods,
    instances,
    installedVersions,
    environment,
    showInfo,
    launch: () => run('launch'),
    startTask: () => {
      navigate('games');
      versions();
    },
  };
}

function NativeEnvironment() {
  const { t } = useTranslation();
  const { nativeState: state } = useLauncher();
  return (
    <dl className="detail-list">
      <dt>Terraria</dt>
      <dd>{state?.terraria || t('未安装或校验未通过')}</dd>
      <dt>tModLoader</dt>
      <dd>{state?.tml || t('未安装或校验未通过')}</dd>
      <dt>{t('Android 运行时')}</dt>
      <dd>{state?.runtime ? t('已随 APK 提供') : t('运行时资产缺失')}</dd>
      <dt>{t('启动')}</dt>
      <dd>{state?.ready ? t('资源已就绪，启动时再次检查依赖') : t('请先完成资源准备')}</dd>
    </dl>
  );
}
function NativeLogs() {
  const { t } = useTranslation();
  const { notify } = useLauncher();
  const [text, setText] = useState<string | null>(null);
  useEffect(() => {
    void nativeRequest<{ text: string }>('logs')
      .then((data) => setText(data.text))
      .catch((error: Error) => setText(error.message));
  }, []);
  return (
    <>
      <pre className="native-log">{text ?? t('正在读取…')}</pre>
      <Button
        onClick={() =>
          void nativeRequest('share', { text: text ?? '正在读取…' }).catch((error: Error) =>
            notify(error.message),
          )
        }
      >
        {t('分享日志')}
      </Button>
      <Button onClick={() => void nativeRequest('gameDiagnostics.share')
        .catch((error: Error) => notify(t(error.message)))}>
        {t('分享游戏运行诊断包')}
      </Button>
      <Button onClick={() => void nativeRequest('crashReport.shareLauncher')
        .catch((error: Error) => notify(t(error.message)))}>
        {t('分享启动器诊断包')}
      </Button>
    </>
  );
}
const STORAGE_LABELS: Readonly<Record<string, string>> = {
  sharedRoot: '共享游戏目录',
  games: '发行包',
  userdata: '用户数据',
  mods: '模组',
  cache: '下载缓存',
  availableBytes: '可用空间',
  cacheBytes: '可清理缓存',
};

function NativeStorage({ clear }: { clear: boolean }) {
  const { t } = useTranslation();
  const { nativeState } = useLauncher();
  const [data, setData] = useState<Record<string, string | number | boolean>>({});
  const [error, setError] = useState('');
  const read = (method = 'storage') => {
    setError('');
    void nativeRequest<Record<string, string | number | boolean>>(method)
      .then(setData)
      .catch((failure: Error) => setError(failure.message));
  };
  useEffect(() => {
    read();
    const refresh = () => read();
    window.addEventListener('launcher:files-resume', refresh);
    return () => window.removeEventListener('launcher:files-resume', refresh);
  }, []);
  return (
    <>
      <dl className="detail-list storage-detail-list">
        {Object.entries(data)
          .filter(([key]) => Object.hasOwn(STORAGE_LABELS, key))
          .map(([key, value]) => (
            <div key={key}>
              <dt>{t(STORAGE_LABELS[key])}</dt>
              <dd>
                {['games', 'userdata', 'mods'].includes(key) || (key === 'sharedRoot' && data.sharedExists) ? (
                  <button
                    type="button"
                    className="storage-folder-link"
                    aria-label={t('打开{folder}', { folder: t(STORAGE_LABELS[key]) })}
                    onClick={() => {
                      setError('');
                      void nativeRequest('storage.openFolder', { folder: key }).catch((failure: Error) =>
                        setError(failure.message),
                      );
                    }}
                  >
                    {String(value)}
                  </button>
                ) : typeof value === 'number' ? `${(value / 1048576).toFixed(1)} MB` : String(value)}
              </dd>
            </div>
          ))}
      </dl>
      {error && <p role="alert">{t(error)}</p>}
      {clear ? (
        <Button onClick={() => read('cache.clear')}>{t('清理下载临时文件')}</Button>
      ) : (
        data.sharedRoot && (
          <>
            <p>{t('卸载启动器后，共享目录中的存档和模组会保留。游戏资源需要重新导入或下载。')}</p>
            {data.storageGranted === false ? (
              <Button
                onClick={() =>
                  void nativeRequest('files.permission').catch((failure: Error) =>
                    setError(failure.message),
                  )
                }
              >
                {t('授予文件访问权限')}
              </Button>
            ) : data.shared ? (
              <p role="status">{t('已使用共享游戏目录')}</p>
            ) : (
              <Button disabled={nativeState?.busy} onClick={() => read('storage.enable')}>
                {t(data.sharedExists ? '接回已有共享游戏目录' : '使用共享游戏目录')}
              </Button>
            )}
          </>
        )
      )}
      {nativeState?.busy && nativeState.operation === 'storage.enable' && (
        <p role="status">{t(nativeState.message)}</p>
      )}
    </>
  );
}
function NativeArguments() {
  const { t } = useTranslation();
  const { nativeState, notify, closeDialog } = useLauncher();
  const [value, setValue] = useState(nativeState?.preferences.launchArguments?.join('\n') || '');
  return (
    <>
      <textarea
        aria-label={t('启动参数')}
        className="native-textarea"
        value={value}
        onChange={(event) => setValue(event.target.value)}
      />
      <Button
        variant="primary"
        onClick={() =>
          void nativeRequest('settings.save', {
            preferences: {
              launchArguments: value
                .split('\n')
                .map((item) => item.trim())
                .filter(Boolean),
            },
          })
            .then(() => {
              notify(t('启动参数已保存。'));
              closeDialog();
            })
            .catch((error: Error) => notify(error.message))
        }
      >
        {t('保存')}
      </Button>
    </>
  );
}
