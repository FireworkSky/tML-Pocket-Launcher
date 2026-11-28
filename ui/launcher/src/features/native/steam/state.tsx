import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import type { useTranslation } from '../../../i18n';
import { nativeRequest } from '../../../native/bridge';
import { useLauncher } from '../../../state/launcher';
import { type DownloadTask, useDownloads } from '../download-state';

export interface SteamAccount {
  phase: string;
  message: string;
  account: string;
  owned: boolean;
  canReconnect?: boolean;
  diagnostics?: string;
}
export interface SteamDownload {
  id: number;
  status: DownloadTask['status'];
  error?: string;
  cancelling: boolean;
}
export interface SteamGameDetails {
  appId: number;
  name: string;
  description: string;
  developers: string[];
  publishers: string[];
  genres: string[];
  releaseDate: string;
  headerImage: string;
  storeUrl: string;
  cached: boolean;
  stale: boolean;
  fetchedAt: number;
}
interface SteamContextValue {
  account: SteamAccount | null;
  connectionError: string;
  download: SteamDownload | null;
  refresh: () => Promise<void>;
  openSteam: () => void;
  startDownload: () => void;
  cancelDownload: () => void;
  details: SteamGameDetails | null;
  detailsLoading: boolean;
  detailsError: string;
  loadDetails: (force?: boolean) => Promise<void>;
}
const SteamContext = createContext<SteamContextValue | null>(null);
export const isSteamAuthenticating = (phase: string) =>
  ['connecting', 'reconnecting', 'authenticating', 'email', 'device', 'confirmation'].includes(
    phase,
  );

export function translateSteamAccountMessage(
  account: SteamAccount | null,
  t: ReturnType<typeof useTranslation>['t'],
) {
  if (!account) return '';
  const { phase, message } = account;
  if (phase === 'error' || phase === 'disconnected') return message;
  if (phase === 'connecting') {
    const attempt = /^正在连接 Steam · (?<route>.+)（(?<current>\d+)\/(?<total>\d+)）$/.exec(
      message,
    );
    if (attempt?.groups) return t('正在连接 Steam · {route}（{current}/{total}）', attempt.groups);
  }
  return t(message);
}

export function SteamProvider({ children }: { children: ReactNode }) {
  const { nativeMode, nativeWorkspace, openWorkspace } = useLauncher();
  const [account, setAccount] = useState<SteamAccount | null>(null);
  const [connectionError, setConnectionError] = useState('');
  const downloads = useDownloads();
  const download = downloads.tasks.find((task) => task.method === 'store.steam.download') ?? null;
  const [details, setDetails] = useState<SteamGameDetails | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [detailsError, setDetailsError] = useState('');
  const loadingDetails = useRef(false);
  const loadDetails = useCallback(async (force = false) => {
    if (loadingDetails.current) return;
    loadingDetails.current = true;
    setDetailsLoading(true);
    setDetailsError('');
    try {
      setDetails(await nativeRequest<SteamGameDetails>('store.steam.details', { refresh: force }));
    } catch (error) {
      setDetailsError((error as Error).message);
    } finally {
      setDetailsLoading(false);
      loadingDetails.current = false;
    }
  }, []);
  useEffect(() => {
    if (
      nativeMode &&
      (nativeWorkspace === 'steam-library' || nativeWorkspace === 'downloads') &&
      !details
    )
      void loadDetails();
  }, [nativeMode, nativeWorkspace, loadDetails, details]);
  const refresh = useCallback(async () => {
    try {
      const result = await nativeRequest<{ steam?: SteamAccount }>('store.status');
      if (result.steam) setAccount(result.steam);
      setConnectionError('');
    } catch (error) {
      setConnectionError((error as Error).message);
    }
  }, []);
  useEffect(() => {
    if (!nativeMode || !nativeWorkspace) return;
    let disposed = false;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      await refresh();
      if (!disposed) timer = setTimeout(poll, 1800);
    };
    void poll();
    return () => {
      disposed = true;
      clearTimeout(timer);
    };
  }, [nativeMode, nativeWorkspace, refresh]);
  useEffect(() => {
    if (nativeWorkspace === 'steam-login' && account?.phase === 'ready')
      openWorkspace('steam-library');
  }, [nativeWorkspace, account?.phase, openWorkspace]);
  const openSteam = () =>
    openWorkspace(account?.phase === 'ready' ? 'steam-library' : 'steam-login');
  const startDownload = () =>
    downloads.start({
      method: 'store.steam.download',
      name: details?.name || 'Terraria',
      source: 'Steam · Linux 游戏资源',
    });
  const cancelDownload = () => {
    if (download) downloads.cancel(download.id);
  };
  return (
    <SteamContext.Provider
      value={{
        account,
        connectionError,
        download,
        refresh,
        openSteam,
        startDownload,
        cancelDownload,
        details,
        detailsLoading,
        detailsError,
        loadDetails,
      }}
    >
      {children}
    </SteamContext.Provider>
  );
}
export function useSteam() {
  const context = useContext(SteamContext);
  if (!context) throw new Error('SteamProvider is required');
  return context;
}
