using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Reads and streams an audio file (MP3, WAV, FLAC, AAC) in real-time.
    /// </summary>
    public sealed class FileAudioSource : IAudioSource
    {
        private readonly string _filePath;
        private AudioFileReader? _reader;
        private CancellationTokenSource? _cts;
        private Task? _playbackTask;
        private bool _isActive;

        public WaveFormat WaveFormat => _reader?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public bool IsActive => _isActive;
        public double Position => _reader?.CurrentTime.TotalSeconds ?? 0;
        public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;

        public event Action<byte[], int, int>? DataAvailable;

        public FileAudioSource(string filePath)
        {
            _filePath = filePath;
            if (File.Exists(filePath))
            {
                _reader = new AudioFileReader(filePath);
            }
        }

        public Task StartAsync()
        {
            if (_isActive) return Task.CompletedTask;
            if (_reader == null)
            {
                if (File.Exists(_filePath))
                    _reader = new AudioFileReader(_filePath);
                else
                    throw new FileNotFoundException($"Audio file not found: {_filePath}");
            }

            _isActive = true;
            _cts = new CancellationTokenSource();
            _playbackTask = Task.Run(() => PlaybackLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isActive) return;
            _isActive = false;
            _cts?.Cancel();
            if (_playbackTask != null)
            {
                try { await _playbackTask; } catch (OperationCanceledException) { }
            }
            _playbackTask = null;
            _cts?.Dispose();
            _cts = null;
        }

        public Task SeekAsync(double seconds)
        {
            if (_reader != null)
            {
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration));
            }
            return Task.CompletedTask;
        }

        private async Task PlaybackLoopAsync(CancellationToken ct)
        {
            const int chunkDurationMs = 20;
            var bytesPerSec = WaveFormat.AverageBytesPerSecond;
            var bufferSize = bytesPerSec * chunkDurationMs / 1000;
            // Round to block align
            bufferSize -= bufferSize % WaveFormat.BlockAlign;
            var buffer = new byte[bufferSize];
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(chunkDurationMs));

            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                if (_reader == null) break;

                var read = _reader.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    DataAvailable?.Invoke(buffer, 0, read);
                }
                else
                {
                    // Looping back to start if track ends
                    _reader.Position = 0;
                }
            }
        }

        public void Dispose()
        {
            _isActive = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _reader?.Dispose();
            _reader = null;
        }
    }
}

