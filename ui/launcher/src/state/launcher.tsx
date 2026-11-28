import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { AnimateUIProvider } from '@animate-ui/react/provider';
import { MotionConfig } from 'motion/react';
import type { ActivityKind, LauncherState, Page, Preferences } from '../domain/types';
import { defaultFilters, demoMods } from '../domain/mods';
import {
  defaultPreferences,
  normalizePreferences,
  readPreferences,
  savePreferences,
} from './preferences';
import { tintGradient } from '../domain/background-tint';
import { useLauncherOrientation } from './use-launcher-orientation';
import { nativeMode, nativeRequest, onNativeSnapshot } from '../native/bridge';
import type { NativeSnapshot } from '../native/models';
import { resolveLocale, TranslationProvider } from '../i18n';

export interface DialogSpec {
  title: string;
  description: string;
  body?: ReactNode;
  actions?: ReactNode;
  onDismiss?: () => void;
  drawer?: boolean;
  prototype?: boolean;
  surface?: 'files' | 'download';
}
export interface DialogState extends DialogSpec {
  id: number;
  open: boolean;
}
type Update = (updater: (current: LauncherState) => LauncherState) => void;
function preferredSystemDark() {
  const preset = new URLSearchParams(location.search).get('system-theme');
  return preset ? preset === 'dark' : matchMedia('(prefers-color-scheme:dark)').matches;
}
export const resourceDestinations = [
  { id: 'resource-versions', label: '游戏', symbol: 'game' },
  { id: 'resource-mods', label: '模组', symbol: 'mod' },
  { id: 'resource-assets', label: '资源', symbol: 'wallpaper' },
  { id: 'resource-packs', label: '模组包', symbol: 'folder' },
] as const;
export type ResourceWorkspace = (typeof resourceDestinations)[number]['id'];
export type NativeWorkspace = 'steam-login' | 'steam-library' | 'downloads' | 'control-packs' | ResourceWorkspace;
export function isResourceWorkspace(
  workspace: NativeWorkspace | null,
): workspace is ResourceWorkspace {
  return resourceDestinations.some((item) => item.id === workspace);
}
export const workspaceNames: Record<NativeWorkspace, string> = {
  'steam-login': 'Steam',
  'steam-library': 'Steam',
  downloads: '下载',
  'control-packs': '控制布局包',
  'resource-versions': '下载 - 游戏',
  'resource-mods': '下载 - 模组',
  'resource-assets': '下载 - 资源',
  'resource-packs': '下载 - 模组包',
};
export interface LauncherContextValue {
  nativeMode: boolean;
  nativeState: NativeSnapshot | null;
  state: LauncherState;
  installed: LauncherState['mods'];
  update: Update;
  navigate: (page: Page) => void;
  nativeWorkspace: NativeWorkspace | null;
  openWorkspace: (workspace: NativeWorkspace) => void;
  closeWorkspace: () => void;
  setPreferences: (patch: Partial<Preferences>) => void;
  dialog: DialogState | null;
  openDialog: (spec: DialogSpec) => void;
  closeDialog: (restore?: boolean) => void;
  returnFocus: React.RefObject<HTMLElement | null>;
  toast: string;
  notify: (text: string, kind?: ActivityKind) => void;
  record: (text: string, kind?: ActivityKind) => void;
  motionEnabled: boolean;
}
const LauncherContext = createContext<LauncherContextValue | null>(null);
export const pageNames: Record<Page, string> = {
  home: '首页',
  games: '游戏',
  mods: '模组',
  patches: '补丁',
  settings: '设置',
};
export const profileTitle = (profile: LauncherState['profile']) =>
  profile === 'preview' ? '示例预览版本' : '示例稳定版本';

