import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from '../../i18n';
import { nativeMode, nativeRequest } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';

export type DownloadMethod =
  | 'download'
  | 'store.steam.download'
  | 'store.gog.download'
  | 'workshop.download';

export function translateDownloadProgress(
  message: string,
  t: ReturnType<typeof useTranslation>['t'],
) {
  const steam = /^Steam：已处理 (?<done>[\d.]+)\/(?<total>[\d.]+) MiB · 已校验 (?<files>\d+)\/(?<count>\d+) 个文件$/.exec(message);
  if (steam?.groups) return t('Steam：已处理 {done}/{total} MiB · 已校验 {files}/{count} 个文件', steam.groups);

  const store = /^(?<store>Steam|GOG)：下载资源 (?<done>\d+)\/(?<total>\d+) MB$/.exec(message);
  if (store?.groups) return t('{store}：下载资源 {done}/{total} MB', store.groups);

  const manifest = /^Steam：读取资源清单 (?<id>\d+)$/.exec(message);
  if (manifest?.groups) return t('Steam：读取资源清单 {id}', manifest.groups);

  const workshop = /^正在下载工坊内容 · (?<size>\d+(?:\/\d+)? MB)$/.exec(message);
  if (workshop?.groups) return t('正在下载工坊内容 · {size}', workshop.groups);

  const release = /^正在下载 (?<version>.+) · (?<size>\d+ MB)$/.exec(message);
  if (release?.groups) return t('正在下载 {version} · {size}', release.groups);

  const extracting = /^正在解压：(?<filename>.+)$/.exec(message);
  if (extracting?.groups) return t('正在解压：{filename}', extracting.groups);

  return t(message);
}

export interface DownloadRequest {
  method: DownloadMethod;
  params?: object;
  name: string;
  source: string;
}
export interface DownloadTask extends DownloadRequest {
  id: number;
  status: 'queued' | 'running' | 'paused' | 'completed' | 'failed' | 'cancelled';
  message: string;
  progress: number;
  error?: string;
  cancelling: boolean;
  installedModId?: string;
}
interface DownloadsContextValue {
  tasks: DownloadTask[];
  activeCount: number;
  focusedTask: DownloadTask | undefined;
  start: (request: DownloadRequest) => void;
  cancel: (id: number) => void;
  retry: (id: number) => void;
  show: (id: number) => void;
  minimize: () => void;
}
const DownloadsContext = createContext<DownloadsContextValue | null>(null);
const STORAGE_KEY = 'tml-pocket-downloads-v1';
const TASK_LIMIT = 50;
const statuses = ['queued', 'running', 'paused', 'completed', 'failed', 'cancelled'];
const methods = ['download', 'store.steam.download', 'store.gog.download', 'workshop.download'];

function loadTasks(): DownloadTask[] {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (!saved || saved.length > 512_000) return [];
    const entries: unknown = JSON.parse(saved);
    if (!Array.isArray(entries)) return [];
    const ids = new Set<number>();
    return entries.slice(0, TASK_LIMIT).flatMap((entry: DownloadTask) => {
      if (
        !entry ||
        !Number.isSafeInteger(entry.id) ||
        entry.id <= 0 ||
        ids.has(entry.id) ||
        !methods.includes(entry.method) ||
        !statuses.includes(entry.status) ||
        typeof entry.name !== 'string' ||
        typeof entry.source !== 'string' ||
        typeof entry.message !== 'string' ||
        !Number.isFinite(entry.progress) ||
        (entry.params !== undefined &&
          (!entry.params || typeof entry.params !== 'object' || Array.isArray(entry.params))) ||
        (entry.method === 'download' &&
          !/^(?:gitee-)?\d{1,19}$/.test(String((entry.params as { id?: string })?.id)))
      )
        return [];
      ids.add(entry.id);
      // A new WebView cannot receive the previous operation's reply.
      if (entry.status === 'running' || entry.status === 'queued')
        return [
          {
            ...entry,
            status: entry.method === 'download' ? 'paused' : 'cancelled',
            cancelling: false,
          },
        ];
      return [{ ...entry, cancelling: false }];
    });
  } catch {
    return [];
  }
}

