using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Generates a real-time synthetic test audio stream with stereo rhythmic pulses,
    /// ideal for verifying multi-device sync, latency, and absence of echo.
    /// </summary>
    public sealed class ToneAudioSource : IAudioSource
    {
        private const int SampleRate = 48000;
        private const int Channels = 2;
        private const int ChunkDurationMs = 20;
        private const int SamplesPerChunk = SampleRate * ChunkDurationMs / 1000;
        private const int BytesPerSample = 4; // 32-bit float
        private const int BytesPerChunk = SamplesPerChunk * Channels * BytesPerSample;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

        private CancellationTokenSource? _cts;
        private Task? _generatorTask;
        private bool _isActive;
        private double _positionSeconds;
        private double _phase;

        public bool IsActive => _isActive;
        public double Position => _positionSeconds;
        public double Duration => 300.0; // 5-minute virtual track

        public event Action<byte[], int, int>? DataAvailable;

        public Task StartAsync()
        {
            if (_isActive) return Task.CompletedTask;

            _isActive = true;
            _cts = new CancellationTokenSource();
            _generatorTask = Task.Run(() => GenerateLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isActive) return;
            _isActive = false;
            _cts?.Cancel();
            if (_generatorTask != null)
            {
                try { await _generatorTask; } catch (OperationCanceledException) { }
            }
            _generatorTask = null;
            _cts?.Dispose();
            _cts = null;
        }

        public Task SeekAsync(double seconds)
        {
            _positionSeconds = Math.Clamp(seconds, 0, Duration);
            return Task.CompletedTask;
        }

        private async Task GenerateLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[BytesPerChunk];
            var floatBuffer = new float[SamplesPerChunk * Channels];
            var interval = TimeSpan.FromMilliseconds(ChunkDurationMs);

            using var timer = new PeriodicTimer(interval);

            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                // Harmonious alternating chords (C4, E4, G4, A4) with subtle rhythmic pulse
                var beat = (int)(_positionSeconds * 2) % 4;
                var baseFreq = beat switch
                {
                    0 => 261.63, // C4
                    1 => 329.63, // E4
                    2 => 392.00, // G4
                    _ => 440.00  // A4
                };

                for (var i = 0; i < SamplesPerChunk; i++)
                {
                    var envelope = 0.15 * Math.Sin(Math.PI * (i % (SamplesPerChunk / 2)) / (SamplesPerChunk / 2.0));
                    var sampleVal = (float)(Math.Sin(_phase) * Math.Max(0.02, envelope));

                    // Stereo channels
                    floatBuffer[i * 2] = sampleVal;     // Left
                    floatBuffer[i * 2 + 1] = sampleVal; // Right

                    _phase += 2 * Math.PI * baseFreq / SampleRate;
                    if (_phase >= 2 * Math.PI) _phase -= 2 * Math.PI;
                }

                Buffer.BlockCopy(floatBuffer, 0, buffer, 0, BytesPerChunk);
                _positionSeconds += ChunkDurationMs / 1000.0;
                if (_positionSeconds >= Duration) _positionSeconds = 0;

                DataAvailable?.Invoke(buffer, 0, BytesPerChunk);
            }
        }

        public void Dispose()
        {
            _isActive = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}

