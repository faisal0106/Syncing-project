import type { AgentConnectionStatus } from "../hooks/useAgentConnection";
import { LogoIcon } from "./Icons";

interface Props {
  status: AgentConnectionStatus;
  masterName: string;
  onOpenSettings: () => void;
  onOpenHelp: () => void;
}

export function MasterIndicator({ status, masterName, onOpenSettings, onOpenHelp }: Props) {
  const statusClass =
    status === "connected"
      ? "connection-status connection-status--connected"
      : status === "error"
        ? "connection-status connection-status--error"
        : "connection-status";

  const statusLabel =
    status === "connected"
      ? "Connected"
      : status === "connecting"
        ? "Connecting…"
        : status === "error"
          ? "Agent unavailable"
          : "Disconnected";

  return (
    <header className="app-header glass" style={{ padding: "20px" }}>
      <div className="app-header__brand">
        <div className="app-header__logo">
          <LogoIcon />
        </div>
        <div>
          <div className="app-header__title">MultiAudio</div>
          <div className="app-header__subtitle">Multi-Bluetooth Sync</div>
        </div>
      </div>

      <div className="master-badge">
        <span className="master-badge__label">Master Device</span>
        <span className="master-badge__name">{masterName}</span>
      </div>

      <div className={statusClass}>
        <span
          className={`state-dot ${status === "connected" ? "state-success" : status === "error" ? "state-error state-dot--pulse" : "state-neutral"}`}
        />
        {statusLabel}
      </div>

      <nav className="sidebar-nav">
        <button className="sidebar-nav__item" onClick={onOpenSettings}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 01-2.83 2.83l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z" />
          </svg>
          Settings
        </button>
        <button className="sidebar-nav__item" onClick={onOpenHelp}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="12" r="10" />
            <path d="M9.09 9a3 3 0 015.83 1c0 2-3 3-3 3M12 17h.01" />
          </svg>
          Quick Start Guide
        </button>
      </nav>
    </header>
  );
}
