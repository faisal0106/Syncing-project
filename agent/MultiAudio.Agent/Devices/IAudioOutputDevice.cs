using System.Threading.Tasks;
using MultiAudio.Agent.Protocol;

namespace MultiAudio.Agent.Devices
{
    /// <summary>
    /// Common interface every platform-specific output implementation
    /// must conform to. See Architecture.md §5 (Device Abstraction).
    ///
    /// rules.md #2 — never assume one platform's capability implies
    /// another's. Each implementation must be honest about what it
    /// can actually do; use Capabilities to advertise, not assume.
    /// </summary>
    public interface IAudioOutputDevice
    {
        string Id { get; }
        string Name { get; }
        DeviceType Type { get; }
        DeviceState State { get; }
        string[] Capabilities { get; }

        /// <summary>Measured, not guessed (rules.md #7). Null until measured.</summary>
        double? LatencyMs { get; }

        Task ConnectAsync();
        Task DisconnectAsync();

        /// <summary>Begin rendering audio to this device from the current session clock.</summary>
        Task StartAsync();

        Task StopAsync();

        /// <summary>0.0 - 1.0</summary>
        Task SetVolumeAsync(double volume);

        DeviceDiagnostics GetDiagnostics();
    }

    public class DeviceDiagnostics
    {
        public double BufferDepthMs { get; set; }
        public double ClockOffsetMs { get; set; }
        public double DriftEstimateMsPerSec { get; set; }
        public string? LastError { get; set; }
    }
}
