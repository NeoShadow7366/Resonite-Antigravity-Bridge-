using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;
using FrooxEngine;

namespace AntigravityBridge;

/// <summary>
/// AntigravityBridge — RML mod that exposes a local HTTP server for programmatic
/// scene construction. Antigravity sends JSON commands, this mod executes them
/// via FrooxEngine's API.
/// </summary>
public class AntigravityBridge : ResoniteMod
{
    public override string Name => "AntigravityBridge";
    public override string Author => "WikiFacet";
    public override string Version => "1.0.0";
    public override string Link => "https://github.com/NeoShadow7366/Resonite-Antigravity-Bridge-";

    internal static AntigravityBridge Instance;

    private BridgeHttpServer _server;
    private SlotTracker _tracker;
    private CommandRouter _router;

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> PORT = new("Port",
        "HTTP server port", () => 9090);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> VERBOSE = new("VerboseLogging",
        "Log every command and response", () => false);

    private static ModConfiguration _config;

    public override void OnEngineInit()
    {
        Instance = this;
        _config = GetConfiguration();

        _tracker = new SlotTracker();
        _router = new CommandRouter(_tracker);

        int port = _config.GetValue(PORT);
        _server = new BridgeHttpServer(port, _router);
        _server.Start();

        // Register for graceful shutdown
        Engine.Current.OnShutdown += OnEngineShutdown;

        Msg($"AntigravityBridge v{Version} listening on http://localhost:{port}/");
        Msg("Endpoints: /cmd, /batch, /ping, /tracker, /help");
    }

    private void OnEngineShutdown()
    {
        Msg("AntigravityBridge shutting down...");
        _server?.Stop();
        _server = null;
    }

    internal static bool IsVerbose => _config?.GetValue(VERBOSE) ?? false;
}
