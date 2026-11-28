import type { Preferences, Tool } from '../domain/types';
import { defaultBackgroundTint } from '../domain/background-tint';
import { nativeMode } from '../native/bridge';
import { isLanguagePreference } from '../i18n';

export const preferenceKey = 'tml-launcher-ui-v1';
export const toolIds: Tool[] = [
  'mods',
  'browse',
  'packs',
  'patches',
  'controls',
  'storage',
  'tasks',
  'accounts',
  'logs',
  'activity',
];
export const defaultPreferences: Preferences = {
  language: 'system',
  theme: 'system',
  themeStyle: 'tml',
  orientation: 'landscape',
  motion: true,
  background: 'none',
  backgroundTint: defaultBackgroundTint,
  quickTools: ['mods', 'patches', 'controls', 'storage'],
};
export function readPreferences(): Preferences {
  const result = { ...defaultPreferences, quickTools: [...defaultPreferences.quickTools] };
  try {
    const saved = JSON.parse(localStorage.getItem(preferenceKey) || 'null') as Record<
      string,
      unknown
    > | null;
    if (saved?.version !== 1) return result;
    if (isLanguagePreference(saved.language)) result.language = saved.language;
    if (['dark', 'light', 'system', 'tml'].includes(String(saved.theme)))
      result.theme = saved.theme as Preferences['theme'];
    if (saved.theme === 'tml') {
      result.theme = 'dark';
      result.themeStyle = 'tml';
    }
    if (saved.themeStyle === 'default' || saved.themeStyle === 'tml')
      result.themeStyle = saved.themeStyle;
    if (saved.orientation === 'landscape' || saved.orientation === 'portrait')
      result.orientation = saved.orientation;
    if (typeof saved.motion === 'boolean') result.motion = saved.motion;
    if (saved.background === 'soft') result.background = 'diy';
    if (saved.background === 'none' || saved.background === 'diy')
      result.background = saved.background;
    if (typeof saved.backgroundTint === 'string' && /^#[0-9a-f]{6}$/i.test(saved.backgroundTint))
      result.backgroundTint = saved.backgroundTint;
    if (
      nativeMode &&
      saved.background === 'custom' &&
      typeof saved.customImage === 'string' &&
      saved.customImage.length <= 2 * 1024 * 1024 &&
      /^data:image\/(jpeg|png|webp);base64,/.test(saved.customImage)
    ) {
      result.background = 'custom';
      result.customImage = saved.customImage;
    }
    if (
      Array.isArray(saved.quickTools) &&
      saved.quickTools.length === 4 &&
      new Set(saved.quickTools).size === 4 &&
      saved.quickTools.every((id) => toolIds.includes(id as Tool))
    )
      result.quickTools = saved.quickTools as Tool[];
  } catch {}
  return result;
}
export function normalizePreferences(patch: Partial<Preferences>): Partial<Preferences> {
  const result = { ...patch };
  if (!isLanguagePreference(result.language)) delete result.language;
  if ((result.background as string | undefined) === 'soft') result.background = 'diy';
  return result;
}
export function savePreferences(preferences: Preferences) {
  const { customImage, ...safe } = preferences;
  const image =
    nativeMode && customImage && customImage.length <= 2 * 1024 * 1024 ? customImage : undefined;
  try {
    localStorage.setItem(
      preferenceKey,
      JSON.stringify({
        ...safe,
        version: 1,
        background: safe.background === 'custom' && !image ? 'none' : safe.background,
        customImage: image,
      }),
    );
  } catch {}
}
