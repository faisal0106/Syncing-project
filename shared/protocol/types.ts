/**
 * MultiAudio Control Protocol — TypeScript types (web side mirror).
 * Source of truth: shared/protocol/PROTOCOL.md
 * Keep this in sync with agent/MultiAudio.Agent/Protocol.cs
 */

// ---------- Domain types ----------

export type DeviceType = "earbuds" | "headphones" | "speaker" | "unknown";

export type DeviceState =
  | "available"
  | "connecting"
  | "connected"
  | "playing"
  | "paused"
  | "disconnected"
  | "unsupported"
  | "error";

export interface AudioOutputDevice {
  id: string;
  name: string;
  type: DeviceType;
  state: DeviceState;
  capabilities: string[]; // e.g. ["classic-bt", "le-audio"]
  latencyMs: number | null;
}

export type SyncState = "synced" | "syncing" | "degraded";

export interface DeviceSyncInfo {
  deviceId: string;
  measuredLatencyMs: number;
  bufferDepthMs: number;
  clockOffsetMs: number;
  driftEstimateMsPerSec: number;
  syncState: SyncState;
}

export type PlaybackState = "stopped" | "playing" | "paused";

export type AudioSourceType = "system" | "file" | "mic" | "tone";

export interface SessionState {
  sessionId: string;
  playbackState: PlaybackState;
  position: number; // seconds
  volume: number; // 0..1
  audioSource?: AudioSourceType;
  devices: DeviceSyncInfo[];
}

// ---------- Error codes ----------

export type ErrorCode =
  | "DEVICE_NOT_FOUND"
  | "DEVICE_UNSUPPORTED"
  | "DEVICE_CONNECT_FAILED"
  | "AUDIO_INIT_FAILED"
  | "PERMISSION_DENIED"
  | "UNAUTHORIZED"
  | "SESSION_NOT_FOUND"
  | "INVALID_MESSAGE";

// ---------- Client -> Agent messages ----------

export type ClientMessage =
  | { type: "HELLO"; payload: { token: string; clientVersion: string } }
  | { type: "DEVICE_LIST"; payload: Record<string, never> }
  | { type: "DEVICE_CONNECT"; payload: { deviceId: string } }
  | { type: "DEVICE_DISCONNECT"; payload: { deviceId: string } }
  | { type: "OPEN_BLUETOOTH_SETTINGS"; payload: Record<string, never> }
  | {
      type: "CREATE_SESSION";
      payload: { name: string; deviceIds: string[]; audioSource?: AudioSourceType };
    }
  | {
      type: "PLAY";
      sessionId: string;
      payload: { position: number; targetTimestamp: number };
    }
  | { type: "PAUSE"; sessionId: string; payload: Record<string, never> }
  | { type: "STOP"; sessionId: string; payload: Record<string, never> }
  | { type: "SEEK"; sessionId: string; payload: { position: number } }
  | {
      type: "SET_VOLUME";
      sessionId: string;
      payload: { volume: number };
    }
  | {
      type: "SET_DEVICE_ENABLED";
      sessionId: string;
      payload: { deviceId: string; enabled: boolean };
    }
  | {
      type: "SET_AUDIO_SOURCE";
      sessionId: string;
      payload: { source: AudioSourceType; filePath?: string };
    }
  | { type: "GET_STATUS"; payload: Record<string, never> };

// ---------- Agent -> Client messages ----------

export type AgentMessage =
  | {
      type: "HELLO_ACK";
      payload: { agentVersion: string; platform: string };
    }
  | { type: "DEVICE_LIST_RESULT"; payload: { devices: AudioOutputDevice[] } }
  | {
      type: "DEVICE_STATE";
      payload: { deviceId: string; state: DeviceState };
    }
  | { type: "SESSION_STATE"; payload: SessionState }
  | { type: "SYNC"; payload: { sessionId: string; devices: DeviceSyncInfo[] } }
  | {
      type: "STATUS";
      payload: {
        agentVersion: string;
        uptimeSeconds: number;
        activeSessionId?: string;
      };
    }
  | {
      type: "ERROR";
      payload: { code: ErrorCode; message: string; deviceId?: string };
    };

// Common envelope fields present on every message.
export interface Envelope {
  requestId?: string;
}
