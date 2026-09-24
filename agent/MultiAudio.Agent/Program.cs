// MultiAudio Native Agent — entry point.
//
// This is the privileged, platform-specific component described in
// Architecture.md §2.2. It owns all OS audio API access; the web UI
// never talks to Bluetooth/audio APIs directly (rules.md #3, #4).
//
// The control server exposes live Windows WASAPI render endpoints and
// distributes the selected audio source to every enabled output.

using System.Net;
using MultiAudio.Agent;

AgentLog.Start();

// Real-hardware test runs can hit failures that never show up against
// the loopback/simulated path this was built and exercised on. Two
// global handlers so a failure on a background thread (e.g. the
// fire-and-forget per-connection task in ControlServer) is logged
// instead of disappearing silently (rules.md #12).
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine($"[WARN] Unobserved task exception: {e.Exception}");
    e.SetObserved();
};

Console.WriteLine("MultiAudio Agent starting...");
if (AgentLog.FilePath != null)
    Console.WriteLine($"Logging to: {AgentLog.FilePath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // let the loop below exit cleanly instead of the process dying mid-connection
    cts.Cancel();
};

try
{
    var server = new ControlServer();
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // expected on Ctrl+C
}
catch (HttpListenerException ex)
{
    // The single most likely real-world startup failure: another copy
    // of the agent (or something else) already has the port. Give a
    // clear, actionable message instead of a raw HttpListenerException
    // stack trace.
    Console.WriteLine($"[FATAL] Could not start the control server: {ex.Message}");
    Console.WriteLine("This usually means another instance of the agent is already running, or something else is using the port. Close any other running copy and try again.");
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] Agent crashed: {ex}");
}

Console.WriteLine("MultiAudio Agent stopped.");

