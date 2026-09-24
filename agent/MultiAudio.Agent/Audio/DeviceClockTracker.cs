using System;
using System.Collections.Generic;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Tracks one output device's real playback clock against wall-clock
    /// time, from a single measured signal: how many milliseconds of
    /// audio the device's own hardware clock (WASAPI's IAudioClock, via
    /// <see cref="NativeAudioClockAccess"/>) reports it has actually
    /// rendered, sampled at a known wall-clock instant.
    ///
    /// rules.md #7 -- this is measured, not guessed or simulated: every
    /// sample comes from a real IAudioClock::GetPosition() call. It is
    /// deliberately NOT derived from software buffer bookkeeping (bytes
    /// enqueued minus bytes still buffered), which would only reflect
    /// how much this process handed to WASAPI, not how much the
    /// hardware actually played.
    ///
    /// See Architecture.md §4 for the target_time/scheduling_offset
    /// model this feeds: <see cref="ClockOffsetMs"/> and
    /// <see cref="DriftEstimateMsPerSec"/> are the "how far off, and how
    /// fast is that changing" numbers a scheduler/corrector needs.
    /// </summary>
    public sealed class DeviceClockTracker
    {
        /// <summary>
        /// Minimum samples before a drift estimate is trusted rather
        /// than treated as startup noise. Diagnostics are sampled at
        /// the ~1Hz cadence of the periodic SYNC push (ControlServer),
        /// so this is roughly a 5 second warm-up.
        /// </summary>
        public const int MinSamplesForConfidence = 5;

        private const int MaxSamples = 30;
        private const double RegressionWindowMs = 15_000;

        private readonly object _lock = new();
        private readonly List<(double ElapsedMs, double OffsetMs)> _samples = new();

        public double ClockOffsetMs { get; private set; }
        public double DriftEstimateMsPerSec { get; private set; }

        public int SampleCount { get { lock (_lock) return _samples.Count; } }
        public bool HasConfidentEstimate => SampleCount >= MinSamplesForConfidence;

        public void Reset()
        {
            lock (_lock)
            {
                _samples.Clear();
                ClockOffsetMs = 0;
                DriftEstimateMsPerSec = 0;
            }
        }

        /// <summary>
        /// Records one measured sample: at <paramref name="wallElapsedMs"/>
        /// ms of real (wall-clock) time since playback started, the
        /// device's own hardware clock reports having rendered
        /// <paramref name="devicePositionMs"/> ms of audio. A device
        /// whose clock runs fast relative to real time shows a positive,
        /// growing offset; one that runs slow shows a negative, shrinking
        /// one.
        /// </summary>
        public void AddSample(double wallElapsedMs, double devicePositionMs)
        {
            lock (_lock)
            {
                var offsetMs = devicePositionMs - wallElapsedMs;

                _samples.Add((wallElapsedMs, offsetMs));

                var cutoff = wallElapsedMs - RegressionWindowMs;
                _samples.RemoveAll(s => s.ElapsedMs < cutoff);
                while (_samples.Count > MaxSamples) _samples.RemoveAt(0);

                ClockOffsetMs = offsetMs;
                DriftEstimateMsPerSec = EstimateSlopePerSecondLocked();
            }
        }

        // Ordinary least-squares slope of offsetMs vs elapsedMs (ms per
        // ms), converted to ms/sec. A simple linear fit is enough here:
        // consumer audio clock drift (crystal oscillator tolerance) is
        // very close to a constant rate over the tens-of-seconds windows
        // this tracks -- it doesn't swing around like network jitter.
        private double EstimateSlopePerSecondLocked()
        {
            if (_samples.Count < 3) return 0;

            double n = _samples.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            foreach (var (x, y) in _samples)
            {
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            var denominator = n * sumXX - sumX * sumX;
            if (Math.Abs(denominator) < 1e-9) return 0;

            var slopePerMs = (n * sumXY - sumX * sumY) / denominator;
            return slopePerMs * 1000.0;
        }
    }
}
