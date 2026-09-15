import { useCallback, useEffect, useState } from "react";
import { useAgentConnection } from "./hooks/useAgentConnection";
import { MasterIndicator } from "./components/MasterIndicator";
import { DeviceList } from "./components/DeviceList";
import { PlaybackControls } from "./components/PlaybackControls";
import { SyncPanel } from "./components/SyncPanel";
import { ErrorBanner } from "./components/ErrorBanner";
import { SettingsPanel } from "./components/SettingsPanel";
import { OnboardingOverlay } from "./components/OnboardingOverlay";

const ONBOARDING_KEY = "multiaudio-onboarding-seen";
const SETTINGS_KEY = "multiaudio-settings";

interface AppSettings {
  advancedByDefault: boolean;
}

function loadSettings(): AppSettings {
  try {
    const raw = localStorage.getItem(SETTINGS_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    /* ignore */
  }
  return { advancedByDefault: false };
}

export default function App() {
  const { status, devices, session, lastError, agentInfo, send, clearError } =
    useAgentConnection();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [sessionName, setSessionName] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [showOnboarding, setShowOnboarding] = useState(
    () => !localStorage.getItem(ONBOARDING_KEY)
  );
  const [settings, setSettings] = useState<AppSettings>(loadSettings);

  const toggleDevice = useCallback(
    (deviceId: string) => {
      setSelectedIds((prev) => {
        const next = new Set(prev);
        const wasSelected = next.has(deviceId);

        if (wasSelected) {
          next.delete(deviceId);
          send({ type: "DEVICE_DISCONNECT", payload: { deviceId } });
        } else {
          next.add(deviceId);
          send({ type: "DEVICE_CONNECT", payload: { deviceId } });
        }
        return next;
      });
    },
    [send]
  );

  const handleStartSession = () => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0) return;
    const name = `Session · ${ids.length} outputs`;
    setSessionName(name);
    send({
      type: "CREATE_SESSION",
      payload: { name, deviceIds: ids, audioSource: "system" },
    });
  };

  const openBluetoothSettings = useCallback(() => {
    send({ type: "OPEN_BLUETOOTH_SETTINGS", payload: {} });
  }, [send]);

  const dismissOnboarding = () => {
    localStorage.setItem(ONBOARDING_KEY, "1");
    setShowOnboarding(false);
  };

  const updateSettings = (patch: Partial<AppSettings>) => {
    setSettings((prev) => {
      const next = { ...prev, ...patch };
      localStorage.setItem(SETTINGS_KEY, JSON.stringify(next));
      return next;
    });
  };

  useEffect(() => {
    if (session) setSessionName((prev) => prev ?? "Active Session");
  }, [session]);

  const deviceNames = Object.fromEntries(devices.map((d) => [d.id, d.name]));
  const masterName = agentInfo.platform || "Windows PC";

  return (
    <>
      <div className="app-bg" aria-hidden="true">
        <div className="app-bg__gradient" />
        <div className="app-bg__orb app-bg__orb--1" />
        <div className="app-bg__orb app-bg__orb--2" />
        <div className="app-bg__orb app-bg__orb--3" />
      </div>

      {lastError && <ErrorBanner message={lastError} onDismiss={clearError} />}

      <div className="app">
        <aside className="app__sidebar">
          <MasterIndicator
            status={status}
            masterName={masterName}
            onOpenSettings={() => setShowSettings(true)}
            onOpenHelp={() => setShowOnboarding(true)}
          />
        </aside>

        <div className="app__content">
          <main className="app__main">
            <DeviceList
              devices={devices}
              selectedIds={selectedIds}
              onToggle={toggleDevice}
              onOpenBluetoothSettings={openBluetoothSettings}
            />

            <PlaybackControls
              playbackState={session?.playbackState ?? "stopped"}
              position={session?.position ?? 0}
              volume={session?.volume ?? 0.75}
              audioSource={session?.audioSource ?? "system"}
              sessionName={sessionName}
              hasSession={!!session}
              onPlay={() =>
                session &&
                send({
                  type: "PLAY",
                  sessionId: session.sessionId,
                  payload: {
                    position: session.position,
                    targetTimestamp: Date.now() + 200,
                  },
                })
              }
              onPause={() =>
                session &&
                send({ type: "PAUSE", sessionId: session.sessionId, payload: {} })
              }
              onStop={() =>
                session &&
                send({ type: "STOP", sessionId: session.sessionId, payload: {} })
              }
              onSeek={(position) =>
                session &&
                send({
                  type: "SEEK",
                  sessionId: session.sessionId,
                  payload: { position },
                })
              }
              onVolumeChange={(volume) =>
                session &&
                send({
                  type: "SET_VOLUME",
                  sessionId: session.sessionId,
                  payload: { volume },
                })
              }
            />

            <SyncPanel
              devices={session?.devices ?? []}
              deviceNames={deviceNames}
              selectedCount={selectedIds.size}
              hasSession={!!session}
              advancedByDefault={settings.advancedByDefault}
              onStart={handleStartSession}
            />
          </main>
        </div>
      </div>

      {showSettings && (
        <SettingsPanel
          agentVersion={agentInfo.agentVersion}
          platform={agentInfo.platform}
          advancedByDefault={settings.advancedByDefault}
          onAdvancedDefaultChange={(v) => updateSettings({ advancedByDefault: v })}
          onClose={() => setShowSettings(false)}
        />
      )}

      {showOnboarding && <OnboardingOverlay onDismiss={dismissOnboarding} />}
    </>
  );
}
