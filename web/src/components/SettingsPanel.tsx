import { CloseIcon } from "./Icons";

interface Props {
  agentVersion: string;
  platform: string;
  advancedByDefault: boolean;
  onAdvancedDefaultChange: (value: boolean) => void;
  onClose: () => void;
}

export function SettingsPanel({
  agentVersion,
  platform,
  advancedByDefault,
  onAdvancedDefaultChange,
  onClose,
}: Props) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal panel" onClick={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <h2 className="modal__title">Settings</h2>
          <button className="modal__close" onClick={onClose} aria-label="Close settings">
            <CloseIcon />
          </button>
        </div>

        <div className="settings-panel">
          <div className="settings-item">
            <div>
              <div className="settings-item__label">Advanced sync details</div>
              <div className="settings-item__desc">Show latency, buffer, and drift by default</div>
            </div>
            <input
              type="checkbox"
              className="settings-toggle"
              checked={advancedByDefault}
              onChange={(e) => onAdvancedDefaultChange(e.target.checked)}
            />
          </div>

          <div className="settings-info">
            <strong>Agent</strong> v{agentVersion} · {platform}
            <br />
            <br />
            MultiAudio routes audio through a native agent on this machine. The web UI is a control
            interface only — Bluetooth and audio privileges remain with the agent process.
          </div>
        </div>
      </div>
    </div>
  );
}
