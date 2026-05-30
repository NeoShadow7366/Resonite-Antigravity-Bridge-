using System;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Handlers for logging, tracker management, dynamic variables, events, templates,
/// measurements, batch operations, and other utility commands.
/// </summary>
internal class UtilityHandlers : HandlerBase
{
    private readonly EventSystem _events;
    private readonly TemplateSystem _templates;

    public UtilityHandlers(SlotTracker tracker, EventSystem events, TemplateSystem templates) : base(tracker)
    {
        _events = events;
        _templates = templates;
    }

    // ─── Logging ────────────────────────────────────────────────

    public JObject HandleLog(string id, JObject p)
    {
        string message = p["message"]?.ToString() ?? "";
        string level = p["level"]?.ToString()?.ToLowerInvariant() ?? "info";

        switch (level)
        {
            case "warn":
                ResoniteMod.Warn($"[Bridge] {message}");
                break;
            case "error":
                ResoniteMod.Error($"[Bridge] {message}");
                break;
            default:
                ResoniteMod.Msg($"[Bridge] {message}");
                break;
        }

        return Ok(id, new JObject { ["logged"] = true, ["level"] = level });
    }

    // ─── Tracker ────────────────────────────────────────────────

    public JObject HandleClearTracker(string id)
    {
        _tracker.Clear();
        return Ok(id, new JObject { ["cleared"] = true });
    }

    // ─── Dynamic Variables ──────────────────────────────────────

