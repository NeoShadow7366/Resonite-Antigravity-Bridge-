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
    private EventSystem _events;

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> PORT = new("Port",
        "HTTP server port", () => 9090);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> VERBOSE = new("VerboseLogging",
        "Log every command and response", () => false);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> COMMAND_TIMEOUT = new("CommandTimeout",
        "Single command timeout in seconds", () => 10);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> UNDO_MODE = new("UndoMode",
        "Undo tracking scope: 'Full' (all operations) or 'Structural' (slot/component only)", () => "Structural");

    private static ModConfiguration _config;

    public override void OnEngineInit()
    {
        Instance = this;
        _config = GetConfiguration();

        _tracker = new SlotTracker();

        int port = _config.GetValue(PORT);

        // Create server first (needed for broadcast function)
        _router = new CommandRouter(_tracker);
        _server = new BridgeHttpServer(port, _router);

        // Create event system with broadcast function
        _events = new EventSystem(_tracker, msg => _server.BroadcastAsync(msg));
        _router.SetEventSystem(_events);

        // Create template system
        var templateSystem = new TemplateSystem(_tracker);
        _router.SetTemplateSystem(templateSystem);

        _server.Start();
        _events.Start();

        // Hook into engine update for event polling
        Engine.Current.OnShutdown += OnEngineShutdown;

        // Use a world-focused update by scheduling periodic checks
        Engine.Current.WorldManager.WorldFocused += OnWorldFocused;

        Msg($"AntigravityBridge v{Version} listening on http://localhost:{port}/");
        Msg($"Endpoints: /cmd, /batch, /ping, /tracker, /help, /status, /ws");
    }

    private void OnWorldFocused(World world)
    {
        if (world == null) return;

        // Hook into the world's update loop for event polling
        world.RunInUpdates(0, () => RegisterUpdateHook(world));
    }

    private void RegisterUpdateHook(World world)
    {
        // Use a recurring update by re-scheduling on each tick
        world.RunInUpdates(5, () =>
        {
            if (_events != null && world == Engine.Current.WorldManager.FocusedWorld)
            {
                _events.Tick();
                _events.CheckDestroyedAndUserEvents();

                // Re-register for next tick
                RegisterUpdateHook(world);
            }
        });
    }

    private void OnEngineShutdown()
    {
        Msg("AntigravityBridge shutting down...");
        _events?.Stop();
        _server?.Stop();
        _server = null;
    }

    internal static bool IsVerbose => _config?.GetValue(VERBOSE) ?? false;
    internal static int CommandTimeoutSeconds => _config?.GetValue(COMMAND_TIMEOUT) ?? 10;
    internal static string UndoModeValue => _config?.GetValue(UNDO_MODE) ?? "Structural";
}
