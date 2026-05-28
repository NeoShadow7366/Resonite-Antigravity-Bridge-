using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Lightweight HTTP server using System.Net.HttpListener.
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
        try { _listener?.Stop(); } catch { /* already stopped */ }
        try { _listener?.Close(); } catch { /* already closed */ }
        _cts?.Dispose();
        _cts = null;
        _listener = null;
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

                default:
                    SendResponse(response, 404, new JObject
                    {
                        ["status"] = "error",
                        ["error"] = $"Unknown endpoint: {path}. Use /ping, /cmd, /batch, /tracker, or /help"
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

    private void HandlePing(HttpListenerResponse response)
    {
        SendResponse(response, 200, new JObject
        {
            ["status"] = "ok",
            ["mod"] = "AntigravityBridge",
            ["version"] = AntigravityBridge.Instance.Version,
            ["trackedSlots"] = _router.TrackedSlotCount
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
