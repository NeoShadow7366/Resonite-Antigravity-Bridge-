using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Handlers for component attachment, removal, field get/set, and component inspection.
/// </summary>
internal class ComponentHandlers : HandlerBase
{
    public ComponentHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleAttachComponent(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string typeName = p["type"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Type componentType = ComponentRegistry.Resolve(typeName);
        if (componentType == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"Component type '{typeName}' not found");

        var component = slot.AttachComponent(componentType);

        // Apply initial field values if provided
        var fields = p["fields"] as JObject;
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
                    ResoniteMod.Warn($"[CMD {id}] Failed to set field '{kvp.Key}': {ex.Message}");
                }
            }
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentType.Name,
            ["refId"] = component.ReferenceID.ToString()
        });
    }

    public JObject HandleRemoveComponent(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string typeName = p["type"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, typeName, componentIndex, id);
        if (error != null)
            return error;

        component.Destroy();

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["removedComponent"] = typeName
        });
    }

    public JObject HandleCopyComponent(string id, JObject p)
    {
        string sourceSlot = p["sourceSlot"]?.ToString();
        string targetSlot = p["targetSlot"]?.ToString();
        string componentName = p["component"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var srcSlot = _tracker.Get(sourceSlot);
        if (srcSlot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Source slot '{sourceSlot}' not found");

        var tgtSlot = _tracker.Get(targetSlot);
        if (tgtSlot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Target slot '{targetSlot}' not found");

        var (component, error) = ResolveComponent(srcSlot, componentName, componentIndex, id);
        if (error != null) return error;

        try
        {
            // Use DuplicateComponent on the target slot
            var copy = tgtSlot.DuplicateComponent(component);
            return Ok(id, new JObject
            {
                ["sourceSlot"] = sourceSlot,
                ["targetSlot"] = targetSlot,
                ["component"] = componentName,
                ["copyType"] = copy.GetType().Name,
                ["copyRefId"] = copy.ReferenceID.ToString()
            });
        }
        catch (Exception ex)
        {
            return Error(id, "OPERATION_FAILED", $"Failed to copy component: {ex.Message}");
        }
    }

    public JObject HandleSetField(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        var value = p["value"];
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null)
            return error;

        try
        {
            FieldParser.SetFieldValue(component, fieldName, value, _tracker);
        }
        catch (Exception ex)
        {
            return Error(id, "FIELD_SET_FAILED", $"Failed to set {componentName}.{fieldName}: {ex.Message}");
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["field"] = fieldName,
            ["set"] = true
        });
    }

    public JObject HandleSetFields(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        var fields = p["fields"] as JObject;
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        if (fields == null || fields.Count == 0)
            return Error(id, "INVALID_PARAMS", "setFields requires 'fields' object with field→value pairs");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null)
            return error;

        var setFields = new JArray();
        var errors = new JArray();

        foreach (var kvp in fields)
        {
            try
            {
                FieldParser.SetFieldValue(component, kvp.Key, kvp.Value, _tracker);
                setFields.Add(kvp.Key);
            }
            catch (Exception ex)
            {
                errors.Add(new JObject
                {
                    ["field"] = kvp.Key,
                    ["error"] = ex.Message
                });
            }
        }

        var result = new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["set"] = setFields,
            ["setCount"] = setFields.Count,
            ["totalRequested"] = fields.Count
        };

        if (errors.Count > 0)
            result["errors"] = errors;

        return Ok(id, result);
    }

    public JObject HandleGetComponentField(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        if (string.IsNullOrEmpty(fieldName))
            return Error(id, "INVALID_PARAMS", "getComponentField requires 'field'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null)
            return error;

        var member = component.GetSyncMember(fieldName);
        if (member == null)
            return Error(id, "FIELD_NOT_FOUND", $"Field '{fieldName}' not found on {componentName}");

        try
        {
            var value = FieldParser.ReadFieldValue(member);
            return Ok(id, new JObject
            {
                ["slot"] = slotName,
                ["component"] = componentName,
                ["field"] = fieldName,
                ["value"] = value,
                ["fieldType"] = member.GetType().Name
            });
        }
        catch (Exception ex)
        {
            return Error(id, "FIELD_READ_FAILED", $"Failed to read {componentName}.{fieldName}: {ex.Message}");
        }
    }

    public JObject HandleGetComponentFields(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null)
            return error;

        var fields = new JArray();
        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            var member = component.GetSyncMember(i);
            if (member == null) continue;

            string name = component.GetSyncMemberName(i);
            var fieldInfo = new JObject
            {
                ["name"] = name,
                ["type"] = member.GetType().Name,
            };

            try
            {
                fieldInfo["value"] = FieldParser.ReadFieldValue(member);
            }
            catch
            {
                fieldInfo["value"] = "<error reading>";
            }

            fields.Add(fieldInfo);
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["fieldCount"] = fields.Count,
            ["fields"] = fields
        });
    }

    public JObject HandleFindComponents(string id, JObject p)
    {
        string typeName = p["type"]?.ToString();
        string slotName = p["slot"]?.ToString() ?? "__root__";
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;
        bool trackMatches = p["trackMatches"]?.Value<bool>() ?? false;

        var root = _tracker.Get(slotName);
        if (root == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Type componentType = ComponentRegistry.Resolve(typeName);
        if (componentType == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"Component type '{typeName}' not found");

        var matches = new JArray();
        SearchForComponents(root, componentType, matches, 0, maxDepth, trackMatches);

        return Ok(id, new JObject
        {
            ["type"] = typeName,
            ["searchRoot"] = slotName,
            ["count"] = matches.Count,
            ["matches"] = matches
        });
    }

    private void SearchForComponents(Slot slot, Type componentType, JArray results, int depth, int maxDepth, bool trackMatches)
    {
        int matchCount = 0;
        foreach (var comp in slot.Components)
        {
            if (componentType.IsAssignableFrom(comp.GetType()))
                matchCount++;
        }

        if (matchCount > 0)
        {
            if (trackMatches)
                _tracker.Register(slot.Name, slot);

            results.Add(new JObject
            {
                ["slotName"] = slot.Name,
                ["refId"] = slot.ReferenceID.ToString(),
                ["depth"] = depth,
                ["componentCount"] = matchCount
            });
        }

        if (maxDepth == -1 || depth < maxDepth)
        {
            foreach (var child in slot.Children)
                SearchForComponents(child, componentType, results, depth + 1, maxDepth, trackMatches);
        }
    }

    public JObject HandleGetRegisteredComponents(string id)
    {
        var categories = new JObject();

        var uixCore = new JArray();
        var uixLayout = new JArray();
        var textures = new JArray();
        var materials = new JArray();
        var meshes = new JArray();
        var lighting = new JArray();
        var colliders = new JArray();
        var audio = new JArray();
        var interaction = new JArray();
        var animation = new JArray();
        var dynVars = new JArray();
        var utility = new JArray();

        foreach (var kvp in ComponentRegistry.ComponentTypes.OrderBy(k => k.Key))
        {
            var entry = new JObject
            {
                ["shortName"] = kvp.Key,
                ["fullType"] = kvp.Value.FullName
            };

            if (kvp.Value.Namespace == "FrooxEngine.UIX")
            {
                if (kvp.Key.Contains("Layout") || kvp.Key == "RectTransform" || kvp.Key == "LayoutElement" ||
                    kvp.Key == "ContentSizeFitter" || kvp.Key == "ScrollRect" || kvp.Key == "IgnoreLayout")
                    uixLayout.Add(entry);
                else
                    uixCore.Add(entry);
            }
            else if (kvp.Key.Contains("Texture") || kvp.Key == "SpriteProvider")
                textures.Add(entry);
            else if (kvp.Key.Contains("Material") || kvp.Key.StartsWith("PBS"))
                materials.Add(entry);
            else if (kvp.Key.Contains("Mesh") || kvp.Key.Contains("Renderer"))
                meshes.Add(entry);
            else if (kvp.Key == "Light")
                lighting.Add(entry);
            else if (kvp.Key.Contains("Collider"))
                colliders.Add(entry);
            else if (kvp.Key.Contains("Audio"))
                audio.Add(entry);
            else if (kvp.Key == "Grabbable")
                interaction.Add(entry);
            else if (kvp.Key == "Spinner" || kvp.Key == "Wiggler" || kvp.Key.StartsWith("Panner"))
                animation.Add(entry);
            else if (kvp.Key.Contains("DynamicVariable"))
                dynVars.Add(entry);
            else
                utility.Add(entry);
        }

        return Ok(id, new JObject
        {
            ["totalRegistered"] = ComponentRegistry.ComponentTypes.Count,
            ["categories"] = new JObject
            {
                ["uixCore"] = uixCore,
                ["uixLayout"] = uixLayout,
                ["textures"] = textures,
                ["materials"] = materials,
                ["meshes"] = meshes,
                ["lighting"] = lighting,
                ["colliders"] = colliders,
                ["audio"] = audio,
                ["interaction"] = interaction,
                ["animation"] = animation,
                ["dynamicVariables"] = dynVars,
                ["utility"] = utility
            },
            ["note"] = "Components not listed here can still be attached using their full FrooxEngine type name"
        });
    }

    public JObject HandleGetComponentByRefId(string id, JObject p)
    {
        string refIdStr = p["refId"]?.ToString();

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        IWorldElement element = null;

        if (ulong.TryParse(refIdStr, out var rawId))
            element = world.ReferenceController.GetObjectOrNull(new RefID(rawId));
        if (element == null && ulong.TryParse(refIdStr,
            System.Globalization.NumberStyles.HexNumber, null, out var hexId))
            element = world.ReferenceController.GetObjectOrNull(new RefID(hexId));

        if (element == null)
            return Error(id, "REF_NOT_FOUND", $"No element found with RefID '{refIdStr}'");

        var result = new JObject
        {
            ["refId"] = refIdStr,
            ["type"] = element.GetType().Name,
            ["fullType"] = element.GetType().FullName
        };

        if (element is Component comp)
        {
            result["isComponent"] = true;
            result["slotName"] = comp.Slot?.Name;
            result["slotRefId"] = comp.Slot?.ReferenceID.ToString();

            var fields = new JArray();
            for (int i = 0; i < comp.SyncMemberCount; i++)
            {
                var member = comp.GetSyncMember(i);
                var memberName = comp.GetSyncMemberName(i);
                var fieldObj = new JObject
                {
                    ["name"] = memberName,
                    ["type"] = member?.GetType()?.Name ?? "unknown"
                };

                try
                {
                    var valueProperty = member?.GetType().GetProperty("Value");
                    if (valueProperty != null)
                        fieldObj["value"] = valueProperty.GetValue(member)?.ToString();
                    else if (member is ISyncRef syncRef)
                        fieldObj["value"] = syncRef.Target?.ToString() ?? "<null>";
                }
                catch { /* skip */ }

                fields.Add(fieldObj);
            }
            result["fields"] = fields;
        }
        else if (element is Slot slot)
        {
            result["isSlot"] = true;
            result["slotName"] = slot.Name;
            result["childCount"] = slot.ChildrenCount;
            result["componentCount"] = slot.ComponentCount;
        }

        return Ok(id, result);
    }

    public JObject HandleGetAllComponents(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var components = new JArray();
        int index = 0;
        foreach (var comp in slot.Components)
        {
            var compObj = new JObject
            {
                ["index"] = index++,
                ["type"] = comp.GetType().Name,
                ["fullType"] = comp.GetType().FullName,
                ["refId"] = comp.ReferenceID.ToString()
            };

            var fields = new JArray();
            for (int i = 0; i < comp.SyncMemberCount; i++)
            {
                var member = comp.GetSyncMember(i);
                var memberName = comp.GetSyncMemberName(i);
                fields.Add(new JObject
                {
                    ["name"] = memberName,
                    ["type"] = member?.GetType()?.Name ?? "unknown"
                });
            }
            compObj["fields"] = fields;

            components.Add(compObj);
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["totalComponents"] = components.Count,
            ["components"] = components
        });
    }
}
