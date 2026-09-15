import type { AudioOutputDevice } from "../types/protocol";
import { DeviceCard } from "./DeviceCard";

interface Props {
  devices: AudioOutputDevice[];
  selectedIds: Set<string>;
  onToggle: (deviceId: string) => void;
  onOpenBluetoothSettings: () => void;
}

export function DeviceList({ devices, selectedIds, onToggle, onOpenBluetoothSettings }: Props) {
  const connectedCount = devices.filter(
    (d) => d.state === "connected" || d.state === "playing" || d.state === "paused"
  ).length;

  return (
    <section className="panel panel--devices">
      <div className="panel__header">
        <h2 className="panel__title panel__title--large">Audio Outputs</h2>
        <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
          {connectedCount}/{devices.length} connected
        </span>
      </div>
      {devices.length === 0 ? (
        <p className="empty-state">
          No devices found. Make sure the MultiAudio Agent is running and Bluetooth devices are paired.
        </p>
      ) : (
        <div className="device-list">
          {devices.map((d) => (
            <DeviceCard
              key={d.id}
              device={d}
              selected={selectedIds.has(d.id)}
              onToggle={onToggle}
              onOpenBluetoothSettings={onOpenBluetoothSettings}
            />
          ))}
        </div>
      )}
    </section>
  );
}
