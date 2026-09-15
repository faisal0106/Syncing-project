import type { AudioOutputDevice } from "../types/protocol";
import { DeviceIcon } from "./Icons";

const STATE_LABEL: Record<AudioOutputDevice["state"], string> = {
  available: "Available",
  connecting: "Connecting…",
  connected: "Connected",
  playing: "Playing",
  paused: "Paused",
  disconnected: "Disconnected",
  unsupported: "Unsupported",
  error: "Error",
};

const STATE_CLASS: Record<AudioOutputDevice["state"], string> = {
  available: "state-neutral",
  connecting: "state-warning state-dot--pulse",
  connected: "state-success",
  playing: "state-success state-dot--pulse",
  paused: "state-success",
  disconnected: "state-neutral",
  unsupported: "state-error",
  error: "state-error state-dot--pulse",
};

interface Props {
  device: AudioOutputDevice;
  selected: boolean;
  onToggle: (deviceId: string) => void;
  onOpenBluetoothSettings: () => void;
}

export function DeviceCard({ device, selected, onToggle, onOpenBluetoothSettings }: Props) {
  const disabled = device.state === "unsupported" || device.state === "disconnected";

  return (
    <label
      className={`device-card ${selected ? "device-card--selected" : ""} ${disabled ? "device-card--disabled" : ""}`}
    >
      <input
        type="checkbox"
        className="device-card__checkbox"
        checked={selected}
        disabled={disabled}
        onChange={() => onToggle(device.id)}
      />
      <div className="device-card__icon">
        <DeviceIcon type={device.type} />
      </div>
      <div className="device-card__body">
        <div className="device-card__row">
          <span className="device-card__name">{device.name}</span>
          <span className="device-card__type">{device.type}</span>
        </div>
        <div className="device-card__row" style={{ marginTop: 4 }}>
          <span className={`state-dot ${STATE_CLASS[device.state]}`} />
          <span className="device-card__state">{STATE_LABEL[device.state]}</span>
        </div>
        {device.latencyMs !== null && (
          <div className="device-card__latency">Latency: {device.latencyMs} ms</div>
        )}
        {device.state === "disconnected" && (
          <button
            type="button"
            className="glass-button glass-button--sm"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onOpenBluetoothSettings();
            }}
          >
            Open Bluetooth settings
          </button>
        )}
      </div>
    </label>
  );
}
