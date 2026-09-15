import { useEffect, useRef, useState, useCallback } from "react";
import type {
  AgentMessage,
  AudioOutputDevice,
  AudioSourceType,
  ClientMessage,
  SessionState,
} from "../types/protocol";

const AGENT_HOST = "127.0.0.1:8787";
const AGENT_WS_URL = `ws://${AGENT_HOST}`;
const PAIRING_TOKEN_URL = `http://${AGENT_HOST}/pairing-token`;
const CLIENT_VERSION = "0.1.0";
const RECONNECT_DELAY_MS = 3000;

export type AgentConnectionStatus =
  | "disconnected"
  | "connecting"
  | "connected"
  | "error";

export interface AgentInfo {
  agentVersion: string;
  platform: string;
}

export function useAgentConnection() {
  const wsRef = useRef<WebSocket | null>(null);
  const reconnectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const mountedRef = useRef(true);
  const [status, setStatus] = useState<AgentConnectionStatus>("disconnected");
  const [devices, setDevices] = useState<AudioOutputDevice[]>([]);
  const [session, setSession] = useState<SessionState | null>(null);
  const [lastError, setLastError] = useState<string | null>(null);
  const [agentInfo, setAgentInfo] = useState<AgentInfo>({
    agentVersion: "—",
    platform: "Windows PC",
  });

  const send = useCallback((message: ClientMessage) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(message));
    }
  }, []);

  const clearError = useCallback(() => setLastError(null), []);

  const connect = useCallback(() => {
    if (reconnectTimer.current) {
      clearTimeout(reconnectTimer.current);
      reconnectTimer.current = null;
    }

    wsRef.current?.close();
    wsRef.current = null;
    setStatus("connecting");

    (async () => {
      let token: string;
      try {
        const res = await fetch(PAIRING_TOKEN_URL);
        if (!res.ok) throw new Error(`pairing-token request failed: ${res.status}`);
        ({ token } = await res.json());
      } catch (err) {
        if (!mountedRef.current) return;
        setStatus("error");
        setLastError(
          err instanceof Error
            ? `Could not reach MultiAudio Agent: ${err.message}`
            : "Could not reach MultiAudio Agent. Make sure the agent is running."
        );
        reconnectTimer.current = setTimeout(() => {
          if (mountedRef.current) connect();
        }, RECONNECT_DELAY_MS);
        return;
      }

      if (!mountedRef.current) return;

      const ws = new WebSocket(AGENT_WS_URL);
      wsRef.current = ws;

      ws.onopen = () => {
        send({
          type: "HELLO",
          payload: { token, clientVersion: CLIENT_VERSION },
        });
      };

      ws.onmessage = (event) => {
        const msg: AgentMessage = JSON.parse(event.data);
        switch (msg.type) {
          case "HELLO_ACK":
            setStatus("connected");
            setAgentInfo({
              agentVersion: msg.payload.agentVersion,
              platform: msg.payload.platform,
            });
            send({ type: "DEVICE_LIST", payload: {} });
            send({ type: "GET_STATUS", payload: {} });
            break;
          case "DEVICE_LIST_RESULT":
            setDevices(msg.payload.devices);
            break;
          case "DEVICE_STATE":
            setDevices((prev) =>
              prev.map((d) =>
                d.id === msg.payload.deviceId
                  ? { ...d, state: msg.payload.state }
                  : d
              )
            );
            break;
          case "SESSION_STATE":
            setSession(msg.payload);
            break;
          case "SYNC":
            setSession((prev) =>
              prev && prev.sessionId === msg.payload.sessionId
                ? { ...prev, devices: msg.payload.devices }
                : prev
            );
            break;
          case "STATUS":
            setAgentInfo((prev) => ({
              ...prev,
              agentVersion: msg.payload.agentVersion,
            }));
            break;
          case "ERROR":
            setLastError(msg.payload.message);
            break;
        }
      };

      ws.onerror = () => {
        if (mountedRef.current) setStatus("error");
      };

      ws.onclose = () => {
        if (!mountedRef.current) return;
        setStatus("disconnected");
        reconnectTimer.current = setTimeout(() => {
          if (mountedRef.current) connect();
        }, RECONNECT_DELAY_MS);
      };
    })();
  }, [send]);

  useEffect(() => {
    mountedRef.current = true;
    connect();

    return () => {
      mountedRef.current = false;
      if (reconnectTimer.current) clearTimeout(reconnectTimer.current);
      wsRef.current?.close();
      wsRef.current = null;
    };
  }, [connect]);

  const setAudioSource = useCallback(
    (source: AudioSourceType, filePath?: string) => {
      if (session) {
        send({
          type: "SET_AUDIO_SOURCE",
          sessionId: session.sessionId,
          payload: { source, filePath },
        });
      }
    },
    [session, send]
  );

  const refreshDevices = useCallback(() => {
    send({ type: "DEVICE_LIST", payload: {} });
  }, [send]);

  return {
    status,
    devices,
    session,
    lastError,
    agentInfo,
    send,
    clearError,
    reconnect: connect,
    setAudioSource,
    refreshDevices,
  };
}
