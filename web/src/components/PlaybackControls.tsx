import { useEffect, useRef, useState } from "react";
import type { AudioSourceType, PlaybackState } from "../types/protocol";
import { formatTime } from "../utils/formatTime";
import {
  MusicIcon,
  PauseIcon,
  PlayIcon,
  SkipBackIcon,
  StopIcon,
  VolumeIcon,
  VolumeMuteIcon,
} from "./Icons";

interface Props {
  playbackState: PlaybackState;
  position: number;
  volume: number;
  audioSource: AudioSourceType;
  sessionName: string | null;
  hasSession: boolean;
  onPlay: () => void;
  onPause: () => void;
  onStop: () => void;
  onSeek: (position: number) => void;
  onVolumeChange: (volume: number) => void;
}

export function PlaybackControls({
  playbackState,
  position,
  volume,
  audioSource,
  sessionName,
  hasSession,
  onPlay,
  onPause,
  onStop,
  onSeek,
  onVolumeChange,
}: Props) {
  const [muted, setMuted] = useState(false);
  const [prevVolume, setPrevVolume] = useState(volume);
  const [displayPosition, setDisplayPosition] = useState(position);
  const tickRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    setDisplayPosition(position);
  }, [position]);

  useEffect(() => {
    if (playbackState !== "playing") {
      if (tickRef.current) clearInterval(tickRef.current);
      return;
    }

    tickRef.current = setInterval(() => {
      setDisplayPosition((p) => p + 1);
    }, 1000);
    return () => {
      if (tickRef.current) clearInterval(tickRef.current);
    };
  }, [playbackState]);

  const toggleMute = () => {
    if (muted) {
      onVolumeChange(prevVolume);
      setMuted(false);
    } else {
      setPrevVolume(volume);
      onVolumeChange(0);
      setMuted(true);
    }
  };

  const isLive = audioSource === "system" || audioSource === "mic";
  const sourceLabel = audioSource === "system"
    ? "Host system audio"
    : audioSource === "mic"
      ? "Host microphone"
      : audioSource === "file"
        ? "Audio file"
        : "Test tone";

  return (
    <section className="panel panel--playback">
      <h2 className="panel__title">Now Playing</h2>

      <div className="now-playing">
        <div className="now-playing__art">
          <MusicIcon />
        </div>
        <p className="now-playing__title">
          {sessionName ?? (hasSession ? "Ready to Play" : "No Active Session")}
        </p>
        <p className="now-playing__subtitle">
          {playbackState === "playing"
            ? `${sourceLabel} → all outputs`
            : playbackState === "paused"
              ? "Paused"
              : hasSession
                ? sourceLabel
                : "Select devices and start a session"}
        </p>
      </div>

      <div className="seek-bar">
        <div className="seek-bar__times">
          <span>{formatTime(displayPosition)}</span>
          <span>{isLive ? "LIVE" : ""}</span>
        </div>
        <div className="seek-bar__track">
          <div className="seek-bar__fill" style={{ width: isLive ? "100%" : "0%" }} />
          <input
            className="seek-bar__input"
            type="range"
            min={0}
            max={isLive ? Math.max(displayPosition + 1, 1) : 300}
            step={1}
            value={displayPosition}
            disabled={!hasSession || isLive}
            onChange={(e) => {
              const pos = Number(e.target.value);
              setDisplayPosition(pos);
              onSeek(pos);
            }}
            aria-label="Seek position"
          />
        </div>
      </div>

      <div className="playback-controls">
        <button
          className="glass-button glass-button--sm"
          onClick={() => onSeek(0)}
          disabled={!hasSession || isLive}
          aria-label="Restart"
        >
          <SkipBackIcon />
        </button>
        {playbackState === "playing" ? (
          <button
            className="glass-button glass-button--lg"
            onClick={onPause}
            disabled={!hasSession}
            aria-label="Pause"
          >
            <PauseIcon />
          </button>
        ) : (
          <button
            className="glass-button glass-button--lg"
            onClick={onPlay}
            disabled={!hasSession}
            aria-label="Play"
          >
            <PlayIcon />
          </button>
        )}
        <button
          className="glass-button glass-button--sm"
          onClick={onStop}
          disabled={!hasSession}
          aria-label="Stop"
        >
          <StopIcon />
        </button>
      </div>

      <div className="volume-row">
        <button
          className="volume-row__icon"
          onClick={toggleMute}
          disabled={!hasSession}
          aria-label={muted ? "Unmute" : "Mute"}
          style={{ background: "none", border: "none", padding: 0 }}
        >
          {muted || volume === 0 ? <VolumeMuteIcon /> : <VolumeIcon />}
        </button>
        <input
          id="volume"
          type="range"
          min={0}
          max={1}
          step={0.01}
          value={volume}
          disabled={!hasSession}
          onChange={(e) => {
            const v = Number(e.target.value);
            onVolumeChange(v);
            setMuted(v === 0);
          }}
          aria-label="Volume"
        />
        <span style={{ fontSize: "0.75rem", color: "var(--text-muted)", width: 32, textAlign: "right" }}>
          {Math.round(volume * 100)}
        </span>
      </div>
    </section>
  );
}
