import type { LanguagePreference } from '../i18n';

export type Page = 'home' | 'games' | 'mods' | 'patches' | 'settings';
export type Profile = 'stable' | 'preview';
export type ModTab = 'installed' | 'browse' | 'packs';
export type GameTab = 'installed' | 'sources' | 'accounts';
export type SettingsTab =
  | 'appearance'
  | 'runtime'
  | 'controls'
  | 'control-management'
  | 'storage'
  | 'about';
export type ActivityKind = '配置' | '任务' | '提示';
export type Tool =
  | 'mods'
  | 'browse'
  | 'packs'
  | 'patches'
  | 'controls'
  | 'storage'
  | 'tasks'
  | 'accounts'
  | 'logs'
  | 'activity';
export interface Preferences {
  language: LanguagePreference;
  theme: 'dark' | 'light' | 'system' | 'tml';
  themeStyle: 'default' | 'tml';
  orientation: 'landscape' | 'portrait';
  motion: boolean;
  background: 'none' | 'diy' | 'custom';
  backgroundTint: string;
  customImage?: string;
  quickTools: Tool[];
}
export interface Mod {
  id: string;
  title: string;
  author: string;
  description: string;
  version: string;
  kind: string;
  art: string;
  deps: string[];
  side: 'both' | 'client' | 'server';
  channel: Profile;
  size: number;
  hot: number;
  days: number;
  enabled: boolean;
  installed: boolean;
  changed?: number;
}
export interface ModFilters {
  query: string;
  searchBy: 'name' | 'author';
  sort: 'hot' | 'name' | 'recent';
  status: 'all' | 'updates' | 'installed' | 'not-installed' | 'compatible';
  tag: string;
  side: string;
  period: string;
}
export interface ModPack {
  id: string;
  name: string;
  profile: Profile;
  mods: string[];
}
export interface Task {
  kind: 'terraria' | 'tml' | 'mods';
  paused: boolean;
  ids: string[];
}
export interface Activity {
  id: number;
  text: string;
  kind: ActivityKind;
  time: number;
}
export interface LauncherState {
  page: Page;
  demo: boolean;
  profile: Profile;
  gameTab: GameTab;
  modTab: ModTab;
  patchTab: 'builtin' | 'optional';
  settingsTab: SettingsTab;
  localQuery: string;
  localFilter: 'all' | 'enabled' | 'disabled';
  localSort: 'name' | 'size' | 'enabled' | 'updated';
  filters: ModFilters;
  mods: Mod[];
  packs: ModPack[];
  configs: Record<string, string>;
  preferences: Preferences;
  patchEnabled: boolean;
  task: Task | null;
  activity: Activity[];
}
