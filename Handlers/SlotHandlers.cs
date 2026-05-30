using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handles all slot lifecycle and property commands:
/// createSlot, setSlotActive, destroySlot, destroyChildren, reparentSlot,
/// setSlotName, setSlotTag, setSlotOrderIndex, duplicateSlot, setSlotPersist,
/// setSlotTransform.
/// </summary>
internal class SlotHandlers : HandlerBase
{
    public SlotHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleCreateSlot(string id, JObject p)
    {
        string name = p["name"]?.ToString();
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string tag = p["tag"]?.ToString();
        bool active = p["active"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(name))
            return Error(id, "INVALID_PARAMS", "createSlot requires 'name'");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, "SLOT_NOT_FOUND", $"Parent slot '{parentName}' not found");

        var slot = parent.AddSlot(name);
        if (!string.IsNullOrEmpty(tag))
            slot.Tag = tag;
        slot.ActiveSelf = active;

        // Optional transform
        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        _tracker.Register(name, slot);

        var result = new JObject
        {
            ["slotName"] = name,
            ["refId"] = slot.ReferenceID.ToString()
        };

        // Optional inline component attachment
        var componentsArr = p["components"] as JArray;
        if (componentsArr != null && componentsArr.Count > 0)
        {
            var attachedComponents = new JArray();
            var compErrors = new JArray();

            foreach (JObject compDef in componentsArr)
            {
                string typeName = compDef["type"]?.ToString();
                var fields = compDef["fields"] as JObject;

                Type componentType = ComponentRegistry.Resolve(typeName);
                if (componentType == null)
                {
                    compErrors.Add(new JObject { ["type"] = typeName, ["error"] = $"Unknown component type '{typeName}'" });
                    continue;
                }

                try
                {
                    var component = slot.AttachComponent(componentType);
                    var compResult = new JObject
                    {
                        ["type"] = componentType.Name,
                        ["refId"] = component.ReferenceID.ToString()
                    };

                    // Set fields if provided
                    if (fields != null && fields.Count > 0)
                    {
                        var setFieldNames = new JArray();
                        var fieldErrors = new JArray();

                        foreach (var kvp in fields)
                        {
                            try
                            {
                                FieldParser.SetFieldValue(component, kvp.Key, kvp.Value, _tracker);
                                setFieldNames.Add(kvp.Key);
                            }
                            catch (Exception ex)
                            {
                                fieldErrors.Add(new JObject { ["field"] = kvp.Key, ["error"] = ex.Message });
                            }
                        }

                        compResult["fieldsSet"] = setFieldNames;
                        if (fieldErrors.Count > 0)
                            compResult["fieldErrors"] = fieldErrors;
                    }

                    attachedComponents.Add(compResult);
                }
                catch (Exception ex)
                {
                    compErrors.Add(new JObject { ["type"] = typeName, ["error"] = ex.Message });
                }
            }

            // Auto-wire MeshRenderer to mesh and material components on the same slot
            AutoWireMeshRenderer(slot);

            result["components"] = attachedComponents;
            if (compErrors.Count > 0)
                result["componentErrors"] = compErrors;
        }

