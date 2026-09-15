using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MultiAudio.Agent.Protocol;

namespace MultiAudio.Agent
{
    /// <summary>
    /// The localhost control server described in Architecture.md §7 and
    /// specified message-by-message in shared/protocol/PROTOCOL.md.
    ///
    /// Security (rules.md #9):
    ///   - binds to 127.0.0.1 only, never 0.0.0.0;
    ///   - validates the Origin header on every WebSocket upgrade;
    ///   - requires a valid HELLO (with pairing token) before any other
    ///     message on a connection is processed;
    ///   - GET /pairing-token exists only so a same-machine browser can
    ///     retrieve the token on first run (see PairingTokenStore) — it
    ///     is not a substitute for the checks above.
    /// </summary>
    public class ControlServer
    {
        private const int Port = 8787;
        private static readonly DateTime StartedAt = DateTime.UtcNow;
        private static readonly string AgentVersion = "0.2.0-wasapi";

        // No enum converters registered here on purpose: each enum in
        // Protocol.cs carries its own [JsonConverter] attribute (plain
        // JsonStringEnumConverter<ErrorCode> for UPPER_SNAKE_CASE,
        // LowercaseEnumConverter<T> for the rest). Options-level
        // converters are checked before type-level attributes in
        // System.Text.Json, so registering a catch-all converter here
        // would silently override all of those per-type choices.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly SessionManager _sessions = new();
        private readonly string _pairingToken = PairingTokenStore.GetOrCreateToken();
        private readonly HttpListener _listener = new();
        private readonly List<WebSocket> _activeSockets = new();

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _sessions.DevicesChanged += () =>
            {
                _ = BroadcastDeviceListAsync();
            };

            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            Console.WriteLine($"MultiAudio Agent listening on ws://127.0.0.1:{Port}/");
            Console.WriteLine($"Pairing token (first run only, see docs/design.md §16): {_pairingToken}");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var contextTask = _listener.GetContextAsync();
                    var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken));
                    if (completed != contextTask) break;

                    var context = await contextTask;
                    _ = HandleHttpContextAsync(context, cancellationToken); // fire-and-forget per connection
                }
            }
            catch (ObjectDisposedException)
            {
                // listener stopped during shutdown — expected
            }
            finally
            {
                _listener.Stop();
            }
        }

        private async Task HandleHttpContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                if (context.Request.Url?.AbsolutePath == "/pairing-token")
                {
                    var origin = context.Request.Headers["Origin"];
                    if (!IsOriginAllowed(origin))
                    {
                        context.Response.StatusCode = 403;
                        context.Response.Close();
                        return;
                    }

                    if (context.Request.HttpMethod == "OPTIONS")
                    {
                        ApplyCorsHeaders(context.Response, origin);
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                        return;
                    }

                    if (context.Request.HttpMethod == "GET")
                    {
                        await WriteJsonResponseAsync(context, new { token = _pairingToken }, origin);
                        return;
                    }

                    context.Response.StatusCode = 405;
                    context.Response.Close();
                    return;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    return;
                }

                if (!IsOriginAllowed(context.Request.Headers["Origin"]))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return;
                }

                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                await HandleConnectionAsync(wsContext.WebSocket, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ControlServer] connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Same-machine origins only. A browser tab on a remote page
        /// cannot carry an Origin of http://localhost/http://127.0.0.1,
        /// so this is a meaningful check, not just a formality.
        /// </summary>
        private static bool IsOriginAllowed(string? origin)
        {
            if (string.IsNullOrEmpty(origin)) return true; // non-browser clients (e.g. dev tools) send none
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host is "localhost" or "127.0.0.1" or "[::1]");
        }

        private async Task HandleConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            string? currentSessionId = null;
            var authenticated = false;

            try
            {
                while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    var envelope = await ReceiveEnvelopeAsync(socket, cts.Token);
                    if (envelope == null) break; // connection closed by peer

                    if (!authenticated)
                    {
                        if (envelope.Type != "HELLO")
                        {
                            await SendErrorAsync(socket, ErrorCode.UNAUTHORIZED, "First message must be HELLO.", envelope.RequestId, cts.Token);
                            break;
                        }

                        var hello = Deserialize<HelloPayload>(envelope.Payload);
                        if (hello?.Token != _pairingToken)
                        {
                            await SendErrorAsync(socket, ErrorCode.UNAUTHORIZED, "Invalid pairing token.", envelope.RequestId, cts.Token);
                            break;
                        }

                        authenticated = true;
                        lock (_activeSockets) _activeSockets.Add(socket);
                        await SendAsync(socket, "HELLO_ACK", null,
                            new HelloAckPayload { AgentVersion = AgentVersion, Platform = "Windows (WASAPI)" },
                            envelope.RequestId, cts.Token);

                        // Kick off periodic SESSION_STATE/SYNC pushes for
                        // whatever session this connection creates
                        // (PROTOCOL.md: "pushed periodically + on change").
                        _ = RunPeriodicPushLoopAsync(socket, () => currentSessionId, cts.Token);
                        continue;
                    }

                    try
                    {
                        currentSessionId = await DispatchAsync(socket, envelope, currentSessionId, cts.Token);
                    }
                    catch (ProtocolException pex)
                    {
                        await SendErrorAsync(socket, pex.Code, pex.Message, envelope.RequestId, cts.Token, pex.DeviceId);
                    }
                    catch (Exception ex)
                    {
                        await SendErrorAsync(socket, ErrorCode.INVALID_MESSAGE, ex.Message, envelope.RequestId, cts.Token);
                    }
                }
            }
            catch (WebSocketException)
            {
                // peer dropped the connection abruptly — nothing to do
            }
            finally
            {
                lock (_activeSockets) _activeSockets.Remove(socket);
                cts.Cancel();
                if (socket.State == WebSocketState.Open)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                    catch { /* best effort */ }
                }
            }
        }

        private async Task BroadcastDeviceListAsync()
        {
            var devices = _sessions.GetDeviceListSnapshot();
            var payload = new DeviceListResultPayload { Devices = devices };
            List<WebSocket> targets;
            lock (_activeSockets)
            {
                targets = _activeSockets.Where(s => s.State == WebSocketState.Open).ToList();
            }

            foreach (var socket in targets)
            {
                try
                {
                    await SendAsync(socket, "DEVICE_LIST_RESULT", null, payload, null, CancellationToken.None);
                }
                catch { }
            }
        }

        /// <summary>Routes one authenticated message. Returns the connection's current sessionId (unchanged unless CREATE_SESSION ran).</summary>
        private async Task<string?> DispatchAsync(WebSocket socket, InboundEnvelope envelope, string? currentSessionId, CancellationToken ct)
        {
            switch (envelope.Type)
            {
                case "DEVICE_LIST":
                {
                    var devices = _sessions.GetDeviceListSnapshot();
                    await SendAsync(socket, "DEVICE_LIST_RESULT", null,
                        new DeviceListResultPayload { Devices = devices }, envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "DEVICE_CONNECT":
                {
                    var payload = Deserialize<DeviceIdPayload>(envelope.Payload) ?? throw Invalid("DEVICE_CONNECT requires deviceId.");
                    var device = await _sessions.ConnectDeviceAsync(payload.DeviceId);
                    await SendAsync(socket, "DEVICE_STATE", null,
                        new DeviceStatePayload { DeviceId = device.Id, State = device.State }, envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "DEVICE_DISCONNECT":
                {
                    var payload = Deserialize<DeviceIdPayload>(envelope.Payload) ?? throw Invalid("DEVICE_DISCONNECT requires deviceId.");
                    var device = await _sessions.DisconnectDeviceAsync(payload.DeviceId);
                    await SendAsync(socket, "DEVICE_STATE", null,
                        new DeviceStatePayload { DeviceId = device.Id, State = device.State }, envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "OPEN_BLUETOOTH_SETTINGS":
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ms-settings:bluetooth",
                        UseShellExecute = true
                    });
                    return currentSessionId;
                }

                case "CREATE_SESSION":
                {
                    var payload = Deserialize<CreateSessionPayload>(envelope.Payload) ?? throw Invalid("CREATE_SESSION requires name and deviceIds.");
                    var sessionId = _sessions.CreateSession(payload.Name, payload.DeviceIds, payload.AudioSource);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return sessionId;
                }

                case "PLAY":
                {
                    var sessionId = RequireSessionId(envelope);
                    var payload = Deserialize<PlayPayload>(envelope.Payload) ?? new PlayPayload();
                    await _sessions.PlayAsync(sessionId, payload.Position);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId ?? sessionId;
                }

                case "PAUSE":
                {
                    var sessionId = RequireSessionId(envelope);
                    await _sessions.PauseAsync(sessionId);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "STOP":
                {
                    var sessionId = RequireSessionId(envelope);
                    await _sessions.StopAsync(sessionId);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "SEEK":
                {
                    var sessionId = RequireSessionId(envelope);
                    var payload = Deserialize<SeekPayload>(envelope.Payload) ?? throw Invalid("SEEK requires position.");
                    await _sessions.SeekAsync(sessionId, payload.Position);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "SET_VOLUME":
                {
                    var sessionId = RequireSessionId(envelope);
                    var payload = Deserialize<SetVolumePayload>(envelope.Payload) ?? throw Invalid("SET_VOLUME requires volume.");
                    await _sessions.SetVolumeAsync(sessionId, payload.Volume);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "SET_DEVICE_ENABLED":
                {
                    var sessionId = RequireSessionId(envelope);
                    var payload = Deserialize<SetDeviceEnabledPayload>(envelope.Payload) ?? throw Invalid("SET_DEVICE_ENABLED requires deviceId and enabled.");
                    await _sessions.SetDeviceEnabledAsync(sessionId, payload.DeviceId, payload.Enabled);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "SET_AUDIO_SOURCE":
                {
                    var sessionId = RequireSessionId(envelope);
                    var payload = Deserialize<SetAudioSourcePayload>(envelope.Payload) ?? throw Invalid("SET_AUDIO_SOURCE requires source.");
                    await _sessions.SetAudioSourceAsync(sessionId, payload.Source, payload.FilePath);
                    await SendAsync(socket, "SESSION_STATE", sessionId,
                        _sessions.GetSessionStateSnapshot(sessionId), envelope.RequestId, ct);
                    return currentSessionId;
                }

                case "GET_STATUS":
                {
                    var activeId = currentSessionId ?? System.Linq.Enumerable.FirstOrDefault(_sessions.ActiveSessionIds);
                    await SendAsync(socket, "STATUS", null, new StatusPayload
                    {
                        AgentVersion = AgentVersion,
                        UptimeSeconds = (DateTime.UtcNow - StartedAt).TotalSeconds,
                        ActiveSessionId = activeId
                    }, envelope.RequestId, ct);
                    return currentSessionId;
                }

                default:
                    throw new ProtocolException(ErrorCode.INVALID_MESSAGE, $"Unknown message type '{envelope.Type}'.");
            }

            static ProtocolException Invalid(string message) => new(ErrorCode.INVALID_MESSAGE, message);
        }

        private static string RequireSessionId(InboundEnvelope envelope) =>
            envelope.SessionId ?? throw new ProtocolException(ErrorCode.SESSION_NOT_FOUND, $"{envelope.Type} requires sessionId.");

        /// <summary>Pushes SESSION_STATE + SYNC every second while this connection has an active session (PROTOCOL.md).</summary>
        private async Task RunPeriodicPushLoopAsync(WebSocket socket, Func<string?> getSessionId, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    var sessionId = getSessionId();
                    if (sessionId == null) continue;

                    try
                    {
                        await SendAsync(socket, "SESSION_STATE", sessionId, _sessions.GetSessionStateSnapshot(sessionId), null, ct);
                        await SendAsync(socket, "SYNC", sessionId,
                            new SyncPayload { SessionId = sessionId, Devices = _sessions.GetSyncSnapshot(sessionId) }, null, ct);
                    }
                    catch (ProtocolException)
                    {
                        // session was torn down between the null-check and the read; skip this tick
                    }
                }
            }
            catch (OperationCanceledException) { /* connection closing */ }
            catch (WebSocketException) { /* connection closing */ }
        }

        // ---------- wire helpers ----------

        private static async Task<InboundEnvelope?> ReceiveEnvelopeAsync(WebSocket socket, CancellationToken ct)
        {
            using var stream = new System.IO.MemoryStream();
            var buffer = new byte[8192];

            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch (WebSocketException)
                {
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close) return null;
                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
            }

            if (stream.Length == 0) return null;
            stream.Position = 0;

            try
            {
                return JsonSerializer.Deserialize<InboundEnvelope>(stream, JsonOptions);
            }
            catch (JsonException)
            {
                return new InboundEnvelope { Type = "__INVALID__" };
            }
        }

        private static T? Deserialize<T>(JsonElement element) where T : class
        {
            if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
            return element.Deserialize<T>(JsonOptions);
        }

        private static Task SendAsync<TPayload>(WebSocket socket, string type, string? sessionId, TPayload payload, string? requestId, CancellationToken ct)
        {
            var envelope = new OutboundEnvelope<TPayload>
            {
                Type = type,
                SessionId = sessionId,
                RequestId = requestId,
                Payload = payload
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            return socket.State == WebSocketState.Open
                ? socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, ct)
                : Task.CompletedTask;
        }

        private static Task SendErrorAsync(WebSocket socket, ErrorCode code, string message, string? requestId, CancellationToken ct, string? deviceId = null) =>
            SendAsync(socket, "ERROR", null, new ErrorPayload { Code = code, Message = message, DeviceId = deviceId }, requestId, ct);

        private static async Task WriteJsonResponseAsync(HttpListenerContext context, object body, string? origin = null)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = json.Length;
            ApplyCorsHeaders(context.Response, origin);
            await context.Response.OutputStream.WriteAsync(json);
            context.Response.Close();
        }

        private static void ApplyCorsHeaders(HttpListenerResponse response, string? origin)
        {
            if (string.IsNullOrEmpty(origin)) return;
            response.Headers["Access-Control-Allow-Origin"] = origin;
            response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            response.Headers["Vary"] = "Origin";
        }
    }
}
