// MultiAudio Native Agent — entry point.
//
// This is the privileged, platform-specific component described in
// Architecture.md §2.2. It owns all OS audio API access; the web UI
// never talks to Bluetooth/audio APIs directly (rules.md #3, #4).
//
// The control server exposes live Windows WASAPI render endpoints and
// distributes the selected audio source to every enabled output.

using MultiAudio.Agent;

Console.WriteLine("MultiAudio Agent starting...");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // let the loop below exit cleanly instead of the process dying mid-connection
    cts.Cancel();
};

var server = new ControlServer();

try
{
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // expected on Ctrl+C
}

Console.WriteLine("MultiAudio Agent stopped.");
