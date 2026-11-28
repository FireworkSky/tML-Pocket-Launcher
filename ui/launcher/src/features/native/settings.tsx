import { useEffect, useState } from 'react';
import { Badge, Button, SettingRow, Switch } from '../../components/ui';
import { Select } from '../../components/form-controls';
import { useGameActions } from '../game-actions';
import { nativeRequest } from '../../native/bridge';
import { useTranslation } from '../../i18n';
import { useLauncher } from '../../state/launcher';
import { ControlsEditor } from './controls-editor';
import { useControlArtwork } from './control-artwork';

export function NativeRuntimeSettings() {
  const { t } = useTranslation();
  const { showInfo, nativeState, notify } = useGameActions();
  const [childLimit, setChildLimit] = useState('unknown');
  useEffect(() => {
    let mounted = true;
    const refresh = () => void nativeRequest<string>('system.childProcessLimit')
      .then((status) => { if (mounted) setChildLimit(status); })
      .catch(() => { if (mounted) setChildLimit('unknown'); });
    refresh();
    window.addEventListener('launcher:files-resume', refresh);
    return () => {
      mounted = false;
      window.removeEventListener('launcher:files-resume', refresh);
    };
  }, []);
  const rendererOptions = nativeState?.renderers?.length
    ? nativeState.renderers
    : [
        {
          identity: 'native',
          packageName: '',
          packageVersion: '',
          rendererId: 'native',
          displayName: '系统原生 OpenGL ES 3',
          description: '',
          backend: 'native',
        },
        {
          identity: 'vulkan',
          packageName: '',
          packageVersion: '',
          rendererId: 'vulkan',
          displayName: 'SDL GPU · Vulkan',
          description: 'SDL2-compat + SDL3 GPU',
          backend: 'vulkan',
        },
      ];
  const selectedRenderer = nativeState?.preferences.rendererPlugin ?? 'native';
  const rendererSelection = rendererOptions.some(
    (renderer) => renderer.identity === selectedRenderer,
  )
    ? selectedRenderer
    : 'native';
  return (
    <section className="settings-section">
      <h2>{t('游戏与运行')}</h2>
      <div className="panel">
        <SettingRow
          title={t('启动前校验')}
          description={t('检查实际资源、模组依赖和补丁目标版本。')}
        >
          <Badge tone="green">{t('始终启用')}</Badge>
        </SettingRow>
        <SettingRow title={t('渲染模式')} description={t('选择 SDL2/FNA3D 图形后端。')}>
          <Select
            label={t('渲染器')}
            value={rendererSelection}
            options={rendererOptions.map((renderer) => [
              renderer.identity,
              renderer.identity === 'native'
                ? t(renderer.displayName)
                : `${renderer.displayName} · ${renderer.packageName}${renderer.packageVersion ? ` ${renderer.packageVersion}` : ''}`,
            ])}
            onChange={(rendererPlugin) =>
              void nativeRequest('settings.save', { preferences: { rendererPlugin } }).catch(
                (error: Error) => notify(t(error.message)),
              )
            }
          />
        </SettingRow>
        <SettingRow
          title={t('内部渲染分辨率')}
          description={t('降低分辨率可减轻绘制负担，画面会变软，视野也可能缩小。下次启动生效。')}
        >
          <Select
            label={t('内部渲染分辨率')}
            value={nativeState?.preferences.fixed1080p ? 'fixed' : String(nativeState?.preferences.renderResolutionPercent ?? 100)}
            options={[
              ['100', t('原始分辨率（关闭）')],
              ['fixed', '2400 × 1080'],
              ['75', '75%'],
              ['60', t('60%（受游戏最低分辨率限制）')],
            ]}
            onChange={(value) =>
              void nativeRequest('settings.save', {
                preferences: value === 'fixed' ? { fixed1080p: true } : { fixed1080p: false, renderResolutionPercent: Number(value) },
              }).catch((error: Error) => notify(t(error.message)))
            }
          />
        </SettingRow>
        <SettingRow title={t('解锁高刷新率')} description={t('下次启动游戏时请求屏幕支持的最高刷新率。配合 120 FPS 补丁使用，实际帧率取决于系统设置和游戏负载。')}>
          <Switch
            label={t('解锁高刷新率')}
            checked={nativeState?.preferences.highRefreshRate !== false}
            onChange={(highRefreshRate) => void nativeRequest('settings.save', { preferences: { highRefreshRate } })
              .catch((error: Error) => notify(t(error.message)))}
          />
        </SettingRow>
        {childLimit !== 'unsupported' && (
          <SettingRow title={t('停止限制子进程')} description={[
            t('在系统开发者选项中打开「停止限制子进程」，并保持开发者选项开启，以减少子进程被系统结束的情况。'),
            t(childLimit === 'unrestricted' ? '已停止限制。' : childLimit === 'restricted' ? '系统仍在限制子进程。' : '请在系统设置中确认开关状态。'),
          ].join(' ')}>
            <Button variant="ghost" onClick={() => void nativeRequest<boolean>('system.developerOptions')
              .then((direct) => { if (!direct) notify(t('请在系统设置中搜索开发者选项。')); })
              .catch((error: Error) => notify(t(error.message)))}>
              {t('打开开发者选项')}
            </Button>
          </SettingRow>
        )}
        <SettingRow title={t('启动参数')} description={t('每行一个参数，在下一次启动生效。')}>
          <Button variant="ghost" onClick={() => showInfo('launch-args')}>
            {t('编辑参数')}
          </Button>
        </SettingRow>
      </div>
    </section>
  );
}
export function NativeControlsSettings() {
  const { t } = useTranslation();
  return (
    <section className="settings-section">
      <h2>{t('触控与输入')}</h2>
      <div className="panel">
        <SettingRow
          title="MobileJoystick"
          description={t('随启动器加载的游戏补丁，提供双摇杆、触屏背包、菜单与地图。')}
        >
          <Badge tone="green">{t('内置触控系统')}</Badge>
        </SettingRow>
        <SettingRow title={t('文本输入')} description={t('游戏内使用 Android 系统键盘。')}>
          <Badge>{t('系统键盘')}</Badge>
        </SettingRow>
      </div>
    </section>
  );
}

