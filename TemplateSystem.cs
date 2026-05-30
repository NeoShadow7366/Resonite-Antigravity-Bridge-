using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Manages slot hierarchy snapshots and reusable templates.
/// Snapshots capture slot names, transforms, components, and field values.
/// Templates are named snapshots that can be stamped repeatedly.
/// </summary>
internal class TemplateSystem
{
    private readonly SlotTracker _tracker;
    private readonly ConcurrentDictionary<string, JObject> _templates = new(StringComparer.OrdinalIgnoreCase);

    public int TemplateCount => _templates.Count;

    public TemplateSystem(SlotTracker tracker)
    {
        _tracker = tracker;
    }

    // ─── Snapshot ────────────────────────────────────────────────

    /// <summary>Serialize a slot and its hierarchy to a JSON snapshot.</summary>
    public JObject SnapshotSlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;
        bool includeComponents = p["includeComponents"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return MakeError(id, $"Slot '{slotName}' not found");

        var snapshot = CaptureSlot(slot, 0, maxDepth, includeComponents);

        return MakeOk(id, new JObject
        {
            ["slot"] = slotName,
            ["snapshot"] = snapshot,
            ["slotCount"] = CountSlots(snapshot)
        });
    }

    /// <summary>Save a snapshot as a named template.</summary>
    public JObject SaveTemplate(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string templateName = p["templateName"]?.ToString();
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;

        if (string.IsNullOrEmpty(templateName))
            return MakeError(id, "saveTemplate requires 'templateName'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return MakeError(id, $"Slot '{slotName}' not found");

        var snapshot = CaptureSlot(slot, 0, maxDepth, true);
        _templates[templateName] = snapshot;

        return MakeOk(id, new JObject
        {
            ["templateName"] = templateName,
            ["sourceSlot"] = slotName,
            ["slotCount"] = CountSlots(snapshot),
            ["message"] = $"Template '{templateName}' saved"
        });
    }

    /// <summary>Stamp a template onto a parent slot, creating a copy.</summary>
    public JObject StampTemplate(string id, JObject p)
    {
        string templateName = p["templateName"]?.ToString();
        string parentSlotName = p["slot"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();

        if (string.IsNullOrEmpty(templateName))
            return MakeError(id, "stampTemplate requires 'templateName'");

        if (!_templates.TryGetValue(templateName, out var snapshot))
            return MakeError(id, $"Template '{templateName}' not found. Use saveTemplate first or listTemplates to see available templates.");

        var parentSlot = _tracker.Get(parentSlotName);
        if (parentSlot == null)
            return MakeError(id, $"Parent slot '{parentSlotName}' not found");

        var (rootSlot, slotCount) = RestoreFromSnapshot(parentSlot, snapshot);

        // Track the stamped root
        string trackedName = trackAs ?? rootSlot.Name;
        _tracker.Register(trackedName, rootSlot);

        return MakeOk(id, new JObject
        {
            ["templateName"] = templateName,
            ["slotName"] = rootSlot.Name,
            ["refId"] = rootSlot.ReferenceID.ToString(),
            ["trackedAs"] = trackedName,
            ["slotsCreated"] = slotCount
        });
    }

    /// <summary>List all saved templates.</summary>
    public JObject ListTemplates(string id)
    {
        var templates = new JArray();
        foreach (var kvp in _templates.OrderBy(k => k.Key))
        {
            templates.Add(new JObject
            {
                ["name"] = kvp.Key,
                ["rootName"] = kvp.Value["name"]?.ToString(),
                ["slotCount"] = CountSlots(kvp.Value)
            });
        }

        return MakeOk(id, new JObject
        {
            ["count"] = templates.Count,
            ["templates"] = templates
        });
    }

    /// <summary>Delete a saved template.</summary>
    public JObject DeleteTemplate(string id, JObject p)
    {
        string templateName = p["templateName"]?.ToString();

        if (string.IsNullOrEmpty(templateName))
            return MakeError(id, "deleteTemplate requires 'templateName'");

        if (_templates.TryRemove(templateName, out _))
        {
            return MakeOk(id, new JObject
            {
                ["templateName"] = templateName,
                ["message"] = $"Template '{templateName}' deleted"
            });
        }

        return MakeError(id, $"Template '{templateName}' not found");
    }

    // ─── Serialization ──────────────────────────────────────────

    private JObject CaptureSlot(Slot slot, int depth, int maxDepth, bool includeComponents)
    {
        var obj = new JObject
        {
            ["name"] = slot.Name,
            ["tag"] = slot.Tag,
            ["active"] = slot.ActiveSelf,
            ["position"] = new JArray(slot.LocalPosition.x, slot.LocalPosition.y, slot.LocalPosition.z),
            ["rotation"] = new JArray(slot.LocalRotation.x, slot.LocalRotation.y, slot.LocalRotation.z, slot.LocalRotation.w),
            ["scale"] = new JArray(slot.LocalScale.x, slot.LocalScale.y, slot.LocalScale.z),
        };

        // Capture components and their fields
        if (includeComponents)
        {
            var components = new JArray();
            foreach (var comp in slot.Components)
            {
                // Skip system components that are auto-created
                var typeName = comp.GetType().FullName;
                if (typeName == "FrooxEngine.Slot" || typeName == "FrooxEngine.SlotChildren")
                    continue;

                var compObj = new JObject
                {
                    ["type"] = comp.GetType().FullName,
                    ["shortType"] = comp.GetType().Name
                };

                // Capture writable fields
                var fields = new JObject();
                for (int i = 0; i < comp.SyncMemberCount; i++)
                {
                    var member = comp.GetSyncMember(i);
                    var memberName = comp.GetSyncMemberName(i);

                    try
                    {
                        string value = ReadMemberValue(member);
                        if (value != null)
                            fields[memberName] = value;
                    }
                    catch { /* skip unreadable fields */ }
                }

                if (fields.Count > 0)
                    compObj["fields"] = fields;

                components.Add(compObj);
            }

            if (components.Count > 0)
                obj["components"] = components;
        }

        // Capture children recursively
        if (maxDepth == -1 || depth < maxDepth)
        {
            var children = new JArray();
            foreach (var child in slot.Children)
            {
                children.Add(CaptureSlot(child, depth + 1, maxDepth, includeComponents));
            }
            if (children.Count > 0)
                obj["children"] = children;
        }

        return obj;
    }

    private (Slot rootSlot, int slotCount) RestoreFromSnapshot(Slot parent, JObject snapshot)
    {
        int totalSlots = 0;
        var rootSlot = RestoreSlotRecursive(parent, snapshot, ref totalSlots);
        return (rootSlot, totalSlots);
    }

    private Slot RestoreSlotRecursive(Slot parent, JObject snapshot, ref int slotCount)
    {
        string name = snapshot["name"]?.ToString() ?? "Slot";
        var slot = parent.AddSlot(name);
        slotCount++;

        // Set tag
        string tag = snapshot["tag"]?.ToString();
        if (!string.IsNullOrEmpty(tag))
            slot.Tag = tag;

        // Set active state
        if (snapshot["active"] != null)
            slot.ActiveSelf = snapshot["active"].Value<bool>();

        // Set transform
        if (snapshot["position"] is JArray pos && pos.Count >= 3)
            slot.LocalPosition = new Elements.Core.float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        if (snapshot["rotation"] is JArray rot && rot.Count >= 4)
            slot.LocalRotation = new Elements.Core.floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        if (snapshot["scale"] is JArray scl && scl.Count >= 3)
            slot.LocalScale = new Elements.Core.float3(scl[0].Value<float>(), scl[1].Value<float>(), scl[2].Value<float>());

        // Restore components
        if (snapshot["components"] is JArray components)
        {
            foreach (var compToken in components)
            {
                if (compToken is not JObject compObj) continue;

                string typeName = compObj["type"]?.ToString();
                if (string.IsNullOrEmpty(typeName)) continue;

                try
                {
                    // Resolve component type
                    Type compType = ResolveType(typeName);
                    if (compType == null)
                    {
                        if (AntigravityBridge.IsVerbose)
                            ResoniteMod.Warn($"[Template] Could not resolve type: {typeName}");
                        continue;
                    }

                    var component = slot.AttachComponent(compType);

                    // Set fields
                    if (compObj["fields"] is JObject fields)
                    {
                        foreach (var field in fields)
                        {
                            try
                            {
                                var member = component.GetSyncMember(field.Key);
                                if (member != null)
                                    SetMemberValue(member, field.Value.ToString());
                            }
                            catch (Exception ex)
                            {
                                if (AntigravityBridge.IsVerbose)
                                    ResoniteMod.Warn($"[Template] Failed to set field {field.Key}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (AntigravityBridge.IsVerbose)
                        ResoniteMod.Warn($"[Template] Failed to restore component {typeName}: {ex.Message}");
                }
            }
        }

        // Restore children
        if (snapshot["children"] is JArray children)
        {
            foreach (var childToken in children)
            {
                if (childToken is JObject childObj)
                    RestoreSlotRecursive(slot, childObj, ref slotCount);
            }
        }

        return slot;
    }

    // ─── Helpers ────────────────────────────────────────────────

    private Type ResolveType(string fullTypeName)
    {
        // Try FrooxEngine assembly
        var type = typeof(Slot).Assembly.GetType(fullTypeName, false, true);
        if (type != null) return type;

        // Try ProtoFluxBindings
        var pfxAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "ProtoFluxBindings");
        if (pfxAsm != null)
        {
            type = pfxAsm.GetType(fullTypeName, false, true);
            if (type != null) return type;
        }

        return null;
    }

    private string ReadMemberValue(ISyncMember member)
    {
        if (member == null) return null;
        var type = member.GetType();

        // Sync<T> value fields
        var valueProperty = type.GetProperty("Value");
        if (valueProperty != null)
        {
            var val = valueProperty.GetValue(member);
            return val?.ToString();
        }

        // Skip reference fields and complex types for now
        return null;
    }

    private void SetMemberValue(ISyncMember member, string value)
    {
        if (member == null || value == null) return;
        var type = member.GetType();

        var valueProperty = type.GetProperty("Value");
        if (valueProperty == null) return;

        var targetType = valueProperty.PropertyType;

        object parsedValue = null;
        if (targetType == typeof(string))
            parsedValue = value;
        else if (targetType == typeof(float))
            parsedValue = float.Parse(value);
        else if (targetType == typeof(int))
            parsedValue = int.Parse(value);
        else if (targetType == typeof(bool))
            parsedValue = bool.Parse(value);
        else if (targetType == typeof(double))
            parsedValue = double.Parse(value);
        else if (targetType == typeof(long))
            parsedValue = long.Parse(value);
        else if (targetType == typeof(Uri))
            parsedValue = new Uri(value);
        else if (targetType == typeof(Elements.Core.colorX))
        {
            if (Elements.Core.colorX.TryParse(value, out var c))
                parsedValue = c;
        }
        else if (targetType == typeof(Elements.Core.float3))
        {
            if (Elements.Core.float3.TryParse(value, out var v))
                parsedValue = v;
        }
        else if (targetType == typeof(Elements.Core.float2))
        {
            if (Elements.Core.float2.TryParse(value, out var v))
                parsedValue = v;
        }
        else if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, value, true, out var e))
                parsedValue = e;
        }

        if (parsedValue != null)
            valueProperty.SetValue(member, parsedValue);
    }

    private int CountSlots(JObject snapshot)
    {
        int count = 1;
        if (snapshot["children"] is JArray children)
        {
            foreach (var child in children)
            {
                if (child is JObject childObj)
                    count += CountSlots(childObj);
            }
        }
        return count;
    }

    private JObject MakeOk(string id, JObject data)
    {
        data["status"] = "ok";
        data["id"] = id;
        return data;
    }

    private JObject MakeError(string id, string error)
    {
        return new JObject
        {
            ["status"] = "error",
            ["id"] = id,
            ["error"] = error
        };
    }
}
