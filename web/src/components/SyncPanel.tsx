import { useState } from "react";
import type { DeviceSyncInfo } from "../types/protocol";
import { CheckIcon, SyncIcon } from "./Icons";

interface Props {
  devices: DeviceSyncInfo[];
  deviceNames: Record<string, string>;
  selectedCount: number;
  hasSession: boolean;
  advancedByDefault: boolean;
  onStart: () => void;
}

export function SyncPanel({
  devices,
  deviceNames,
  selectedCount,
  hasSession,
  advancedByDefault,
  onStart,
}: Props) {
  const [advanced, setAdvanced] = useState(advancedByDefault);
  const allSynced = devices.length > 0 && devices.every((d) => d.syncState === "synced");
  const canStart = selectedCount > 0 && !hasSession;

  return (
    <section className="panel panel--sync">
      <div className="panel__header">
        <h2 className="panel__title panel__title--large">Synchronization</h2>
        <button className="link-button" onClick={() => setAdvanced((a) => !a)}>
          {advanced ? "Simple view" : "Advanced"}
        </button>
      </div>

      {!advanced ? (
        <div className={`sync-summary ${allSynced ? "sync-summary--ok" : ""}`}>
          {allSynced ? (
            <>
              <CheckIcon className="sync-summary__icon" />
              Synchronized across {devices.length} device{devices.length !== 1 ? "s" : ""}
            </>
          ) : hasSession ? (
            <>
              <SyncIcon className="sync-summary__icon" />
              Synchronizing outputs…
            </>
          ) : (
            <>
              <SyncIcon className="sync-summary__icon" />
              Select devices and start a session to synchronize
            </>
          )}
        </div>
      ) : (
        <ul className="sync-detail-list">
          {devices.length === 0 ? (
            <li className="sync-detail-item" style={{ textAlign: "center" }}>
              No sync data yet
            </li>
          ) : (
            devices.map((d) => (
              <li className="sync-detail-item" key={d.deviceId}>
                <strong>{deviceNames[d.deviceId] ?? d.deviceId}</strong>
                <div className="sync-detail-item__metrics">
                  <div className="sync-detail-item__metric">
                    <div className="sync-detail-item__metric-value">{d.measuredLatencyMs} ms</div>
                    <div className="sync-detail-item__metric-label">Latency</div>
                  </div>
                  <div className="sync-detail-item__metric">
                    <div className="sync-detail-item__metric-value">{d.bufferDepthMs} ms</div>
                    <div className="sync-detail-item__metric-label">Buffer</div>
                  </div>
                  <div className="sync-detail-item__metric">
                    <div className="sync-detail-item__metric-value">
                      {d.driftEstimateMsPerSec >= 0 ? "+" : ""}
                      {d.driftEstimateMsPerSec}
                    </div>
                    <div className="sync-detail-item__metric-label">Drift ms/s</div>
                  </div>
                </div>
              </li>
            ))
          )}
        </ul>
      )}

      <button className="button-primary" onClick={onStart} disabled={!canStart}>
        {hasSession ? "Session Active" : `Start Session (${selectedCount} device${selectedCount !== 1 ? "s" : ""})`}
      </button>
    </section>
  );
}
