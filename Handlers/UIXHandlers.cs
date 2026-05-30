using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handler for building complete UI hierarchies from declarative JSON trees.
/// </summary>
internal class UIXHandlers : HandlerBase
{
    public UIXHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleBuildUIXTree(string id, JObject p)
    {
        string parentName = p["parent"]?.ToString() ?? "__root__";
        var root = p["root"] as JObject;

        if (root == null)
            return Error(id, "INVALID_PARAMS", "buildUIXTree requires 'root' object");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, "SLOT_NOT_FOUND", $"Parent slot '{parentName}' not found");

        var created = new JArray();
        var errors = new JArray();

        BuildTreeNode(parent, root, created, errors);

        return Ok(id, new JObject
        {
            ["slotsCreated"] = created.Count,
            ["errors"] = errors.Count,
            ["slots"] = created,
            ["errorDetails"] = errors
        });
    }

    private void BuildTreeNode(Slot parent, JObject node, JArray created, JArray errors)
    {
        string name = node["name"]?.ToString() ?? "Node";
        string tag = node["tag"]?.ToString();
        bool active = node["active"]?.Value<bool>() ?? true;

        var slot = parent.AddSlot(name);
        if (!string.IsNullOrEmpty(tag))
            slot.Tag = tag;
        slot.ActiveSelf = active;

        // Transform
        var pos = node["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = node["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = node["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        _tracker.Register(name, slot);

        var slotResult = new JObject
        {
            ["name"] = name,
            ["refId"] = slot.ReferenceID.ToString()
        };

        // Components
        var componentsArr = node["components"] as JArray;
        if (componentsArr != null)
        {
            var attachedComps = new JArray();
            foreach (JObject compDef in componentsArr)
            {
                string typeName = compDef["type"]?.ToString();
                var fields = compDef["fields"] as JObject;

                Type componentType = ComponentRegistry.Resolve(typeName);
                if (componentType == null)
                {
                    errors.Add(new JObject { ["slot"] = name, ["error"] = $"Unknown component type '{typeName}'" });
                    continue;
                }

                try
                {
                    var component = slot.AttachComponent(componentType);

                    if (fields != null)
                    {
                        foreach (var kvp in fields)
                        {
                            try
                            {
                                FieldParser.SetFieldValue(component, kvp.Key, kvp.Value, _tracker);
                            }
                            catch (Exception ex)
                            {
                                errors.Add(new JObject { ["slot"] = name, ["component"] = typeName, ["field"] = kvp.Key, ["error"] = ex.Message });
                            }
                        }
                    }

                    attachedComps.Add(componentType.Name);
                }
                catch (Exception ex)
                {
                    errors.Add(new JObject { ["slot"] = name, ["error"] = $"Failed to attach {typeName}: {ex.Message}" });
                }
            }

            if (attachedComps.Count > 0)
                slotResult["components"] = attachedComps;

            // Auto-wire MeshRenderer to mesh and material on this node
            AutoWireMeshRenderer(slot);
        }

        created.Add(slotResult);

        // Recurse into children
        var children = node["children"] as JArray;
        if (children != null)
        {
            foreach (JObject child in children)
            {
                BuildTreeNode(slot, child, created, errors);
            }
        }
    }
}
