import { useEffect, useState } from 'react';
import { nativeRequest } from '../../native/bridge';
import type { NativeSnapshot } from '../../native/models';

export type ControlArtworkMap = Record<string, string>;
const empty: ControlArtworkMap = {};
let cache: { key: string; request: Promise<ControlArtworkMap> } | undefined;

function preload(key: string) {
  if (cache?.key === key) return cache.request;
  const request = nativeRequest<ControlArtworkMap>('controls.art').then((textures) => {
    if (cache?.request === request) {
      for (const src of new Set(Object.values(textures))) {
        const image = new Image();
        image.src = src;
        void image.decode().catch(() => {});
      }
    }
    return textures;
  });
  cache = { key, request };
  void request.catch(() => {
    if (cache?.request === request) cache = undefined;
  });
  return request;
}

export function useControlArtwork(snapshot: NativeSnapshot | null, editing: boolean) {
  const key = snapshot && JSON.stringify([
    snapshot.activeInstance, snapshot.packageRevision, snapshot.terrariaSource, snapshot.tml,
    snapshot.ready, snapshot.storageGranted,
  ]);
  const [loaded, setLoaded] = useState<{ key: string; textures: ControlArtworkMap }>();
  useEffect(() => {
    if (!key) return;
    let active = true;
    void preload(key).then((textures) => {
      if (active) setLoaded({ key, textures });
    }, () => {});
    return () => { active = false; };
    // Opening the editor retries a failed prefetch and joins an in-flight one.
  }, [key, editing]);
  return loaded && loaded.key === key ? loaded.textures : empty;
}
