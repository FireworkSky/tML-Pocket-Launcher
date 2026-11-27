import { useTranslation } from '../i18n';
import { useState } from 'react';
import { Announcements } from './announcements';
import { Button, Icon, Badge } from '../components/ui';
import { useGameActions } from './game-actions';
import { needsUpdate, defaultFilters } from '../domain/mods';
import { profileTitle } from '../state/launcher';
import type { ActivityKind, Tool } from '../domain/types';

export const tools: { id: Tool; title: string; detail: string; symbol: string }[] = [
  { id: 'mods', title: '模组管理', detail: '启停与依赖', symbol: 'mod' },
  { id: 'browse', title: '下载模组', detail: '浏览与更新', symbol: 'download' },
  { id: 'packs', title: '模组包', detail: '保存启用清单', symbol: 'folder' },
  { id: 'patches', title: '补丁管理', detail: '适配与扩展', symbol: 'patch' },
  { id: 'controls', title: '触控布局', detail: '按键与输入', symbol: 'sliders' },
  { id: 'storage', title: '游戏目录', detail: '资源与存档', symbol: 'folder' },
  { id: 'tasks', title: '下载任务', detail: '进度与队列', symbol: 'download' },
  { id: 'accounts', title: '商店账户', detail: 'Steam / GOG', symbol: 'user' },
  { id: 'logs', title: '诊断日志', detail: '启动与排错', symbol: 'file' },
  { id: 'activity', title: '操作记录', detail: '本次使用记录', symbol: 'reset' },
];
export function useWorkspaceActions() {
  const { t } = useTranslation();
  const actions = useGameActions();
  const { openDialog, navigate, update, tasks, showInfo, nativeMode } = actions;
  const customize = () =>
    openDialog({
      title: t('自定义常用工具'),
      description: t('选择四项放在首页；显示顺序按选择顺序排列。'),
      body: <ShortcutPicker />,
      drawer: true,
    });
  const activity = () =>
    openDialog({
      title: t('操作记录'),
      description: t('本次会话的最近 50 条记录；配置、下载和提示分开查看。'),
      body: <ActivityPanel />,
      drawer: true,
    });
  const openTool = (id: Tool) => {
    if (id === 'mods' || id === 'patches') navigate(id);
    else if (id === 'browse' || id === 'packs') {
      update((current) => ({ ...current, modTab: id }));
      navigate('mods');
    } else if (id === 'controls' || id === 'storage') {
      update((current) => ({
        ...current,
        settingsTab: id === 'controls' && nativeMode ? 'control-management' : id,
      }));
      navigate('settings');
    } else if (id === 'tasks') tasks();
    else if (id === 'accounts') {
      update((current) => ({ ...current, gameTab: 'accounts' }));
      navigate('games');
    } else if (id === 'activity') activity();
    else showInfo(id);
  };
  return { ...actions, customize, activity, openTool };
}
export function HomePage() {
  const { t, locale } = useTranslation();
  const {
    state,
    installed,
    update,
    navigate,
    customize,
    activity,
    openTool,
    getTerraria,
    versions,
    environment,
    instances,
    tasks,
  } = useWorkspaceActions();
  const ready = state.demo;
  const last = state.activity.at(-1);
  const setup = [
    {
      symbol: 'folder',
      title: t('Terraria 资源'),
      detail: ready ? t('资源完整性校验通过') : t('Steam / GOG 下载，或导入本地 ZIP'),
      action: getTerraria,
      label: t('获取'),
      id: 'get-terraria',
    },
    {
      symbol: 'game',
      title: 'tModLoader',
      detail: ready ? profileTitle(state.profile) : t('下载官方发行版，或导入发行 ZIP'),
      action: versions,
      label: t('添加'),
      id: 'versions',
    },
    {
      symbol: 'patch',
      title: t('运行环境'),
      detail: ready ? t('Android 内置适配已就绪') : t('资源安装后检查兼容性'),
      action: environment,
      label: t('检查'),
      id: 'environment',
    },
  ];
  return (
    <>
      <Announcements />
      <div className="home-workspace">
        <section className="panel game-overview setup-panel" aria-label={t('tModLoader 游戏状态')}>
          <div className="workspace-section">
            <h2>{t('游戏准备')}</h2>
            <span>{ready ? t('3 / 3 已就绪') : t('0 / 3 已完成')}</span>
          </div>
          {setup.map((item) => (
            <Button
              className="setup-row"
              key={item.title}
              data-action={item.id}
              onClick={item.action}
            >
              <span className="setup-icon">
                <Icon name={item.symbol} />
              </span>
              <span className="setup-copy">
                <strong>{t(item.title)}</strong>
                <small>{t(item.detail)}</small>
              </span>
              <span className={`setup-state ${ready ? 'complete' : ''}`}>
                {ready ? <Icon name="check" /> : <span>{t(item.label)}</span>}
                <Icon name="chevron" />
              </span>
            </Button>
          ))}
        </section>
        <section className="home-tools">
          <div className="workspace-section tools-heading">
            <h2>{t('常用工具')}</h2>
            <Button className="quiet-action" data-action="extra-shortcuts" onClick={customize}>
              {t('自定义')} <Icon name="sliders" />
            </Button>
          </div>
          <div className="workspace-tools">
            {state.preferences.quickTools.map((id) => {
              const tool = tools.find((tool) => tool.id === id)!;
              return (
                <Button key={id} data-action={`extra-tool-${id}`} onClick={() => openTool(id)}>
                  <Icon name={tool.symbol} />
                  <span>
                    <strong>{t(tool.title)}</strong>
                    <small>{t(tool.detail)}</small>
                  </span>
                  <Icon name="chevron" />
                </Button>
              );
            })}
          </div>
        </section>
      </div>
      <section className="panel workspace-overview" aria-label={t('配置概览')}>
        <div className="workspace-section">
          <div>
            <h2>{t('配置概览')}</h2>
            <p>
              {ready ? t(profileTitle(state.profile)) + t(' · 示例数据') : t('尚未选择游戏版本')}
            </p>
          </div>
          <Button className="quiet-action" data-action="instances" onClick={instances}>
            {t('切换')} <Icon name="chevron" />
          </Button>
        </div>
        <div className="overview-stats">
          <Button
            data-action="extra-enabled"
            onClick={() => {
              update((current) => ({
                ...current,
                modTab: 'installed',
                localQuery: '',
                localFilter: 'enabled',
              }));
              navigate('mods');
            }}
          >
            <strong>
              {installed.filter((mod) => mod.enabled).length}
              <small> / {installed.length}</small>
            </strong>
            <span>{t('启用模组')}</span>
          </Button>
          <Button
            data-action="extra-updates"
            onClick={() => {
              update((current) => ({
                ...current,
                modTab: 'browse',
                filters: { ...defaultFilters, status: 'updates' },
              }));
              navigate('mods');
            }}
          >
            <strong>{installed.filter(needsUpdate).length}</strong>
            <span>{t('可更新模组')}</span>
          </Button>
          <Button data-action="extra-tool-patches" onClick={() => openTool('patches')}>
            <strong>{ready && state.patchEnabled ? 1 : 0}</strong>
            <span>{t('可选补丁')}</span>
          </Button>
          <Button data-action="tasks" onClick={tasks}>
            <strong>{state.task ? 1 : 0}</strong>
            <span>{t('下载任务')}</span>
          </Button>
        </div>
        <Button className="home-activity" data-action="extra-activity" onClick={activity}>
          <span id="home-activity">
            <span className="activity-indicator">
              <Icon name="reset" />
            </span>
            <span>
              <strong>{t(last?.text || '从这里继续你的冒险')}</strong>
              <small>
                {last
                  ? `${t(last.kind)} · ${time(last.time, locale)}`
                  : t('本次操作记录会保留到关闭预览。')}
              </small>
            </span>
          </span>
          <span className="activity-more">
            {t('全部记录')} <Icon name="chevron" />
          </span>
        </Button>
      </section>
      <p className="workspace-footnote">
        <Icon name="lock" />
        {t('存档与模组独立保存，更新游戏时保留。')}
      </p>
    </>
  );
}
function ShortcutPicker() {
  const { t } = useTranslation();
  const { state, setPreferences, closeDialog, notify } = useWorkspaceActions();
  const [draft, setDraft] = useState([...state.preferences.quickTools]);
  const [full, setFull] = useState(false);
  return (
    <>
      <div className="shortcut-count" id="shortcut-count" role="status">
        {full ? t('已满 4 项，请先取消一项') : t('已选择 {count} / 4', { count: draft.length })}
      </div>
      <div className="shortcut-options">
        {tools.map((tool) => (
          <Button
            key={tool.id}
            data-action={`extra-pick-${tool.id}`}
            aria-pressed={draft.includes(tool.id)}
            onClick={() => {
              setFull(false);
              if (draft.includes(tool.id)) setDraft(draft.filter((id) => id !== tool.id));
              else if (draft.length < 4) setDraft([...draft, tool.id]);
              else setFull(true);
            }}
          >
            <Icon name={tool.symbol} />
            <span>
              <strong>{t(tool.title)}</strong>
              <small>{t(tool.detail)}</small>
            </span>
            <span className="shortcut-check">
              <Icon name="check" />
            </span>
          </Button>
        ))}
      </div>
      <p className="shortcut-hint">{t('先取消一项，即可替换。取消或关闭不会保存。')}</p>
      <div className="dialog-actions feature-actions">
        <Button data-action="close" onClick={() => closeDialog()}>
          {t('取消')}
        </Button>
        <Button
          variant="primary"
          data-action="extra-save-shortcuts"
          disabled={draft.length !== 4}
          onClick={() => {
            setPreferences({ quickTools: draft });
            closeDialog();
            notify('已保存首页快捷入口。');
          }}
        >
          {t('保存布局')}
        </Button>
      </div>
    </>
  );
}
const time = (value: number, locale: string) =>
  new Date(value).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' });
