using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Lightweight HTTP + WebSocket server using System.Net.HttpListener.
/// Listens on localhost only for security.
/// Routes requests to CommandRouter.
/// </summary>
internal class BridgeHttpServer
{
    private readonly int _port;
    private readonly CommandRouter _router;
    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private Thread _listenerThread;

    private const int MaxBodySize = 10 * 1024 * 1024; // 10 MB
    private const int WsBufferSize = 64 * 1024; // 64 KB WebSocket buffer

    // Active WebSocket connections for event broadcasting
    private readonly ConcurrentDictionary<string, WebSocket> _wsClients = new();

    public int WebSocketClientCount => _wsClients.Count;

    public BridgeHttpServer(int port, CommandRouter router)
    {
        _port = port;
        _router = router;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");

        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "AntigravityBridge-HTTP"
        };

        _listener.Start();
        _listenerThread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();

        // Close all WebSocket connections
        foreach (var kvp in _wsClients)
        {
            try
            {
                kvp.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* best effort */ }
        }
        _wsClients.Clear();

        try { _listener?.Stop(); } catch { /* already stopped */ }
        try { _listener?.Close(); } catch { /* already closed */ }
        _cts?.Dispose();
        _cts = null;
        _listener = null;
    }

    /// <summary>Broadcast a JSON message to all connected WebSocket clients.</summary>
    public async Task BroadcastAsync(JObject message)
    {
        var json = message.ToString(Formatting.None);
        var buffer = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(buffer);

        var deadClients = new List<string>();

        foreach (var kvp in _wsClients)
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.SendAsync(segment, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
                }
                else
                {
                    deadClients.Add(kvp.Key);
                }
            }
            catch
            {
                deadClients.Add(kvp.Key);
            }
        }

        // Clean up dead connections
        foreach (var id in deadClients)
            _wsClients.TryRemove(id, out _);
    }

    private void ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var context = _listener.GetContext();
                // Process each request on the thread pool
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                ResoniteMod.Error($"HTTP listener error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers for local development
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        try
        {
            // Handle OPTIONS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                SendResponse(response, 200, new JObject { ["status"] = "ok" });
                return;
            }

            string path = request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();

            // Handle WebSocket upgrade
            if (path == "/ws" && request.IsWebSocketRequest)
            {
                HandleWebSocketUpgrade(context);
                return;
            }

            switch (path)
            {
                case "/ping":
                    HandlePing(response);
                    break;

                case "/cmd":
                    HandleCmd(request, response);
                    break;

                case "/batch":
                    HandleBatch(request, response);
                    break;

                case "/tracker":
                    HandleTracker(response);
                    break;

                case "/help":
                    SendResponse(response, 200, _router.GetCommandHelp());
                    break;

                case "/status":
                    SendResponse(response, 200, _router.GetStatus());
                    break;

                case "/ws":
                    // WebSocket request but not an upgrade
                    SendResponse(response, 400, new JObject
                    {
                        ["status"] = "error",
                        ["error"] = "WebSocket upgrade required. Connect using a WebSocket client."
                    });
                    break;

                default:
                    SendResponse(response, 404, new JObject
                    {
                        ["status"] = "error",
                        ["error"] = $"Unknown endpoint: {path}. Use /ping, /cmd, /batch, /tracker, /help, /status, or /ws"
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            ResoniteMod.Error($"Request handler error: {ex}");
            try
            {
                SendResponse(response, 500, new JObject
                {
                    ["status"] = "error",
                    ["error"] = ex.Message
                });
            }
            catch { /* response may already be sent */ }
        }
    }

    // ─── WebSocket ──────────────────────────────────────────────

    private async void HandleWebSocketUpgrade(HttpListenerContext context)
    {
        WebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (Exception ex)
        {
            ResoniteMod.Error($"WebSocket upgrade failed: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        var ws = wsContext.WebSocket;
        var clientId = Guid.NewGuid().ToString("N")[..8];
        _wsClients[clientId] = ws;

        ResoniteMod.Msg($"[WS] Client {clientId} connected ({_wsClients.Count} total)");

        // Send welcome message
        await WsSendAsync(ws, new JObject
        {
            ["type"] = "connected",
            ["clientId"] = clientId,
            ["mod"] = "AntigravityBridge",
            ["version"] = AntigravityBridge.Instance.Version,
            ["trackedSlots"] = _router.TrackedSlotCount
        });

        var buffer = new byte[WsBufferSize];

        try
        {
            while (ws.State == WebSocketState.Open && !(_cts?.IsCancellationRequested ?? true))
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts?.Token ?? CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    // Handle message fragments for large messages
                    string message;
                    if (result.EndOfMessage)
                    {
                        message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                    else
                    {
                        // Accumulate fragments
                        var ms = new MemoryStream();
                        ms.Write(buffer, 0, result.Count);
                        while (!result.EndOfMessage)
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts?.Token ?? CancellationToken.None);
                            ms.Write(buffer, 0, result.Count);

                            if (ms.Length > MaxBodySize)
                            {
                                await WsSendAsync(ws, new JObject
                                {
                                    ["status"] = "error",
                                    ["error"] = $"Message too large. Max: {MaxBodySize / 1024 / 1024} MB"
                                });
                                ms.Dispose();
                                continue;
                            }
                        }
                        message = Encoding.UTF8.GetString(ms.ToArray());
                        ms.Dispose();
                    }

                    if (AntigravityBridge.IsVerbose)
                        ResoniteMod.Msg($"[WS {clientId}] {message[..Math.Min(200, message.Length)]}");

                    await HandleWsMessage(ws, clientId, message);
                }
            }
        }
        catch (WebSocketException)
        {
            // Client disconnected
        }
        catch (OperationCanceledException)
        {
            // Server shutting down
        }
        catch (Exception ex)
        {
            ResoniteMod.Error($"[WS {clientId}] Error: {ex.Message}");
        }
        finally
        {
            _wsClients.TryRemove(clientId, out _);
            ResoniteMod.Msg($"[WS] Client {clientId} disconnected ({_wsClients.Count} total)");

            if (ws.State != WebSocketState.Closed && ws.State != WebSocketState.Aborted)
            {
                try { ws.Abort(); } catch { }
            }
            ws.Dispose();
        }
    }

    private async Task HandleWsMessage(WebSocket ws, string clientId, string message)
    {
        JObject json;
        try
        {
            json = JObject.Parse(message);
        }
        catch (JsonException ex)
        {
            await WsSendAsync(ws, new JObject
            {
                ["status"] = "error",
                ["error"] = $"Invalid JSON: {ex.Message}"
            });
            return;
        }

        // Check if it's a batch
        if (json["commands"] is JArray commands)
        {
            var options = json["options"] as JObject;
            bool stopOnError = options?["stopOnError"]?.Value<bool>() ?? false;
            var result = _router.ExecuteBatch(commands, stopOnError);
            result["type"] = "batchResult";
            await WsSendAsync(ws, result);
        }
        else if (json["action"] != null)
        {
            // Single command
            var result = _router.ExecuteCommand(json);
            result["type"] = "cmdResult";
            await WsSendAsync(ws, result);
        }
        else
        {
            await WsSendAsync(ws, new JObject
            {
                ["status"] = "error",
                ["error"] = "Expected 'action' for single command or 'commands' array for batch"
            });
        }
    }

    private async Task WsSendAsync(WebSocket ws, JObject message)
    {
        if (ws.State != WebSocketState.Open) return;

        var json = message.ToString(Formatting.None);
        var buffer = Encoding.UTF8.GetBytes(json);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true,
                _cts?.Token ?? CancellationToken.None);

            if (AntigravityBridge.IsVerbose)
                ResoniteMod.Msg($"[WS RSP] {json[..Math.Min(200, json.Length)]}");
        }
        catch (Exception ex)
        {
            ResoniteMod.Warn($"[WS] Send failed: {ex.Message}");
        }
    }

    // ─── HTTP Handlers ──────────────────────────────────────────

    private void HandlePing(HttpListenerResponse response)
    {
        SendResponse(response, 200, new JObject
        {
            ["status"] = "ok",
            ["mod"] = "AntigravityBridge",
            ["version"] = AntigravityBridge.Instance.Version,
            ["trackedSlots"] = _router.TrackedSlotCount,
            ["wsClients"] = _wsClients.Count
        });
    }

    private void HandleCmd(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            SendResponse(response, 405, new JObject
            {
                ["status"] = "error",
                ["error"] = "POST required"
            });
            return;
        }

        string body = ReadBody(request, response);
        if (body == null) return; // response already sent

        if (AntigravityBridge.IsVerbose)
            ResoniteMod.Msg($"[CMD] {body}");

        JObject cmd;
        try
        {
            cmd = JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            SendResponse(response, 400, new JObject
            {
                ["status"] = "error",
                ["error"] = $"Invalid JSON: {ex.Message}"
            });
            return;
        }

        var result = _router.ExecuteCommand(cmd);
        int statusCode = result["status"]?.ToString() == "ok" ? 200 : 400;
        SendResponse(response, statusCode, result);
    }

    private void HandleBatch(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            SendResponse(response, 405, new JObject
            {
                ["status"] = "error",
                ["error"] = "POST required"
            });
            return;
        }

        string body = ReadBody(request, response);
        if (body == null) return; // response already sent

        JObject batch;
        try
        {
            batch = JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            SendResponse(response, 400, new JObject
            {
                ["status"] = "error",
                ["error"] = $"Invalid JSON: {ex.Message}"
            });
            return;
        }

        var commands = batch["commands"] as JArray;
        if (commands == null)
        {
            SendResponse(response, 400, new JObject
            {
                ["status"] = "error",
                ["error"] = "'commands' array required"
            });
            return;
        }

        // Parse batch options
        var options = batch["options"] as JObject;
        bool stopOnError = options?["stopOnError"]?.Value<bool>() ?? false;

        // Execute all commands in a single engine thread dispatch
        var responseBody = _router.ExecuteBatch(commands, stopOnError);

        SendResponse(response, 200, responseBody);
    }

    private void HandleTracker(HttpListenerResponse response)
    {
        var slots = _router.GetTrackedSlots();
        SendResponse(response, 200, new JObject
        {
            ["status"] = "ok",
            ["trackedSlots"] = slots
        });
    }

    /// <summary>
    /// Read request body with size limit enforcement. Returns null if body exceeds limit (response already sent).
    /// </summary>
    private string ReadBody(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.ContentLength64 > MaxBodySize)
        {
            SendResponse(response, 413, new JObject
            {
                ["status"] = "error",
                ["error"] = $"Request body too large ({request.ContentLength64} bytes). Max: {MaxBodySize / 1024 / 1024} MB"
            });
            return null;
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return reader.ReadToEnd();
    }

    private void SendResponse(HttpListenerResponse response, int statusCode, JObject body)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        byte[] buffer = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();

        if (AntigravityBridge.IsVerbose)
        {
            var json = body.ToString(Formatting.None);
            ResoniteMod.Msg($"[RSP] {statusCode} {json[..Math.Min(200, json.Length)]}");
        }
    }
}