    public JObject HandleCreateDynVarSpace(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string spaceName = p["spaceName"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var space = slot.AttachComponent<DynamicVariableSpace>();
        space.SpaceName.Value = spaceName;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["spaceName"] = spaceName,
            ["refId"] = space.ReferenceID.ToString()
        });
    }

    public JObject HandleCreateDynVar(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string varName = p["varName"]?.ToString();
        string varType = p["varType"]?.ToString()?.ToLowerInvariant() ?? "string";
        var initialValue = p["value"];

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        string refId;
        switch (varType)
        {
            case "string":
                var sv = slot.AttachComponent<DynamicValueVariable<string>>();
                sv.VariableName.Value = varName;
                if (initialValue != null) sv.Value.Value = initialValue.ToString();
                refId = sv.ReferenceID.ToString();
                break;
            case "bool":
                var bv = slot.AttachComponent<DynamicValueVariable<bool>>();
                bv.VariableName.Value = varName;
                if (initialValue != null) bv.Value.Value = initialValue.Value<bool>();
                refId = bv.ReferenceID.ToString();
                break;
            case "int":
                var iv = slot.AttachComponent<DynamicValueVariable<int>>();
                iv.VariableName.Value = varName;
                if (initialValue != null) iv.Value.Value = initialValue.Value<int>();
                refId = iv.ReferenceID.ToString();
                break;
            case "float":
                var fv = slot.AttachComponent<DynamicValueVariable<float>>();
                fv.VariableName.Value = varName;
                if (initialValue != null) fv.Value.Value = initialValue.Value<float>();
                refId = fv.ReferenceID.ToString();
                break;
            case "float3":
            {
                var f3v = slot.AttachComponent<DynamicValueVariable<float3>>();
                f3v.VariableName.Value = varName;
                if (initialValue is JArray f3a && f3a.Count == 3)
                    f3v.Value.Value = new float3(f3a[0].Value<float>(), f3a[1].Value<float>(), f3a[2].Value<float>());
                refId = f3v.ReferenceID.ToString();
                break;
            }
            case "colorx":
            {
                var cv = slot.AttachComponent<DynamicValueVariable<colorX>>();
                cv.VariableName.Value = varName;
                if (initialValue is JArray ca && ca.Count >= 3)
                {
                    float a = ca.Count >= 4 ? ca[3].Value<float>() : 1f;
                    cv.Value.Value = new colorX(ca[0].Value<float>(), ca[1].Value<float>(), ca[2].Value<float>(), a);
                }
                refId = cv.ReferenceID.ToString();
                break;
            }
            default:
                return Error(id, "UNSUPPORTED_TYPE", $"Unsupported DynVar type: {varType}. Use string, bool, int, float, float3, or colorX.");
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["varName"] = varName,
            ["varType"] = varType,
            ["refId"] = refId
        });
    }

    public JObject HandleReadDynVar(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string path = p["path"]?.ToString();
        string varType = p["type"]?.ToString()?.ToLowerInvariant() ?? "string";

        if (string.IsNullOrEmpty(path))
            return Error(id, "INVALID_PARAMS", "readDynVar requires 'path'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        DynamicVariableHelper.ParsePath(path, out string spaceName, out string variableName);
        var space = DynamicVariableHelper.FindSpace(slot, spaceName);
        if (space == null)
            return Error(id, "FIELD_NOT_FOUND", $"DynamicVariableSpace '{spaceName}' not found from slot '{slotName}'");

        JToken value;
        bool found;
        switch (varType)
        {
            case "string":
                found = space.TryReadValue<string>(variableName, out var svr);
                value = found ? (JToken)(svr ?? "") : JValue.CreateNull();
                break;
            case "bool":
                found = space.TryReadValue<bool>(variableName, out var bvr);
                value = found ? (JToken)bvr : JValue.CreateNull();
                break;
            case "int":
                found = space.TryReadValue<int>(variableName, out var ivr);
                value = found ? (JToken)ivr : JValue.CreateNull();
                break;
            case "float":
                found = space.TryReadValue<float>(variableName, out var fvr);
                value = found ? (JToken)fvr : JValue.CreateNull();
                break;
            case "float3":
                found = space.TryReadValue<float3>(variableName, out var f3vr);
                value = found ? new JArray(f3vr.x, f3vr.y, f3vr.z) : JValue.CreateNull();
                break;
            case "colorx":
                found = space.TryReadValue<colorX>(variableName, out var cvr);
                value = found ? new JArray(cvr.r, cvr.g, cvr.b, cvr.a) : JValue.CreateNull();
                break;
            default:
                return Error(id, "UNSUPPORTED_TYPE", $"Unsupported DynVar read type: {varType}. Use string, bool, int, float, float3, or colorX.");
        }

        if (!found)
            return Error(id, "FIELD_NOT_FOUND", $"Dynamic variable '{path}' not found or type mismatch");

        return Ok(id, new JObject
        {
            ["path"] = path,
            ["type"] = varType,
            ["value"] = value
        });
    }

    public JObject HandleWriteDynVar(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string path = p["path"]?.ToString();
        string varType = p["type"]?.ToString()?.ToLowerInvariant() ?? "string";
        var val = p["value"];

        if (string.IsNullOrEmpty(path))
            return Error(id, "INVALID_PARAMS", "writeDynVar requires 'path'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        switch (varType)
        {
            case "string":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.ToString() ?? "");
                break;
            case "bool":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.Value<bool>() ?? false);
                break;
            case "int":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.Value<int>() ?? 0);
                break;
            case "float":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.Value<float>() ?? 0f);
                break;
            case "float3":
            {
                var arr = val as JArray;
                if (arr == null || arr.Count != 3)
                    return Error(id, "INVALID_PARAMS", "float3 requires [x, y, z] array");
                DynamicVariableHelper.WriteDynamicVariable(slot, path,
                    new float3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>()));
                break;
            }
            case "colorx":
            {
                var arr = val as JArray;
                if (arr == null || arr.Count < 3)
                    return Error(id, "INVALID_PARAMS", "colorX requires [r, g, b, a] array");
                float a = arr.Count >= 4 ? arr[3].Value<float>() : 1f;
                DynamicVariableHelper.WriteDynamicVariable(slot, path,
                    new colorX(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), a));
                break;
            }
            default:
                return Error(id, "UNSUPPORTED_TYPE", $"Unsupported DynVar write type: {varType}. Use string, bool, int, float, float3, or colorX.");
        }

        return Ok(id, new JObject
        {
            ["path"] = path,
            ["type"] = varType,
            ["written"] = true
        });
    }

    // ─── Event Subscriptions ────────────────────────────────────

    public JObject HandleSubscribe(string id, JObject p)
    {
        if (_events == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Event system not initialized");
        return _events.Subscribe(id, p);
    }

    public JObject HandleUnsubscribe(string id, JObject p)
    {
        if (_events == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Event system not initialized");
        return _events.Unsubscribe(id, p);
    }

    public JObject HandleListSubscriptions(string id)
    {
        if (_events == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Event system not initialized");
        return _events.ListSubscriptions(id);
    }

    // ─── Templates ──────────────────────────────────────────────

    public JObject HandleSnapshotSlot(string id, JObject p)
    {
        if (_templates == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Template system not initialized");
        return _templates.SnapshotSlot(id, p);
    }

    public JObject HandleSaveTemplate(string id, JObject p)
    {
        if (_templates == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Template system not initialized");
        return _templates.SaveTemplate(id, p);
    }

    public JObject HandleStampTemplate(string id, JObject p)
    {
        if (_templates == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Template system not initialized");
        return _templates.StampTemplate(id, p);
    }

    public JObject HandleListTemplates(string id)
    {
        if (_templates == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Template system not initialized");
        return _templates.ListTemplates(id);
    }

    public JObject HandleDeleteTemplate(string id, JObject p)
    {
        if (_templates == null)
            return Error(id, "SYSTEM_NOT_INITIALIZED", "Template system not initialized");
        return _templates.DeleteTemplate(id, p);
    }

    // ─── Batch Operations ───────────────────────────────────────

    public JObject HandleMeasureDistance(string id, JObject p)
    {
        string slotA = p["slotA"]?.ToString();
        string slotB = p["slotB"]?.ToString();

        var a = _tracker.Get(slotA);
        if (a == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotA}' not found");

        var b = _tracker.Get(slotB);
        if (b == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotB}' not found");

        var posA = a.GlobalPosition;
        var posB = b.GlobalPosition;
        var diff = posB - posA;
        float distance = MathX.Distance(posA, posB);

        return Ok(id, new JObject
        {
            ["slotA"] = slotA,
            ["slotB"] = slotB,
            ["distance"] = distance,
            ["positionA"] = new JArray(posA.x, posA.y, posA.z),
            ["positionB"] = new JArray(posB.x, posB.y, posB.z),
            ["delta"] = new JArray(diff.x, diff.y, diff.z)
        });
    }

    public JObject HandleSetFieldOnChildren(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        var value = p["value"];
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Type componentType = ComponentRegistry.Resolve(componentName);
        if (componentType == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"Component type '{componentName}' not found");

        int successCount = 0;
        int errorCount = 0;
        SetFieldOnChildrenRecursive(slot, componentType, fieldName, value, 0, maxDepth, ref successCount, ref errorCount);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["field"] = fieldName,
            ["modified"] = successCount,
            ["errors"] = errorCount
        });
    }

    private void SetFieldOnChildrenRecursive(Slot slot, Type componentType, string fieldName, JToken value,
        int depth, int maxDepth, ref int successCount, ref int errorCount)
    {
        foreach (var comp in slot.Components)
        {
            if (componentType.IsAssignableFrom(comp.GetType()))
            {
                try
                {
                    FieldParser.SetFieldValue(comp, fieldName, value, _tracker);
                    successCount++;
                }
                catch
                {
                    errorCount++;
                }
            }
        }

        if (maxDepth == -1 || depth < maxDepth)
        {
            foreach (var child in slot.Children)
                SetFieldOnChildrenRecursive(child, componentType, fieldName, value, depth + 1, maxDepth, ref successCount, ref errorCount);
        }
    }

    public JObject HandleDuplicateSlotArray(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int count = p["count"]?.Value<int>() ?? 3;
        var spacingArr = p["spacing"] as JArray;
        string trackPrefix = p["trackPrefix"]?.ToString();

        if (count < 1 || count > 100)
            return Error(id, "OUT_OF_RANGE", "Count must be between 1 and 100");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        float3 spacing = new float3(1, 0, 0);
        if (spacingArr != null && spacingArr.Count >= 3)
            spacing = new float3(spacingArr[0].Value<float>(), spacingArr[1].Value<float>(), spacingArr[2].Value<float>());

        var copies = new JArray();
        for (int i = 0; i < count; i++)
        {
            var copy = slot.Duplicate();
            copy.LocalPosition += spacing * (i + 1);

            string copyName = trackPrefix != null ? $"{trackPrefix}_{i}" : $"{slot.Name}_copy_{i}";
            copy.Name = copyName;
            _tracker.Register(copyName, copy);

            copies.Add(new JObject
            {
                ["name"] = copyName,
                ["refId"] = copy.ReferenceID.ToString(),
                ["position"] = new JArray(copy.LocalPosition.x, copy.LocalPosition.y, copy.LocalPosition.z)
            });
        }

        return Ok(id, new JObject
        {
            ["sourceSlot"] = slotName,
            ["count"] = count,
            ["spacing"] = new JArray(spacing.x, spacing.y, spacing.z),
            ["copies"] = copies
        });
    }
}
