using System;
using System.Reflection;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// NAudio's <see cref="WasapiOut"/> does not publicly expose the
    /// underlying <see cref="AudioClient"/> it creates inside
    /// <c>WasapiOut.Init()</c>, but the real, hardware-measured
    /// synchronization signals this project needs -- rules.md #7
    /// ("latency should be measured rather than guessed") and
    /// Architecture.md §4's per-device clock offset -- only exist on
    /// that client: <c>StreamLatency</c> and the <c>AudioClockClient</c>
    /// service.
    ///
    /// The only way to reach it is to read WasapiOut's private
    /// "audioClient" field via reflection. That is exactly the kind of
    /// undocumented dependency rules.md #11 says must be isolated and
    /// marked experimental rather than folded into a device class, so
    /// it lives here on its own: every call is wrapped so a failure
    /// (e.g. a future NAudio release renaming or removing the field)
    /// can never take real playback down with it. Callers get null and
    /// the caller (WindowsAudioOutputDevice) degrades its synchronization
    /// telemetry to "unavailable" rather than the device failing to play.
    ///
    /// Pinned against NAudio 2.2.1 (see MultiAudio.Agent.csproj). If
    /// this starts always returning null after a NAudio upgrade, that's
    /// the field name/type having changed in the new version -- re-check
    /// against it rather than assuming the audio device itself is at fault.
    /// </summary>
    internal static class NativeAudioClockAccess
    {
        private static readonly FieldInfo? AudioClientField =
            typeof(WasapiOut).GetField("audioClient", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// The live <see cref="AudioClient"/> a started WasapiOut instance
        /// is using, or null if it isn't available (not yet initialized,
        /// or the reflection lookup failed).
        /// </summary>
        public static AudioClient? GetAudioClient(WasapiOut output)
        {
            try
            {
                return AudioClientField?.GetValue(output) as AudioClient;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Real WASAPI-reported stream latency in ms
        /// (IAudioClient::GetStreamLatency, valid any time after
        /// Initialize()), or null if unavailable.
        /// </summary>
        public static double? TryGetStreamLatencyMs(AudioClient? client)
        {
            if (client == null) return null;
            try
            {
                // StreamLatency is in 100ns units (REFERENCE_TIME).
                return client.StreamLatency / 10_000.0;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The device's own hardware audio clock service
        /// (IAudioClient::GetService(IID_IAudioClock)), or null if the
        /// endpoint doesn't expose one or it isn't available yet.
        /// </summary>
        public static AudioClockClient? TryGetAudioClock(AudioClient? client)
        {
            if (client == null) return null;
            try
            {
                return client.AudioClockClient;
            }
            catch
            {
                return null;
            }
        }
    }
}
