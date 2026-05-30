using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handlers for world environment setup — skybox, ambient light, reflection probes.
/// </summary>
internal class EnvironmentHandlers : HandlerBase
{
    public EnvironmentHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleSetupEnvironment(string id, JObject p)
    {
        string parentName = p["parent"]?.ToString();
        string trackAs = p["trackAs"]?.ToString() ?? "Environment";
        bool addSkybox = p["skybox"]?.Value<bool>() ?? true;
        bool addAmbient = p["ambient"]?.Value<bool>() ?? true;
        bool addReflectionProbe = p["reflectionProbe"]?.Value<bool>() ?? true;

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        Slot parent = null;
        if (!string.IsNullOrEmpty(parentName))
            parent = _tracker.Get(parentName);
        parent ??= world.RootSlot;

        var envSlot = parent.AddSlot(trackAs);
        _tracker.Register(trackAs, envSlot);

        var result = new JObject
        {
            ["slot"] = trackAs,
            ["refId"] = envSlot.ReferenceID.ToString()
        };

        // Add Skybox
        if (addSkybox)
        {
            var skyboxSlot = envSlot.AddSlot("Skybox");
            var skybox = skyboxSlot.AttachComponent<Skybox>();
            result["skybox"] = new JObject
            {
                ["slotName"] = skyboxSlot.Name,
                ["refId"] = skybox.ReferenceID.ToString()
            };
            _tracker.Register("Skybox", skyboxSlot);
        }

        // Add Ambient Light
        if (addAmbient)
        {
            var ambientSlot = envSlot.AddSlot("AmbientLight");
            var ambient = ambientSlot.AttachComponent<AmbientLightSH2>();
            result["ambientLight"] = new JObject
            {
                ["slotName"] = ambientSlot.Name,
                ["refId"] = ambient.ReferenceID.ToString()
            };
            _tracker.Register("AmbientLight", ambientSlot);
        }

        // Add Reflection Probe
        if (addReflectionProbe)
        {
            var probeSlot = envSlot.AddSlot("ReflectionProbe");
            var probe = probeSlot.AttachComponent<ReflectionProbe>();
            result["reflectionProbe"] = new JObject
            {
                ["slotName"] = probeSlot.Name,
                ["refId"] = probe.ReferenceID.ToString()
            };
            _tracker.Register("ReflectionProbe", probeSlot);
        }

        result["trackedAs"] = trackAs;
        return Ok(id, result);
    }
}
