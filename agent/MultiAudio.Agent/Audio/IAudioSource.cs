using System;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Contract for real-time audio sources fed into the MultiAudio Engine.
    /// </summary>
    public interface IAudioSource : IDisposable
    {
        WaveFormat WaveFormat { get; }
        bool IsActive { get; }
        double Position { get; }
        double Duration { get; }

        event Action<byte[], int, int>? DataAvailable;

        Task StartAsync();
        Task StopAsync();
        Task SeekAsync(double seconds);
    }
}

