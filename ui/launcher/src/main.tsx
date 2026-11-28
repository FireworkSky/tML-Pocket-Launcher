import { createRoot } from 'react-dom/client';
import { App } from './app';
import { LauncherProvider } from './state/launcher';
import { TooltipProvider } from './components/tooltip';
import './styles/styles.css';
import './styles/launcher-layout.css';
import './styles/liquid-glass.css';
import './styles/mobile-workspace.css';
import './styles/mods-workspace.css';
import './styles/launcher-polish.css';
import './styles/animate-ui.css';
import './styles/primitive-controls.css';
import './styles/navigation.css';
import './styles/native-workspace.css';
import './styles/controls-editor.css';
import './styles/steam-workspace.css';
import './styles/file-browser.css';
import './styles/launch-transition.css';
import './styles/resource-browser.css';
import './styles/library-workspace.css';
import './styles/tml-theme.css';
import { SteamProvider } from './features/native/steam/state';
import { LaunchTransitionProvider } from './features/launch/transition';
import { DownloadsProvider } from './features/native/download-state';
import { LauncherNoticeGate } from './features/launcher-notice';

createRoot(document.getElementById('root')!).render(
  <LauncherNoticeGate>
    <LauncherProvider>
      <TooltipProvider delayDuration={400} skipDelayDuration={300}>
        <DownloadsProvider>
          <SteamProvider>
            <LaunchTransitionProvider>
              <App />
            </LaunchTransitionProvider>
          </SteamProvider>
        </DownloadsProvider>
      </TooltipProvider>
    </LauncherProvider>
  </LauncherNoticeGate>,
);