function ActivityPanel() {
  const { t, locale } = useTranslation();
  const { state, update, openDialog, closeDialog } = useWorkspaceActions();
  const [filter, setFilter] = useState<ActivityKind | '全部'>('全部');
  const entries = state.activity
    .filter((item) => filter === '全部' || item.kind === filter)
    .toReversed();
  return (
    <>
      <div className="activity-filters" role="group" aria-label={t('筛选操作记录')}>
        {(['全部', '配置', '任务', '提示'] as const).map((kind) => (
          <Button
            key={kind}
            data-action={`extra-filter-${kind}`}
            aria-pressed={filter === kind}
            onClick={() => setFilter(kind)}
          >
            {t(kind)}
          </Button>
        ))}
      </div>
      <div id="activity-list">
        {entries.length ? (
          <ol className="activity-list">
            {entries.map((item) => (
              <li key={item.id}>
                <span className="activity-indicator">
                  <Icon
                    name={
                      item.kind === '任务' ? 'download' : item.kind === '配置' ? 'sliders' : 'info'
                    }
                  />
                </span>
                <span>
                  <strong>{t(item.text)}</strong>
                  <small>
                    {t(item.kind)} · {time(item.time, locale)}
                  </small>
                </span>
              </li>
            ))}
          </ol>
        ) : (
          <div className="activity-empty">
            <Icon name="reset" />
            <strong>{t(filter === '全部' ? '暂无记录' : `暂无${filter}记录`)}</strong>
            <p>{t('操作后会显示在这里，不会在刷新后保留。')}</p>
          </div>
        )}
      </div>
      <div className="feature-actions">
        <Button
          variant="ghost"
          data-action="extra-clear-activity"
          onClick={() =>
            openDialog({
              title: t('清空本次操作记录？'),
              description: t('只清除当前会话的记录，游戏数据与设置不受影响。'),
              actions: (
                <>
                  <Button onClick={() => closeDialog()}>{t('取消')}</Button>
                  <Button
                    variant="primary"
                    data-action="extra-confirm-clear"
                    onClick={() => {
                      update((current) => ({ ...current, activity: [] }));
                      openDialog({
                        title: t('操作记录'),
                        description: t('本次会话的最近 50 条记录；配置、下载和提示分开查看。'),
                        body: <ActivityPanel />,
                        drawer: true,
                      });
                    }}
                  >
                    {t('确认清空')}
                  </Button>
                </>
              ),
            })
          }
        >
          {t('清空记录')}
        </Button>
        <Badge>{t('仅本次会话')}</Badge>
      </div>
    </>
  );
}
