// MultiAudio Control Protocol — C# types (agent side mirror).
// Source of truth: shared/protocol/PROTOCOL.md
// Keep this in sync with shared/protocol/types.ts

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiAudio.Agent.Protocol
{
    /// <summary>
    /// All the string enums in PROTOCOL.md are single lowercase words on
    /// the wire (e.g. "available", "connecting"). CamelCase naming policy
    /// applied to a single-word PascalCase member name just lowercases
    /// the first letter, which is exactly the mapping we need. ErrorCode
    /// is the exception — its members are already UPPER_SNAKE_CASE and
    /// must serialize verbatim (see the plain JsonStringEnumConverter
    /// registered globally in ControlServer's JsonSerializerOptions).
    /// </summary>
    public class LowercaseEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
    {
        public LowercaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
    }

    [JsonConverter(typeof(LowercaseEnumConverter<DeviceType>))]
    public enum DeviceType
    {
        Earbuds,
        Headphones,
        Speaker,
        Unknown
    }

    [JsonConverter(typeof(LowercaseEnumConverter<DeviceState>))]
    public enum DeviceState
    {
        Available,
        Connecting,
        Connected,
        Playing,
        Paused,
        Disconnected,
        Unsupported,
        Error
    }

    [JsonConverter(typeof(LowercaseEnumConverter<SyncState>))]
    public enum SyncState
    {
        Synced,
        Syncing,
        Degraded
    }

    [JsonConverter(typeof(LowercaseEnumConverter<PlaybackState>))]
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    [JsonConverter(typeof(LowercaseEnumConverter<AudioSourceType>))]
    public enum AudioSourceType
    {
        System,
        File,
        Mic,
        Tone
    }

    public class AudioOutputDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DeviceType Type { get; set; }
        public DeviceState State { get; set; }
        public List<string> Capabilities { get; set; } = new();
        public double? LatencyMs { get; set; }
    }

    public class DeviceSyncInfo
    {
        public string DeviceId { get; set; } = "";
        public double MeasuredLatencyMs { get; set; }
        public double BufferDepthMs { get; set; }
        public double ClockOffsetMs { get; set; }
        public double DriftEstimateMsPerSec { get; set; }
        public SyncState SyncState { get; set; }
    }

    public class SessionState
    {
        public string SessionId { get; set; } = "";
        public PlaybackState PlaybackState { get; set; }
        public double Position { get; set; }
        public double Volume { get; set; }
        public AudioSourceType AudioSource { get; set; } = AudioSourceType.System;
        public List<DeviceSyncInfo> Devices { get; set; } = new();
    }

    /// <summary>
    /// Explicit (non-lowercased) converter so this enum serializes as
    /// its exact UPPER_SNAKE_CASE member names, matching PROTOCOL.md
    /// and types.ts. Needed as an attribute rather than a global
    /// options-level converter: System.Text.Json checks
    /// JsonSerializerOptions.Converters *before* a type's own
    /// [JsonConverter] attribute, so a catch-all converter registered
    /// on JsonSerializerOptions would otherwise shadow every other
    /// enum's [JsonConverter(LowercaseEnumConverter&lt;T&gt;)] above —
    /// which is exactly what happened until this was made explicit.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ErrorCode>))]
    public enum ErrorCode
    {
        DEVICE_NOT_FOUND,
        DEVICE_UNSUPPORTED,
        DEVICE_CONNECT_FAILED,
        AUDIO_INIT_FAILED,
        PERMISSION_DENIED,
        UNAUTHORIZED,
        SESSION_NOT_FOUND,
        INVALID_MESSAGE
    }

    /// <summary>
    /// Generic inbound envelope. Deserialize into this first to read
    /// `Type`, then deserialize `Payload.GetRawText()` into the specific
    /// payload record for that type (see the *Payload records below).
    /// See rules.md #9 — every message must be validated before acting on it.
    /// </summary>
    public class InboundEnvelope
    {
        public string Type { get; set; } = "";
        public string? SessionId { get; set; }
        public string? RequestId { get; set; }
        public JsonElement Payload { get; set; }
    }

    public class OutboundEnvelope<TPayload>
    {
        public string Type { get; set; } = "";
        public string? SessionId { get; set; }
        public string? RequestId { get; set; }
        public TPayload? Payload { get; set; }
    }

    public class ErrorPayload
    {
        public ErrorCode Code { get; set; }
        public string Message { get; set; } = "";
        public string? DeviceId { get; set; }
    }

    // ---------- Client -> Agent payload shapes (PROTOCOL.md "Client (Web UI) -> Agent") ----------

    public class HelloPayload
    {
        public string Token { get; set; } = "";
        public string ClientVersion { get; set; } = "";
    }

    public class DeviceIdPayload
    {
        public string DeviceId { get; set; } = "";
    }

    public class CreateSessionPayload
    {
        public string Name { get; set; } = "";
        public List<string> DeviceIds { get; set; } = new();
        public AudioSourceType AudioSource { get; set; } = AudioSourceType.System;
    }

    public class PlayPayload
    {
        public double Position { get; set; }
        public double TargetTimestamp { get; set; }
    }

    public class SeekPayload
    {
        public double Position { get; set; }
    }

    public class SetVolumePayload
    {
        public double Volume { get; set; }
    }

    public class SetDeviceEnabledPayload
    {
        public string DeviceId { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public class SetAudioSourcePayload
    {
        public AudioSourceType Source { get; set; } = AudioSourceType.System;
        public string? FilePath { get; set; }
    }

    // ---------- Agent -> Client payload shapes (PROTOCOL.md "Agent -> Client (Web UI)") ----------

    public class HelloAckPayload
    {
        public string AgentVersion { get; set; } = "";
        public string Platform { get; set; } = "";
    }

    public class DeviceListResultPayload
    {
        public List<AudioOutputDevice> Devices { get; set; } = new();
    }

    public class DeviceStatePayload
    {
        public string DeviceId { get; set; } = "";
        public DeviceState State { get; set; }
    }

    public class SyncPayload
    {
        public string SessionId { get; set; } = "";
        public List<DeviceSyncInfo> Devices { get; set; } = new();
    }

    public class StatusPayload
    {
        public string AgentVersion { get; set; } = "";
        public double UptimeSeconds { get; set; }
        public string? ActiveSessionId { get; set; }
    }
}