        return Ok(id, result);
    }

    public JObject HandleSetSlotActive(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        bool active = p["active"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        slot.ActiveSelf = active;

        return Ok(id, new JObject { ["slot"] = slotName, ["active"] = active });
    }

    public JObject HandleDestroySlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        slot.Destroy();
        _tracker.Unregister(slotName);
        int purged = _tracker.PurgeDestroyed();

        return Ok(id, new JObject
        {
            ["destroyed"] = slotName,
            ["trackerEntriesPurged"] = purged
        });
    }

    public JObject HandleDestroyChildren(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        slot.DestroyChildren();
        int purged = _tracker.PurgeDestroyed();

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["childrenDestroyed"] = true,
            ["trackerEntriesPurged"] = purged
        });
    }

    public JObject HandleReparentSlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string newParentName = p["newParent"]?.ToString();
        bool preserveGlobal = p["preserveGlobalTransform"]?.Value<bool>() ?? false;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        if (string.IsNullOrEmpty(newParentName))
            return Error(id, "INVALID_PARAMS", "reparentSlot requires 'newParent'");

        var newParent = _tracker.Get(newParentName);
        if (newParent == null)
            return Error(id, "SLOT_NOT_FOUND", $"New parent slot '{newParentName}' not found");

        slot.SetParent(newParent, preserveGlobal);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["newParent"] = newParentName,
            ["preservedGlobalTransform"] = preserveGlobal
        });
    }

    public JObject HandleSetSlotName(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string newName = p["newName"]?.ToString();
        bool updateTracker = p["updateTracker"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        if (string.IsNullOrEmpty(newName))
            return Error(id, "INVALID_PARAMS", "setSlotName requires 'newName'");

        slot.Name = newName;

        if (updateTracker)
        {
            _tracker.Unregister(slotName);
            _tracker.Register(newName, slot);
        }

        return Ok(id, new JObject
        {
            ["oldName"] = slotName,
            ["newName"] = newName,
            ["trackerUpdated"] = updateTracker
        });
    }

    public JObject HandleSetSlotTag(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string tag = p["tag"]?.ToString() ?? "";

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        slot.Tag = tag;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["tag"] = tag
        });
    }

    public JObject HandleSetSlotOrderIndex(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int index = p["index"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        slot.ChildIndex = index;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["index"] = slot.ChildIndex
        });
    }

    public JObject HandleDuplicateSlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();
        bool keepGlobalTransform = p["keepGlobalTransform"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var duplicate = slot.Duplicate(keepGlobalTransform: keepGlobalTransform);

        // Register with provided name or auto-generated name
        string name = trackAs ?? $"{slot.Name}_copy";
        duplicate.Name = name;
        _tracker.Register(name, duplicate);

        return Ok(id, new JObject
        {
            ["originalSlot"] = slotName,
            ["duplicateName"] = name,
            ["refId"] = duplicate.ReferenceID.ToString(),
            ["trackedAs"] = name
        });
    }

    public JObject HandleSetSlotPersist(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        bool persistent = p["persistent"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        slot.PersistentSelf = persistent;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["persistent"] = persistent,
            ["refId"] = slot.ReferenceID.ToString()
        });
    }

    public JObject HandleSetSlotTransform(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["position"] = new JArray(slot.LocalPosition.x, slot.LocalPosition.y, slot.LocalPosition.z),
            ["rotation"] = new JArray(slot.LocalRotation.x, slot.LocalRotation.y, slot.LocalRotation.z, slot.LocalRotation.w),
            ["scale"] = new JArray(slot.LocalScale.x, slot.LocalScale.y, slot.LocalScale.z)
        });
    }

    /// <summary>Set world-space (global) position, rotation, and/or scale on a slot.</summary>
    public JObject HandleSetGlobalTransform(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.GlobalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.GlobalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.GlobalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.GlobalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.GlobalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["globalPosition"] = new JArray(slot.GlobalPosition.x, slot.GlobalPosition.y, slot.GlobalPosition.z),
            ["globalRotation"] = new JArray(slot.GlobalRotation.x, slot.GlobalRotation.y, slot.GlobalRotation.z, slot.GlobalRotation.w),
            ["globalScale"] = new JArray(slot.GlobalScale.x, slot.GlobalScale.y, slot.GlobalScale.z)
        });
    }

    /// <summary>Orient a slot to face a target position or another tracked slot.</summary>
    public JObject HandleLookAt(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        float3 targetPos;

        // Accept either a target slot name or a position array
        string targetSlot = p["target"]?.ToString();
        var posArr = p["position"] as JArray;

        if (!string.IsNullOrEmpty(targetSlot))
        {
            var target = _tracker.Get(targetSlot);
            if (target == null)
                return Error(id, "SLOT_NOT_FOUND", $"Target slot '{targetSlot}' not found");
            targetPos = target.GlobalPosition;
        }
        else if (posArr != null && posArr.Count == 3)
        {
            targetPos = new float3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());
        }
        else
        {
            return Error(id, "INVALID_PARAMS", "lookAt requires 'target' (slot name) or 'position' ([x,y,z])");
        }

        // Optional up vector (default Y-up)
        var upArr = p["up"] as JArray;
        float3 up = (upArr != null && upArr.Count == 3)
            ? new float3(upArr[0].Value<float>(), upArr[1].Value<float>(), upArr[2].Value<float>())
            : float3.Up;

        // Calculate look rotation
        float3 direction = (targetPos - slot.GlobalPosition).Normalized;
        if (MathX.Dot(direction, direction) < 0.0001f)
            return Error(id, "OPERATION_FAILED", "Slot and target are at the same position");

        slot.GlobalRotation = floatQ.LookRotation(direction, up);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["lookingAt"] = new JArray(targetPos.x, targetPos.y, targetPos.z),
            ["globalRotation"] = new JArray(slot.GlobalRotation.x, slot.GlobalRotation.y, slot.GlobalRotation.z, slot.GlobalRotation.w)
        });
    }
}
