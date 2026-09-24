using System;
using System.IO;
using System.Text;

namespace MultiAudio.Agent
{
    /// <summary>
    /// Mirrors everything written to <see cref="Console"/> into a
    /// plain-text log file next to the executable, in addition to the
    /// console itself. Every existing <c>Console.WriteLine</c> call
    /// across the codebase gets this for free once <see cref="Start"/>
    /// runs once at startup -- no other call site needs to change.
    ///
    /// This exists for real-hardware test runs: if the agent hits a
    /// problem specific to a real Bluetooth endpoint, the console
    /// window may get closed or scrolled past before anyone reads it.
    /// A log file survives that. One file per run (timestamped), so
    /// nothing gets silently overwritten or unbounded.
    /// </summary>
    internal static class AgentLog
    {
        public static string? FilePath { get; private set; }

        public static void Start()
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);

                var path = Path.Combine(dir, $"agent-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };

                Console.SetOut(new TeeTextWriter(Console.Out, writer));
                Console.SetError(new TeeTextWriter(Console.Error, writer));
                FilePath = path;
            }
            catch (Exception ex)
            {
                // Logging is a diagnostics convenience, not something
                // that should ever take the agent itself down -- fall
                // back to console-only and say so.
                Console.WriteLine($"[AgentLog] Could not open log file, continuing console-only: {ex.Message}");
            }
        }

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _primary;
            private readonly TextWriter _secondary;

            public TeeTextWriter(TextWriter primary, TextWriter secondary)
            {
                _primary = primary;
                _secondary = secondary;
            }

            public override Encoding Encoding => _primary.Encoding;

            public override void Write(char value)
            {
                _primary.Write(value);
                _secondary.Write(value);
            }

            public override void Write(string? value)
            {
                _primary.Write(value);
                _secondary.Write(value);
            }

            public override void WriteLine(string? value)
            {
                var stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {value}";
                _primary.WriteLine(value);
                _secondary.WriteLine(stamped);
            }
        }
    }
}
