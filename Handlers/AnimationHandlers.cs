using System;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handlers for creating keyframe-driven animations using ValueGradientDriver.
/// </summary>
internal class AnimationHandlers : HandlerBase
{
    public AnimationHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleCreateAnimation(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string targetComponent = p["targetComponent"]?.ToString();
        string targetField = p["targetField"]?.ToString();
        string valueType = p["type"]?.ToString() ?? "float";
        float duration = p["duration"]?.Value<float>() ?? 1.0f;
        bool loop = p["loop"]?.Value<bool>() ?? false;
        var keyframes = p["keyframes"] as JArray;
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        if (string.IsNullOrEmpty(slotName))
            return Error(id, "INVALID_PARAMS", "createAnimation requires 'slot'");
        if (string.IsNullOrEmpty(targetComponent))
            return Error(id, "INVALID_PARAMS", "createAnimation requires 'targetComponent'");
        if (string.IsNullOrEmpty(targetField))
            return Error(id, "INVALID_PARAMS", "createAnimation requires 'targetField'");
        if (keyframes == null || keyframes.Count < 2)
            return Error(id, "INVALID_PARAMS", "createAnimation requires 'keyframes' array with at least 2 entries [{time, value}, ...]");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (comp, error) = ResolveComponent(slot, targetComponent, componentIndex, id);
        if (error != null) return error;

        var targetMember = comp.GetSyncMember(targetField);
        if (targetMember == null)
            return Error(id, "FIELD_NOT_FOUND", $"Field '{targetField}' not found on component '{targetComponent}'");

        string driverTypeName = $"ValueGradientDriver<{valueType}>";
        Type driverType = ComponentRegistry.Resolve(driverTypeName);
        if (driverType == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"Cannot resolve animation driver type: '{driverTypeName}'");

        Component driver;
        try
        {
            driver = slot.AttachComponent(driverType);
        }
        catch (Exception ex)
        {
            return Error(id, "COMPONENT_ATTACH_FAILED", $"Failed to attach {driverTypeName}: {ex.Message}");
        }

        // Wire the Target field to the target member
        var driverTargetMember = driver.GetSyncMember("Target");
        if (driverTargetMember is ISyncRef targetRef)
        {
            targetRef.TrySet(targetMember);
        }

        // Add keyframe points
        var pointsMember = driver.GetSyncMember("Points");
        if (pointsMember != null)
        {
            var pointsType = pointsMember.GetType();
            var addMethod = pointsType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);

            if (addMethod != null)
            {
                foreach (var kf in keyframes)
                {
                    float time = kf["time"]?.Value<float>() ?? 0f;
                    var valueToken = kf["value"];

                    try
                    {
                        var point = addMethod.Invoke(pointsMember, null);
                        if (point != null)
                        {
                            var positionProp = point.GetType().GetProperty("Position");
                            if (positionProp != null)
                            {
                                var posMember = positionProp.GetValue(point);
                                if (posMember != null)
                                {
                                    var valueProp = posMember.GetType().GetProperty("Value");
                                    valueProp?.SetValue(posMember, time);
                                }
                            }

                            var valuePropInfo = point.GetType().GetProperty("Value");
                            if (valuePropInfo != null)
                            {
                                var valMember = valuePropInfo.GetValue(point);
                                if (valMember != null)
                                {
                                    var valValueProp = valMember.GetType().GetProperty("Value");
                                    if (valValueProp != null && valueToken != null)
                                    {
                                        object parsedValue = ParseValueForType(valueType, valueToken);
                                        if (parsedValue != null)
                                            valValueProp.SetValue(valMember, parsedValue);
                                    }
                                }
                            }
                        }
                    }
                    catch { /* Skip problematic keyframes */ }
                }
            }
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["driverType"] = driverTypeName,
            ["driverRefId"] = driver.ReferenceID.ToString(),
            ["targetComponent"] = targetComponent,
            ["targetField"] = targetField,
            ["keyframeCount"] = keyframes.Count,
            ["duration"] = duration,
            ["loop"] = loop,
            ["note"] = "Drive the 'Progress' field (0.0→1.0) to animate. Use a Panner1D or ProtoFlux to drive it continuously."
        });
    }

    /// <summary>Parse a JToken value into the correct CLR type for animation keyframes</summary>
    private object ParseValueForType(string typeName, JToken value)
    {
        return typeName.ToLowerInvariant() switch
        {
            "float" => value.Value<float>(),
            "double" => value.Value<double>(),
            "int" => value.Value<int>(),
            "bool" => value.Value<bool>(),
            "float2" => new float2(
                value is JArray a2 ? a2[0].Value<float>() : value.Value<float>(),
                value is JArray a2b ? a2b[1].Value<float>() : 0f),
            "float3" => value is JArray a3
                ? new float3(a3[0].Value<float>(), a3[1].Value<float>(), a3[2].Value<float>())
                : new float3(value.Value<float>()),
            "float4" => value is JArray a4
                ? new float4(a4[0].Value<float>(), a4[1].Value<float>(), a4[2].Value<float>(), a4[3].Value<float>())
                : new float4(value.Value<float>()),
            "colorx" or "color" => value is JArray ac
                ? new colorX(ac[0].Value<float>(), ac[1].Value<float>(), ac[2].Value<float>(),
                    ac.Count >= 4 ? ac[3].Value<float>() : 1f)
                : new colorX(value.Value<float>()),
            _ => value.Value<float>() // fallback to float
        };
    }
}