export function DownloadsProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { nativeState, dialog, openDialog, closeDialog, notify } = useLauncher();
  const [tasks, setTasks] = useState<DownloadTask[]>(loadTasks);
  const [focusedId, setFocusedId] = useState<number>();
  const activeId = useRef<number | undefined>(undefined);
  const cancelRequested = useRef(false);
  const sequence = useRef(Math.max(0, ...tasks.map((task) => task.id)));
  const show = (id: number) => {
    setFocusedId(id);
    openDialog({
      title: t('下载与安装'),
      description: t('最小化后可在下载任务中查看进度。'),
      surface: 'download',
    });
  };
  const saveTask = (task: DownloadTask) =>
    setTasks((current) => {
      const index = current.findIndex((entry) => entry.id === task.id);
      if (index >= 0) return current.map((entry) => (entry.id === task.id ? task : entry));
      const next = [task, ...current];
      if (next.length > TASK_LIMIT) {
        const oldestFinished = next.findLastIndex((entry) =>
          ['completed', 'failed', 'cancelled'].includes(entry.status),
        );
        if (oldestFinished >= 0) next.splice(oldestFinished, 1);
      }
      return next;
    });
  const start = (request: DownloadRequest) => {
    const existing =
      request.method === 'download' &&
      tasks.find(
        (task) =>
          task.method === 'download' &&
          (task.params as { id?: string })?.id === (request.params as { id?: string })?.id &&
          task.status !== 'completed' &&
          task.status !== 'cancelled',
      );
    if (existing) {
      show(existing.id);
      return;
    }
    if (
      request.method !== 'download' &&
      (activeId.current !== undefined ||
        nativeState?.busy ||
        tasks.some((entry) => entry.status === 'queued'))
    ) {
      notify(t('已有任务正在进行，请先完成或取消。'));
      return;
    }
    if (
      tasks.length >= TASK_LIMIT &&
      !tasks.some((entry) => ['completed', 'failed', 'cancelled'].includes(entry.status))
    ) {
      notify(t('下载任务列表已满，请先完成或取消任务。'));
      return;
    }
    const id = ++sequence.current;
    saveTask({
      ...request,
      id,
      status: 'queued',
      cancelling: false,
      progress: -1,
      message: '正在准备下载…',
    });
    show(id);
  };
  useEffect(() => {
    if (!nativeMode) return;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(tasks));
    } catch {
      // The current session and native download checkpoints still work without WebView storage.
    }
  }, [tasks]);
  useEffect(() => {
    if (!nativeState || nativeState.busy || activeId.current !== undefined) return;
    const request = tasks.findLast((task) => task.status === 'queued');
    if (!request) return;
    const { id } = request;
    activeId.current = id;
    cancelRequested.current = false;
    saveTask({ ...request, status: 'running', cancelling: false, message: '正在准备下载…' });
    void nativeRequest<{ modId?: string | null } | boolean>(request.method, {
      ...request.params,
      taskId: id,
    })
      .then((result) => {
        saveTask({
          ...request,
          id,
          status: 'completed',
          cancelling: false,
          progress: 100,
          message: '下载、校验与安装已完成。',
          installedModId: typeof result === 'object' ? (result?.modId ?? undefined) : undefined,
        });
        notify(t('{name} 已下载、校验并安装。', { name: request.name }), '任务');
      })
      .catch((failure: unknown) => {
        const error =
          failure instanceof Error && failure.message.trim()
            ? failure.message
            : t('下载未完成，请重试。');
        const cancelled = cancelRequested.current;
        saveTask({
          ...request,
          id,
          status: cancelled ? (request.method === 'download' ? 'paused' : 'cancelled') : 'failed',
          cancelling: false,
          progress: -1,
          message: error,
          error,
        });
        if (!cancelled)
          notify(t('{name} 下载未完成，可在下载任务中查看原因。', { name: request.name }), '任务');
      })
      .finally(() => {
        activeId.current = undefined;
      });
  }, [nativeState, tasks]);
  const cancel = (id: number) => {
    const task = tasks.find((entry) => entry.id === id);
    if (!task) return;
    if (task.status === 'queued') {
      saveTask({ ...task, status: task.method === 'download' ? 'paused' : 'cancelled' });
      return;
    }
    if (task.status !== 'running' || id !== activeId.current || cancelRequested.current) return;
    const setCancelling = (cancelling: boolean) =>
      setTasks((current) =>
        current.map((task) =>
          task.id === id && task.status === 'running' ? { ...task, cancelling } : task,
        ),
      );
    cancelRequested.current = true;
    setCancelling(true);
    void nativeRequest('cancel', { operation: task.method, taskId: id }).catch((failure: Error) => {
      if (activeId.current !== id) return;
      cancelRequested.current = false;
      setCancelling(false);
      notify(failure.message);
    });
  };
  const currentTasks = tasks.map((task) =>
    task.status === 'running' && nativeState?.busy && nativeState.operation === task.method
      ? {
          ...task,
          message: translateDownloadProgress(nativeState.message, t),
          progress: nativeState.progress,
        }
      : task.status === 'running' || task.status === 'completed'
        ? { ...task, message: t(task.message) }
        : task,
  );
  return (
    <DownloadsContext.Provider
      value={{
        tasks: currentTasks,
        activeCount: tasks.filter((task) => task.status === 'queued' || task.status === 'running')
          .length,
        focusedTask: currentTasks.find((task) => task.id === focusedId),
        start,
        cancel,
        retry: (id) => {
          const task = tasks.find((entry) => entry.id === id);
          if (
            task &&
            (task.status === 'failed' || task.status === 'cancelled' || task.status === 'paused')
          ) {
            saveTask({ ...task, status: 'queued', cancelling: false, error: undefined });
            show(id);
          }
        },
        show,
        minimize: () => {
          if (dialog?.surface === 'download') closeDialog();
        },
      }}
    >
      {children}
    </DownloadsContext.Provider>
  );
}
export function useDownloads() {
  const context = useContext(DownloadsContext);
  if (!context) throw new Error('DownloadsProvider is required');
  return context;
}
