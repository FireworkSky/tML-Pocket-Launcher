import * as ProgressPrimitive from '@animate-ui/react/primitives/radix-ui/progress';
import { Button, Icon } from '../../../components/ui';
import { useTranslation } from '../../../i18n';
import { useLauncher } from '../../../state/launcher';
import { useSteam } from './state';
import { type DownloadTask, useDownloads } from '../download-state';
import { ModDependencies } from '../mod-dependencies';

const STATUS_LABELS = {
  queued: '等待下载',
  running: '下载进行中',
  paused: '已暂停',
  completed: '安装完成',
  failed: '下载未完成',
  cancelled: '下载已取消',
} satisfies Record<DownloadTask['status'], string>;

function DownloadCard({ task, compact }: { task: DownloadTask; compact: boolean }) {
  const { t } = useTranslation();
  const { nativeState, navigate, update, openWorkspace, closeDialog } = useLauncher();
  const { account } = useSteam();
  const downloads = useDownloads();
  const running = task.status === 'running';
  const progress = task.progress >= 0 ? task.progress : null;
  const completed = task.status === 'completed';
  const failed = task.status === 'failed';
  const canRetry = failed || task.status === 'cancelled' || task.status === 'paused';
  const resumable = task.method === 'download';
  const steam = task.method === 'store.steam.download';
  const workshop = task.method === 'workshop.download';
  const terrariaWorkshop =
    workshop && (task.params as { appId?: number } | undefined)?.appId === 105600;
  const installedMod =
    completed && task.installedModId
      ? nativeState?.mods.find((mod) => mod.id === task.installedModId)
      : undefined;
  const installed = () => {
    closeDialog(false);
    if (terrariaWorkshop) {
      openWorkspace('resource-assets');
    } else if (workshop) {
      update((current) => ({ ...current, modTab: 'installed' }));
      navigate('mods');
    } else {
      update((current) => ({ ...current, gameTab: 'installed' }));
      navigate('games');
    }
  };

  return (
    <article className={`download-card ${completed ? 'is-complete' : ''}`} aria-label={task.name}>
      <div className="download-game-line">
        {workshop ? (
          <span className="download-game-icon">
            <Icon name={terrariaWorkshop ? 'wallpaper' : 'mod'} />
          </span>
        ) : (
          <span className="download-game-icon download-game-artwork">
            <img src={resumable ? '/tmodloader.png' : '/terraria.png'} alt="" />
          </span>
        )}
        <div>
          <span className="download-eyebrow">{t(task.source)}</span>
          <h3>{task.name}</h3>
          {!compact && <p>{t(STATUS_LABELS[task.status])}</p>}
        </div>
        {task.progress >= 0 && <strong className="download-percent">{task.progress}%</strong>}
      </div>
      {running && (
        <ProgressPrimitive.Root className="meter" value={progress} aria-label={t('下载进度')}>
          <ProgressPrimitive.Indicator
            style={{ transform: `translateX(-${progress === null ? 0 : 100 - progress}%)` }}
          />
        </ProgressPrimitive.Root>
      )}
      {(running || failed) && task.message && (
        <p
          className={failed ? 'download-error' : 'download-message'}
          role={failed ? 'alert' : 'status'}
        >
          {task.message}
        </p>
      )}
      {installedMod && (
        <details className="download-mod-dependencies">
          <summary>{t('查看模组依赖')}</summary>
          <ModDependencies mod={installedMod} />
        </details>
      )}
      <div className="download-card-actions">
        {(task.status === 'queued' ||
          (running && (!resumable || nativeState?.downloadCanPause !== false))) && (
          <Button disabled={task.cancelling} onClick={() => downloads.cancel(task.id)}>
            {t(
              resumable
                ? task.cancelling
                  ? '正在暂停…'
                  : '暂停'
                : task.cancelling
                  ? '正在取消…'
                  : '取消任务',
            )}
          </Button>
        )}
        {completed && (
          <Button variant="primary" onClick={installed}>
            {t('查看已安装资源')}
            <Icon name="arrow" />
          </Button>
        )}
        {canRetry && (
          <Button
            variant="primary"
            disabled={
              (!resumable &&
                (downloads.tasks.some((entry) => entry.status === 'running') ||
                  nativeState?.busy)) ||
              (steam && (account?.phase !== 'ready' || !account.owned))
            }
            onClick={() => downloads.retry(task.id)}
          >
            {t(resumable && task.status !== 'cancelled' ? '继续下载' : '重新下载')}
          </Button>
        )}
        {canRetry && steam && account?.phase !== 'ready' && (
          <Button onClick={() => openWorkspace('steam-login')}>{t('恢复 Steam 连接')}</Button>
        )}
        {compact ? (
          <Button onClick={downloads.minimize}>{t('最小化')}</Button>
        ) : (
          <Button onClick={() => downloads.show(task.id)}>{t('查看下载窗口')}</Button>
        )}
      </div>
    </article>
  );
}

export function NativeDownloadsPage({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();
  const { openWorkspace } = useLauncher();
  const { tasks, focusedTask } = useDownloads();
  const visibleTasks = compact ? (focusedTask ? [focusedTask] : []) : tasks;
  const titleId = compact ? 'download-window-title' : 'download-title';

  return (
    <section
      className={`download-page ${compact ? 'download-compact' : ''}`}
      aria-labelledby={titleId}
    >
      <div className="download-page-heading">
        <h2 id={titleId}>
          {t(compact && focusedTask ? STATUS_LABELS[focusedTask.status] : '下载任务')}
        </h2>
      </div>
      {visibleTasks.length > 0 ? (
        <div className="download-task-list">
          {visibleTasks.map((task) => (
            <DownloadCard key={task.id} task={task} compact={compact} />
          ))}
        </div>
      ) : (
        <div className="download-empty">
          <Icon name="download" />
          <h3>{t('还没有下载任务')}</h3>
          {!compact && (
            <Button variant="primary" onClick={() => openWorkspace('resource-versions')}>
              {t('浏览下载资源')}
              <Icon name="arrow" />
            </Button>
          )}
        </div>
      )}
    </section>
  );
}
