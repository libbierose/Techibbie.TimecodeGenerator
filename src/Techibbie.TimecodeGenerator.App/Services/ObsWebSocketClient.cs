using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Techibbie.TimecodeGenerator.App.Services;

/// <summary>
/// Minimal client for the obs-websocket v5 protocol (built into OBS Studio 28+, enabled via
/// Tools → WebSocket Server Settings). Connects, performs the Hello/Identify handshake
/// (including the SHA-256 challenge/salt auth obs-websocket uses when a password is set),
/// subscribes to the "Outputs" event category, and raises stream/record start/stop events.
/// Runs its own reconnect loop — the caller doesn't need to retry.
/// </summary>
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    // obs-websocket EventSubscription bitmask: General (1<<0) + Outputs (1<<6).
    private const int EventSubscriptions = (1 << 0) | (1 << 6);
    private const int RpcVersion = 1;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public event Action<bool>? StreamStateChanged;
    public event Action<bool>? RecordStateChanged;
    public event Action<bool>? ConnectionChanged;

    /// <summary>Start the connect/retry loop in the background. Safe to call repeatedly — restarts the loop with new settings.</summary>
    public void Start(string host, int port, string password, TimeSpan? retryInterval = null)
    {
        Stop();

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        var interval = retryInterval ?? TimeSpan.FromSeconds(5);

        _loopTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(host, port, password, token);
                }
                catch
                {
                    // Connection lost or OBS not running yet — fall through to the retry delay.
                }

                ConnectionChanged?.Invoke(false);
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private async Task RunOnceAsync(string host, int port, string password, CancellationToken token)
    {
        using var ws = new ClientWebSocket();
        var uri = new Uri($"ws://{host}:{port}");
        await ws.ConnectAsync(uri, token);

        var hello = await ReceiveJsonAsync(ws, token);
        var d = hello.GetProperty("d");

        string? authResponse = null;
        if (d.TryGetProperty("authentication", out var authEl))
        {
            var challenge = authEl.GetProperty("challenge").GetString()!;
            var salt = authEl.GetProperty("salt").GetString()!;
            authResponse = ComputeAuthResponse(password, salt, challenge);
        }

        var identify = new
        {
            op = 1,
            d = new
            {
                rpcVersion = RpcVersion,
                authentication = authResponse,
                eventSubscriptions = EventSubscriptions,
            },
        };
        await SendJsonAsync(ws, identify, token);

        var identified = await ReceiveJsonAsync(ws, token);
        if (identified.GetProperty("op").GetInt32() != 2)
        {
            throw new InvalidOperationException("obs-websocket did not confirm Identify (wrong password?).");
        }

        ConnectionChanged?.Invoke(true);

        while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var msg = await ReceiveJsonAsync(ws, token);
            if (msg.GetProperty("op").GetInt32() != 5) continue; // only interested in Event messages

            var ed = msg.GetProperty("d");
            var eventType = ed.GetProperty("eventType").GetString();
            var active = ed.TryGetProperty("eventData", out var data) && data.TryGetProperty("outputActive", out var a) && a.GetBoolean();

            switch (eventType)
            {
                case "StreamStateChanged": StreamStateChanged?.Invoke(active); break;
                case "RecordStateChanged": RecordStateChanged?.Invoke(active); break;
            }
        }
    }

    /// <summary>
    /// obs-websocket's documented auth response: base64(sha256(base64(sha256(password+salt)) + challenge)).
    /// Public (not just internal) because it's pure, side-effect-free, and directly unit testable.
    /// </summary>
    public static string ComputeAuthResponse(string password, string salt, string challenge)
    {
        using var sha256 = SHA256.Create();
        var secretBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
        var base64Secret = Convert.ToBase64String(secretBytes);

        var responseBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(base64Secret + challenge));
        return Convert.ToBase64String(responseBytes);
    }

    private static async Task SendJsonAsync(ClientWebSocket ws, object payload, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, token);
    }

    private static async Task<JsonElement> ReceiveJsonAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var stream = new System.IO.MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("obs-websocket closed the connection.");
            }
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        stream.Position = 0;
        return JsonSerializer.Deserialize<JsonElement>(stream);
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { /* ignore */ }
        }
    }
}