function initialState(): LauncherState {
  const hash = location.hash.slice(1).split('/')[0] as Page;
  return {
    page: Object.hasOwn(pageNames, hash) ? hash : 'home',
    demo: false,
    profile: 'stable',
    gameTab: 'installed',
    modTab: new URLSearchParams(location.search).get('tab') === 'browse' ? 'browse' : 'installed',
    patchTab: 'builtin',
    settingsTab: nativeMode && location.hash === '#settings/control-packs' ? 'control-management' : 'appearance',
    localQuery: '',
    localFilter: 'all',
    localSort: 'name',
    filters: { ...defaultFilters },
    mods: nativeMode ? [] : demoMods.map((mod) => ({ ...mod })),
    packs: [],
    configs: {},
    preferences: readPreferences(),
    patchEnabled: true,
    task: null,
    activity: [],
  };
}
export function LauncherProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState(initialState);
  const [browserLanguages, setBrowserLanguages] = useState(() => navigator.languages);
  const [nativeWorkspace, setNativeWorkspace] = useState<NativeWorkspace | null>(() => {
    const route = location.hash.split('/')[1] as NativeWorkspace;
    return nativeMode && Object.hasOwn(workspaceNames, route) ? route : null;
  });
  const workspaceReturn = useRef<NativeWorkspace | null>(null);
  const [nativeState, setNativeState] = useState<NativeSnapshot | null>(null);
  const [androidLocale, setAndroidLocale] = useState('');
  const locale = resolveLocale(
    state.preferences.language,
    androidLocale ? [androidLocale] : browserLanguages,
  );
  useEffect(() => {
    const updateLanguages = () => setBrowserLanguages(navigator.languages);
    window.addEventListener('languagechange', updateLanguages);
    return () => window.removeEventListener('languagechange', updateLanguages);
  }, []);
  const nativeHydrated = useRef(false);
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [toast, setToast] = useState('');
  const [pointer, setPointer] = useState(true);
  const [systemReduced, setSystemReduced] = useState(
    () => matchMedia('(prefers-reduced-motion:reduce)').matches,
  );
  const [systemDark, setSystemDark] = useState(preferredSystemDark);
  const motionEnabled = pointer && state.preferences.motion && !systemReduced;
  const returnFocus = useRef<HTMLElement | null>(null);
  const dialogOpen = useRef(false);
  const dismissAction = useRef<DialogSpec['onDismiss']>(undefined);
  const nextId = useRef(0);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const update = useCallback<Update>((updater) => setState(updater), []);
  const record = useCallback((text: string, kind: ActivityKind = '提示') => {
    setState((current) =>
      current.activity.at(-1)?.text === text
        ? current
        : {
            ...current,
            activity: [
              ...current.activity,
              { id: ++nextId.current, text, kind, time: Date.now() },
            ].slice(-50),
          },
    );
  }, []);
  const notify = useCallback(
    (text: string, kind: ActivityKind = '提示') => {
      setToast(text);
      clearTimeout(toastTimer.current);
      toastTimer.current = setTimeout(() => setToast(''), 3000);
      record(text, kind);
    },
    [record],
  );
  const closeDialog = useCallback((restore = true) => {
    dismissAction.current?.();
    dismissAction.current = undefined;
    dialogOpen.current = false;
    if (!restore) returnFocus.current = null;
    setDialog((current) => (current ? { ...current, open: false } : null));
  }, []);
  const openDialog = useCallback((spec: DialogSpec) => {
    dismissAction.current?.();
    dismissAction.current = spec.onDismiss;
    if (!dialogOpen.current)
      returnFocus.current =
        document.activeElement instanceof HTMLElement ? document.activeElement : null;
    dialogOpen.current = true;
    setDialog({ ...spec, id: ++nextId.current, open: true });
  }, []);
  const navigate = useCallback(
    (page: Page) => {
      closeDialog(false);
      setNativeWorkspace(null);
      workspaceReturn.current = null;
      history.replaceState(null, '', '#' + page);
      setState((current) => ({ ...current, page }));
    },
    [closeDialog],
  );
  const changeWorkspace = useCallback(
    (next: NativeWorkspace | null) => {
      closeDialog(false);
      setNativeWorkspace(next);
      history.replaceState(null, '', '#' + state.page + (next ? '/' + next : ''));
    },
    [closeDialog, state.page],
  );
  const openWorkspace = useCallback(
    (next: NativeWorkspace) => {
      if (next === nativeWorkspace) {
        closeDialog(false);
        return;
      }
      workspaceReturn.current =
        next === 'downloads' &&
        (nativeWorkspace === 'steam-library' || isResourceWorkspace(nativeWorkspace))
          ? nativeWorkspace
          : null;
      changeWorkspace(next);
    },
    [nativeWorkspace, changeWorkspace, closeDialog],
  );
  const closeWorkspace = useCallback(() => {
    const next = workspaceReturn.current;
    workspaceReturn.current = null;
    changeWorkspace(next);
  }, [changeWorkspace]);
  const setPreferences = useCallback(
    (patch: Partial<Preferences>) =>
      setState((current) => ({
        ...current,
        preferences: { ...current.preferences, ...patch },
      })),
    [],
  );
  useEffect(() => {
    if (!nativeMode) return;
    const receive = (snapshot: NativeSnapshot) => {
      setNativeState(snapshot);
      if (snapshot.systemLocale) setAndroidLocale(snapshot.systemLocale);
      if (!nativeHydrated.current && snapshot.preferences) {
        nativeHydrated.current = true;
        setState((current) => ({
          ...current,
          preferences: {
            ...current.preferences,
            ...normalizePreferences(
              Object.fromEntries(
                Object.entries(snapshot.preferences).filter(([key]) =>
                  Object.hasOwn(defaultPreferences, key),
                ),
              ),
            ),
          },
        }));
      }
    };
    const unsubscribe = onNativeSnapshot(receive);
    void nativeRequest<NativeSnapshot>('state')
      .then(receive)
      .catch((error: Error) => setToast(error.message));
    return unsubscribe;
  }, []);
  useEffect(() => {
    if (!nativeMode || !nativeHydrated.current) return;
    const timer = setTimeout(() => {
      const { customImage: _image, ...preferences } = state.preferences;
      void nativeRequest('settings.save', { preferences }).catch((error: Error) =>
        setToast(error.message),
      );
    }, 200);
    return () => clearTimeout(timer);
  }, [state.preferences]);
  useEffect(() => {
    if (!nativeMode) return;
    const back = () => {
      const layoutEditor = document.querySelector<HTMLDialogElement>('#game-layout-editor[open]');
      if (layoutEditor) {
        layoutEditor.dispatchEvent(new Event('cancel', { cancelable: true }));
        return;
      }
      if (dialogOpen.current) closeDialog();
      else if (nativeWorkspace) closeWorkspace();
      else if (state.page !== 'home') navigate('home');
      else if (nativeState?.busy) {
        openDialog({
          title: '任务仍在进行',
          description: '关闭启动器会取消当前操作，请在下载与任务中先完成或取消。',
        });
      } else void nativeRequest('finish');
    };
    window.addEventListener('launcher:back', back);
    return () => window.removeEventListener('launcher:back', back);
  }, [
    closeDialog,
    navigate,
    state.page,
    nativeState?.busy,
    openDialog,
    nativeWorkspace,
    closeWorkspace,
  ]);
  useEffect(() => {
    if (!nativeWorkspace) return;
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && !dialogOpen.current) {
        event.preventDefault();
        closeWorkspace();
      }
    };
    window.addEventListener('keydown', escape);
    return () => window.removeEventListener('keydown', escape);
  }, [nativeWorkspace, closeWorkspace]);
  useLauncherOrientation(state.preferences.orientation, setPreferences);
  useEffect(() => {
    const down = (event: PointerEvent) => {
      if (!event.pointerType) return;
      setPointer(true);
      document.documentElement.dataset.input = 'pointer';
    };
    const key = () => {
      setPointer(false);
      document.documentElement.dataset.input = 'keyboard';
    };
    const media = matchMedia('(prefers-reduced-motion:reduce)');
    const reduce = () => setSystemReduced(media.matches);
    document.addEventListener('pointerdown', down, true);
    document.addEventListener('pointermove', down, { capture: true, passive: true });
    document.addEventListener('keydown', key, true);
    media.addEventListener('change', reduce);
    return () => {
      document.removeEventListener('pointerdown', down, true);
      document.removeEventListener('pointermove', down, true);
      document.removeEventListener('keydown', key, true);
      media.removeEventListener('change', reduce);
      clearTimeout(toastTimer.current);
    };
  }, []);
  useLayoutEffect(() => {
    const preferences = state.preferences;
    const classic = preferences.themeStyle === 'tml';
    const dark = preferences.theme === 'system' ? systemDark : preferences.theme === 'dark';
    const { dataset } = document.documentElement;
    dataset.theme = dark ? 'dark' : 'light';
    if (classic) dataset.skin = 'tml';
    else delete dataset.skin;
    document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute(
        'content',
        classic ? (dark ? '#131b37' : '#e4ecf8') : dark ? '#0c0f14' : '#f5f6f3',
      );
    dataset.motion = preferences.motion ? 'full' : 'reduced';
    document.body.classList.toggle('has-background', preferences.background !== 'none');
    if (preferences.background === 'diy')
      document.body.style.setProperty(
        '--custom-background',
        tintGradient(preferences.backgroundTint),
      );
    else if (preferences.background === 'custom' && preferences.customImage)
      document.body.style.setProperty('--custom-background', `url("${preferences.customImage}")`);
    else document.body.style.removeProperty('--custom-background');
    savePreferences(preferences);
  }, [state.preferences, systemDark]);
  useEffect(() => {
    const media = matchMedia('(prefers-color-scheme:dark)');
    const mediaChange = () => {
      if (!nativeMode) setSystemDark(media.matches);
    };
    const nativeChange = (event: Event) => {
      const detail = (event as CustomEvent<{ dark?: boolean; locale?: string }>).detail;
      if (typeof detail?.dark === 'boolean') setSystemDark(detail.dark);
      if (typeof detail?.locale === 'string') setAndroidLocale(detail.locale);
    };
    media.addEventListener('change', mediaChange);
    window.addEventListener('launcher:system-theme', nativeChange);
    return () => {
      media.removeEventListener('change', mediaChange);
      window.removeEventListener('launcher:system-theme', nativeChange);
    };
  }, []);
  useEffect(() => {
    if (!nativeMode || state.preferences.theme !== 'system') return;
    void nativeRequest<{ dark?: boolean }>('appearance.system')
      .then((result) => setSystemDark(Boolean(result?.dark)))
      .catch(() => {});
  }, [state.preferences]);
  const value = useMemo(
    () => ({
      nativeMode,
      nativeState,
      state,
      installed: state.mods.filter((mod) => state.demo || mod.installed),
      update,
      navigate,
      nativeWorkspace,
      openWorkspace,
      closeWorkspace,
      setPreferences,
      dialog,
      openDialog,
      closeDialog,
      returnFocus,
      toast,
      notify,
      record,
      motionEnabled,
    }),
    [
      nativeState,
      state,
      update,
      navigate,
      nativeWorkspace,
      openWorkspace,
      closeWorkspace,
      setPreferences,
      dialog,
      openDialog,
      closeDialog,
      toast,
      notify,
      record,
      motionEnabled,
    ],
  );
  return (
    <LauncherContext.Provider value={value}>
      <AnimateUIProvider
        tempo={motionEnabled ? 0.8 : 0}
        reducedMotion={motionEnabled ? 'never' : 'always'}
      >
        <MotionConfig reducedMotion={motionEnabled ? 'never' : 'always'}>
          <TranslationProvider locale={locale}>{children}</TranslationProvider>
        </MotionConfig>
      </AnimateUIProvider>
    </LauncherContext.Provider>
  );
}
export function useLauncher() {
  const context = useContext(LauncherContext);
  if (!context) throw new Error('LauncherProvider is required');
  return context;
}
