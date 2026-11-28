import { useTranslation } from '../i18n';
import { useState } from 'react';
import type { Task } from '../domain/types';
import { useLauncher, profileTitle } from '../state/launcher';
import { Badge, Button, EmptyState, Icon, TaskProgress } from '../components/ui';
import { catalog } from '../domain/mods';
import { Select } from '../components/form-controls';
import { useNativeActions } from './native/actions';

export function useGameActions() {
  const { t } = useTranslation();
  const launcher = useLauncher();
  const nativeActions = useNativeActions(launcher);
  const { state, update, openDialog, closeDialog, navigate, record } = launcher;
  const notice = (title: string, description: string, body?: React.ReactNode) =>
    openDialog({
      title,
      description,
      body,
      actions: (
        <Button variant="primary" data-action="close" onClick={() => closeDialog()}>
          {t('知道了')}
        </Button>
      ),
    });
  const tasks = () =>
    openDialog({
      title: t('下载与任务'),
      description: t('下载时可以切换页面；任务状态始终可查看。'),
      body: <TaskPanel />,
      drawer: true,
    });
  const startTask = (kind: Task['kind'], ids: string[] = []) => {
    if (state.task) {
      notice(
        t('已有下载任务'),
        t('先完成或取消当前示例任务，再开始另一项下载。'),
        <Button variant="soft" onClick={tasks}>
          {t('查看当前任务')}
        </Button>,
      );
      return;
    }
    update((current) => ({ ...current, task: { kind, ids, paused: false } }));
    record('已开始示例下载任务', '任务');
    tasks();
  };
  const accounts = (platform: 'steam' | 'gog') =>
    openDialog({
      title: t('连接 {platform} 账户', { platform: platform === 'steam' ? 'Steam' : 'GOG' }),
      description:
        platform === 'steam'
          ? t('通过 Steam 登录并验证已购 Terraria。')
          : t('通过 GOG 官方授权页面连接账户，验证已购 Terraria。'),
      body: (
        <>
          <div className="steps-list">
            {[
              platform === 'steam'
                ? ['登录 Steam', '支持 Steam Guard 与邮箱验证码流程']
                : ['打开 GOG 授权页面', '授权完成后返回启动器'],
              ['验证游戏所有权', '确认账户拥有 Terraria'],
              ['选择资源并下载', '下载、提取、校验后安装'],
            ].map(([title, description], index) => (
              <div className="step" key={title}>
                <span className="step-index">{index + 1}</span>
                <div className="step-copy">
                  <strong>{t(title)}</strong>
                  <p>{t(description)}</p>
                </div>
              </div>
            ))}
          </div>
          <div className="info-strip">
            <Icon name="info" />
            {t('此处用于查看登录流程设计，不会收集账户信息。')}
          </div>
        </>
      ),
      actions: (
        <>
          <Button data-action="close" onClick={() => closeDialog()}>
            {t('取消')}
          </Button>
          <Button
            variant="primary"
            data-action={`account-library-${platform}`}
            onClick={() => library(platform)}
          >
            {t('预览授权后流程')}
          </Button>
        </>
      ),
    });
  const library = (platform: 'steam' | 'gog') =>
    openDialog({
      title: t('选择已购资源'),
      description: t('{platform} 游戏库 · 示例状态', {
        platform: platform === 'steam' ? 'Steam' : 'GOG',
      }),
      body: (
        <>
          <div className="panel dialog-spaced">
            <div className="resource-row">
              <span className="resource-icon">
                <Icon name="game" />
              </span>
              <div className="resource-copy">
                <h3>Terraria</h3>
                <p>{t('原版游戏资源')}</p>
                <Badge tone="green">{t('所有权已验证（示例）')}</Badge>
              </div>
            </div>
          </div>
          <div className="setting-row">
            <strong>{t('资源版本')}</strong>
            <Select
              label={t('选择资源版本')}
              defaultValue="compatible"
              options={[['compatible', t('与当前 tML 兼容的版本')]]}
            />
          </div>
          <p>{t('正式流程应展示版本、下载大小、可用空间与来源。')}</p>
        </>
      ),
      actions: (
        <>
          <Button onClick={() => accounts(platform)}>{t('返回')}</Button>
          <Button
            variant="primary"
            symbol="download"
            data-action="start-task-terraria"
            onClick={() => startTask('terraria')}
          >
            {t('演示下载任务')}
          </Button>
        </>
      ),
    });
  const versions = () =>
    openDialog({
      title: t('选择 tModLoader 版本'),
      description: t('稳定版适合日常游玩；预览版请先确认模组兼容性。'),
      body: <ReleaseList />,
      drawer: true,
    });
  const instances = () =>
    openDialog({
      title: t('版本与启动配置'),
      description: t('选择用于启动的已安装版本；下载新版本后会出现在这里。'),
      body: <InstanceList />,
      actions: state.demo ? (
        <Button variant="soft" symbol="plus" onClick={versions}>
          {t('添加版本')}
        </Button>
      ) : undefined,
      drawer: true,
    });
  const importInfo = (kind: 'terraria' | 'tml' | 'mod' | 'patch' | 'pack') => {
    const [title, description, steps] = {
      terraria: [
        '导入 Terraria 资源',
        '选择资源 ZIP，支持含 Content 目录的安装包与资源根目录。',
        '识别目录 → 校验 Images 资源 → 安装到游戏目录',
      ],
      tml: [
        '导入 tModLoader 发行包',
        '选择官方发行 ZIP，包含程序集、依赖和 Content。',
        '校验发行布局 → 检查运行版本 → 保留用户数据后安装',
      ],
      mod: [
        '导入游戏模组',
        '选择一个或多个游戏模组，核对名称、版本和依赖后确认导入。',
        '读取元信息 → 校验重复与依赖 → 加入当前配置',
      ],
      patch: [
        '导入补丁包',
        '展示补丁来源、用途、目标版本与文件变更，再确认安装。',
        '校验清单与目标 → 检查冲突 → 备份并安装，可恢复',
      ],
      pack: [
        '导入模组包',
        '选择 JSON 模组启用清单，导入后可在模组包中应用。',
        '读取清单 → 检查格式 → 保存本地预设',
      ],
    }[kind];
    notice(
      t(title),
      t(description),
      <>
        <div className="info-strip dialog-spaced">
          <Icon name="file" />
          {t(steps)}
        </div>
        <p className="dialog-spaced">
          {t('框架原型展示导入确认的位置与流程；文件操作将在 Android 界面实现时接入。')}
        </p>
      </>,
    );
  };
  const environment = () =>
    notice(
      t('运行环境检查'),
      t('启动前验证游戏资源、运行时、模组依赖与补丁目标。'),
      <div className="steps-list">
        {[
          ['游戏资源完整性', 'Terraria Content 与 tML 发行包'],
          ['模组与补丁兼容性', '缺失依赖、版本范围与冲突'],
          ['Android 运行环境', '运行时文件与本机适配库'],
        ].map(([title, description], index) => (
          <div className="step" key={title}>
            <span className="step-index">{index + 1}</span>
            <div className="step-copy">
              <strong>{t(title)}</strong>
              <p>{t(description)}</p>
            </div>
          </div>
        ))}
      </div>,
    );
  const showInfo = (id: string) => {
    const infos: Record<string, [string, string]> = {
      logs: ['诊断日志', '按启动时间查看日志；支持复制、导出与分享，导出前隐藏账户敏感信息。'],
      'launch-args': [
        '启动参数',
        '高级参数编辑独立打开，保存前校验格式。不要在基础设置页堆放技术参数。',
      ],
      'controls-layout': ['按键布局', '进入专门的布局编辑器，提供预设、拖动、缩放、还原和保存。'],
      'storage-paths': [
        '数据目录',
        '游戏发行包：games/tmodloader；原版资源：games/terraria；模组与存档：userdata。',
      ],
      cache: [
        '缓存管理',
        '分别展示临时下载、解压缓存和日志占用。清理前显示范围，不删除存档、模组或登录会话。',
      ],
      licenses: [
        '开源与许可',
        '当前 Web UI 使用 React、Motion、Radix UI 与 Animate UI；液态玻璃透镜为自实现的 WebGL 折射。',
      ],
      'mod-folder': [
        '模组目录',
        '模组与存档独立于游戏发行包，规划位置为 userdata/Mods；enabled.json 保存启用列表。',
      ],
    };
    const info = infos[id];
    if (!info) return;
    const [title, description] = info;
    if (id === 'licenses')
      notice(
        t(title),
        t(description),
        <div>
          <p>
            <a href="/animate-ui-license.txt" target="_blank" rel="noreferrer">
              {t('Animate UI 许可与版权')}
            </a>
          </p>
          <p>
            <a
              href="https://github.com/rxing365/html-liquid-glass-effect-webgl"
              target="_blank"
              rel="noreferrer"
            >
              {t('液态玻璃 WebGL 参考实现')}
            </a>
          </p>
        </div>,
      );
    else notice(t(title), t(description));
  };
  return {
    ...launcher,
    notice,
    tasks,
    startTask,
    accounts,
    library,
    versions,
    instances,
    importInfo,
    addMods: () => importInfo('mod'),
    environment,
    showInfo,
    getTerraria: () => {
      update((current) => ({ ...current, gameTab: 'sources' }));
      navigate('games');
    },
    ...(launcher.nativeMode ? nativeActions : {}),
  };
}
function InstanceList() {
  const { t } = useTranslation();
  const { state, update, closeDialog, navigate, record, versions, importInfo } = useGameActions();
  if (!state.demo)
    return (
      <EmptyState
        symbol="game"
        title={t('还没有可用版本')}
        description={t('先下载或导入 tModLoader，再选择用于启动的版本与配置。')}
      >
        <Button variant="primary" symbol="download" onClick={versions}>
          {t('下载 tML')}
        </Button>
        <Button symbol="folder" onClick={() => importInfo('tml')}>
          {t('导入 ZIP')}
        </Button>
      </EmptyState>
    );
  return (
    <div className="instance-list">
      {(['stable', 'preview'] as const).map((profile) => (
        <Button
          key={profile}
          className={`instance-option ${state.profile === profile ? 'selected' : ''}`}
          data-action={`choose-instance-${profile}`}
          onClick={() => {
            update((current) => ({ ...current, profile }));
            record('已切换至' + profileTitle(profile), '配置');
            closeDialog(false);
            navigate(state.page);
            requestAnimationFrame(() =>
              document.querySelector<HTMLElement>('.instance-picker')?.focus(),
            );
          }}
        >
          <span className="instance-icon">
            <Icon name="game" />
          </span>
          <span>
            <strong>{profile === 'stable' ? t('默认配置') : t('测试配置')}</strong>
            <small>{t('{profile} · 示例安装', { profile: t(profileTitle(profile)) })}</small>
          </span>
          <Icon name={state.profile === profile ? 'check' : 'chevron'} />
        </Button>
      ))}
    </div>
  );
}
function ReleaseList() {
  const { t } = useTranslation();
  const [channel, setChannel] = useState('all');
  const { openDialog, versions, startTask } = useGameActions();
  return (
    <>
      <div className="filter-group dialog-spaced">
        {[
          ['all', '全部版本'],
          ['stable', '稳定版'],
          ['preview', '预览版'],
        ].map(([id, label]) => (
          <Button
            key={id}
            className={`filter ${channel === id ? 'active' : ''}`}
            data-version-filter={id}
            onClick={() => setChannel(id)}
          >
            {t(label)}
          </Button>
        ))}
      </div>
      <div id="release-list">
        {(['stable', 'preview'] as const).map((profile) => (
          <div
            className="release-row"
            key={profile}
            data-channel={profile}
            hidden={channel !== 'all' && channel !== profile}
          >
            <div className="row-main">
              <h3>{t(profileTitle(profile))}</h3>
              <p>{t('正式接入后展示官方版本号、日期和大小')}</p>
            </div>
            <Badge tone={profile === 'stable' ? 'green' : 'blue'}>
              {profile === 'stable' ? t('稳定版') : t('预览版')}
            </Badge>
            <Button
              variant="soft"
              data-action={`select-release-${profile}`}
              onClick={() =>
                openDialog({
                  title: t('确认安装版本'),
                  description: t('当前选择：{profile}', { profile: t(profileTitle(profile)) }),
                  body: (
                    <div className="info-strip dialog-spaced">
                      <Icon name="info" />
                      {t('安装前检查所需空间与模组兼容性，替换游戏时保留存档和模组。')}
                    </div>
                  ),
                  actions: (
                    <>
                      <Button onClick={versions}>{t('返回版本列表')}</Button>
                      <Button
                        variant="primary"
                        symbol="download"
                        data-action="start-task-tml"
                        onClick={() => startTask('tml')}
                      >
                        {t('演示下载任务')}
                      </Button>
                    </>
                  ),
                })
              }
            >
              {t('选择')}
            </Button>
          </div>
        ))}
      </div>
      <div className="info-strip dialog-spaced">
        <Icon name="info" />
        {t('数据来源设计：GitHub Releases · tModLoader/tModLoader')}
      </div>
    </>
  );
}
export function TaskPanel() {
  const { t } = useTranslation();
  const { state, update, closeDialog, navigate, record, notify } = useGameActions();
  const task = state.task;
  if (!task)
    return (
      <EmptyState
        symbol="download"
        title={t('暂时没有下载任务')}
        description={t('游戏资源、版本与补丁的下载进度统一出现在这里。')}
      >
        <Button
          variant="soft"
          symbol="arrow"
          data-action="task-to-games"
          onClick={() => {
            update((current) => ({ ...current, gameTab: 'sources' }));
            navigate('games');
          }}
        >
          {t('获取资源')}
        </Button>
      </EmptyState>
    );
  const cancel = () => {
    update((current) => ({ ...current, task: null }));
    record('示例下载任务已结束', '任务');
    if (task.kind === 'mods') closeDialog(false);
  };
  const finish = () => {
    if (task.paused) return;
    update((current) => {
      const mods = [...current.mods];
      for (const id of task.ids) {
        const online = catalog.find((mod) => mod.id === id);
        if (!online) continue;
        const index = mods.findIndex((mod) => mod.id === id);
        const old = index >= 0 ? mods[index] : null;
        const replacement = {
          ...online,
          installed: true,
          enabled: Boolean(old && (current.demo || old.installed) && old.enabled),
          changed: Date.now(),
        };
        if (index >= 0) mods[index] = replacement;
        else mods.push(replacement);
      }
      return { ...current, mods, task: null };
    });
    record('示例下载任务已结束', '任务');
    closeDialog(false);
    notify('示例安装已完成；新模组保持停用。');
  };
  return (
    <div className="task-card">
      <div className="row-main">
        <h3>
          {task.kind === 'mods'
            ? t('{count} 项模组与依赖', { count: task.ids.length })
            : task.kind === 'terraria'
              ? t('Terraria 资源')
              : t('tModLoader 发行包')}
        </h3>
        <Badge tone="green">{task.paused ? t('已暂停') : t('下载中')}</Badge>
      </div>
      <p className="tiny muted dialog-spaced">{t('示例下载任务')}</p>
      <TaskProgress />
      <div className="task-meta">
        <span>{t('38% · 示例进度')}</span>
        <span>{task.paused ? t('等待继续') : t('下载 → 校验 → 安装')}</span>
      </div>
      {task.kind === 'mods' && (
        <div className="mod-plan mod-transfer-items">
          {task.ids.map((id) => (
            <div key={id}>
              <strong>{catalog.find((mod) => mod.id === id)?.title}</strong>
              <span>{t('示例文件')}</span>
            </div>
          ))}
        </div>
      )}
      <div className="task-actions">
        <Button
          symbol="pause"
          data-action={task.kind === 'mods' ? 'mw-pause-download' : 'pause-task'}
          onClick={() => {
            update((current) => ({
              ...current,
              task: current.task ? { ...current.task, paused: !current.task.paused } : null,
            }));
            record(task.paused ? '示例任务已继续' : '示例任务已暂停', '任务');
          }}
        >
          {task.paused ? t('继续') : t('暂停')}
        </Button>
        <Button
          variant="ghost"
          symbol="close"
          data-action={task.kind === 'mods' ? 'mw-cancel-download' : 'cancel-task'}
          onClick={cancel}
        >
          {t('取消任务')}
        </Button>
      </div>
      {task.kind === 'mods' && (
        <Button
          variant="primary"
          disabled={task.paused}
          data-action="mw-finish-download"
          onClick={finish}
        >
          {t('模拟下载完成')}
        </Button>
      )}
    </div>
  );
}
