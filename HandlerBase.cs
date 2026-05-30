using System;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Base class for all command handler groups.
/// Provides shared access to tracker, events, templates, and response helpers.
/// </summary>
internal abstract class HandlerBase
{
    protected readonly SlotTracker _tracker;

    protected HandlerBase(SlotTracker tracker)
    {
        _tracker = tracker;
    }

    // ─── Response Helpers ───────────────────────────────────────

    protected JObject Ok(string id, JObject result)
    {
        result["id"] = id;
        result["status"] = "ok";
        return result;
    }

    protected JObject Error(string id, string message)
    {
        return new JObject
        {
            ["id"] = id,
            ["status"] = "error",
            ["error"] = message
        };
    }

    protected JObject Error(string id, string code, string message, JObject data = null)
    {
        var result = new JObject
        {
            ["id"] = id,
            ["status"] = "error",
            ["error"] = message,
            ["errorCode"] = code
        };
        if (data != null)
            result["errorData"] = data;
        return result;
    }

    // ─── Common Resolution ──────────────────────────────────────

    /// <summary>
    /// Resolve a component on a slot by type name and index.
    /// Returns (component, null) on success or (null, errorJObject) on failure.
    /// </summary>
    protected (Component component, JObject error) ResolveComponent(Slot slot, string typeName, int index, string id)
    {
        Type componentType = ComponentRegistry.Resolve(typeName);
        if (componentType == null)
            return (null, Error(id, "COMPONENT_NOT_FOUND", $"Unknown component type: '{typeName}'"));

        var components = slot.Components
            .Where(c => componentType.IsAssignableFrom(c.GetType()))
            .ToList();

        if (components.Count == 0)
            return (null, Error(id, "COMPONENT_NOT_FOUND", $"No '{typeName}' component found on slot '{slot.Name}'"));

        if (index < 0 || index >= components.Count)
            return (null, Error(id, "OUT_OF_RANGE", $"Component index {index} out of range. Slot '{slot.Name}' has {components.Count} '{typeName}' component(s) (0-{components.Count - 1})"));

        return (components[index], null);
    }

    /// <summary>Helper to resolve a parent slot from params, defaulting to world root.</summary>
    protected Slot ResolveParent(JObject p)
    {
        string parentName = p["parent"]?.ToString();
        Slot parent = null;
        if (!string.IsNullOrEmpty(parentName))
            parent = _tracker.Get(parentName);
        parent ??= Engine.Current.WorldManager.FocusedWorld?.RootSlot;
        return parent;
    }

    /// <summary>Get the focused world or null.</summary>
    protected World GetFocusedWorld()
    {
        return Engine.Current?.WorldManager?.FocusedWorld;
    }

    /// <summary>
    /// Auto-wires MeshRenderer components on a slot:
    ///   - Sets MeshRenderer.Mesh to the first IAssetProvider&lt;Mesh&gt; on the slot
    ///   - Adds material to MeshRenderer.Materials if a material component exists
    /// This mirrors what Resonite does when you manually assemble mesh+renderer+material.
    /// </summary>
    protected void AutoWireMeshRenderer(Slot slot)
    {
        foreach (var renderer in slot.GetComponents<MeshRenderer>())
        {
            // Wire mesh if not already set
            if (renderer.Mesh.Target == null)
            {
                var meshProvider = slot.GetComponent<IAssetProvider<Mesh>>();
                if (meshProvider != null)
                    renderer.Mesh.Target = meshProvider;
            }

            // Wire material if Materials list is empty
            if (renderer.Materials.Count == 0)
            {
                var materialProvider = slot.GetComponent<IAssetProvider<Material>>();
                if (materialProvider != null)
                    renderer.Materials.Add(materialProvider);
            }
        }
    }
}