export function NativeLayoutManagement() {
  const { t } = useTranslation();
  const { openWorkspace, nativeState } = useLauncher();
  const [editing, setEditing] = useState(false);
  const textures = useControlArtwork(nativeState, editing);
  return (
    <section className="settings-section">
      <h2>{t('控制管理')}</h2>
      <div className="panel">
        <SettingRow title={t('游戏布局')}>
          <Button onClick={() => setEditing(true)}>{t('编辑游戏布局')}</Button>
        </SettingRow>
        <SettingRow title={t('控制布局包')}>
          <Button onClick={() => openWorkspace('control-packs')}>{t('管理布局包')}</Button>
        </SettingRow>
      </div>
      {editing && <ControlsEditor controlTextures={textures} onClose={() => setEditing(false)} />}
    </section>
  );
}
export function NativeStorageSettings() {
  const { t } = useTranslation();
  const { nativeState, notify, showInfo, tasks } = useGameActions();
  return (
    <section className="settings-section">
      <h2>{t('存储与下载')}</h2>
      <div className="panel">
        <SettingRow title={t('游戏与用户数据')} description={t('资源、存档与模组分开保存。')}>
          <Button variant="ghost" onClick={() => showInfo('storage-paths')}>
            {t('查看目录')}
          </Button>
        </SettingRow>
        <SettingRow title={t('存档备份')} description={t('备份或恢复当前实例中的角色和世界。')}>
          <Button variant="ghost" onClick={() => showInfo('save-backups')}>
            {t('管理备份')}
          </Button>
        </SettingRow>
        <SettingRow title={t('仅在 Wi-Fi 下下载')} description={t('启用后阻止使用计费网络下载。')}>
          <Switch
            label={t('仅在 Wi-Fi 下下载')}
            checked={nativeState?.preferences.wifiOnly === true}
            onChange={(wifiOnly) =>
              void nativeRequest('settings.save', { preferences: { wifiOnly } }).catch(
                (error: Error) => notify(t(error.message)),
              )
            }
          />
        </SettingRow>
        <SettingRow title={t('下载任务')} description={t('查看进度，暂停或继续下载 tML 版本。')}>
          <Button variant="ghost" onClick={tasks}>
            {t('查看任务')}
          </Button>
        </SettingRow>
        <SettingRow title={t('缓存管理')} description={t('仅清理启动器下载临时文件。')}>
          <Button variant="ghost" onClick={() => showInfo('cache')}>
            {t('管理缓存')}
          </Button>
        </SettingRow>
      </div>
    </section>
  );
}
