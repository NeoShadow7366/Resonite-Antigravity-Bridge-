using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handles reading and writing field values on FrooxEngine components.
/// Supports all common types: string, bool, int, float, float2/3/4, floatQ, colorX, Uri, enums, SyncRef.
/// </summary>
internal static class FieldParser
{
    /// <summary>
    /// Set a field value on a component by sync member name.
    /// Throws on unsupported types or missing fields.
    /// </summary>
    public static void SetFieldValue(Component component, string fieldName, JToken value, SlotTracker tracker)
    {
        var member = component.GetSyncMember(fieldName);
        if (member == null)
            throw new Exception($"Field '{fieldName}' not found on {component.GetType().Name}");

        switch (member)
        {
            case Sync<string> sf:
                sf.Value = value.ToString();
                break;
            case Sync<bool> bf:
                bf.Value = value.Value<bool>();
                break;
            case Sync<byte> byf:
                byf.Value = value.Value<byte>();
                break;
            case Sync<short> shf:
                shf.Value = value.Value<short>();
                break;
            case Sync<ushort> ushf:
                ushf.Value = value.Value<ushort>();
                break;
            case Sync<int> nf:
                nf.Value = value.Value<int>();
                break;
            case Sync<uint> uif:
                uif.Value = value.Value<uint>();
                break;
            case Sync<long> lf:
                lf.Value = value.Value<long>();
                break;
            case Sync<double> df:
                df.Value = value.Value<double>();
                break;
            case Sync<float> ff:
                ff.Value = value.Value<float>();
                break;
            case Sync<float2> f2f:
                var arr2 = value as JArray;
                if (arr2 != null && arr2.Count == 2)
                    f2f.Value = new float2(arr2[0].Value<float>(), arr2[1].Value<float>());
                break;
            case Sync<float3> f3f:
                var arr3 = value as JArray;
                if (arr3 != null && arr3.Count == 3)
                    f3f.Value = new float3(arr3[0].Value<float>(), arr3[1].Value<float>(), arr3[2].Value<float>());
                break;
            case Sync<float4> f4f:
                var arr4 = value as JArray;
                if (arr4 != null && arr4.Count == 4)
                    f4f.Value = new float4(arr4[0].Value<float>(), arr4[1].Value<float>(), arr4[2].Value<float>(), arr4[3].Value<float>());
                break;
            case Sync<floatQ> qf:
                var arrQ = value as JArray;
                if (arrQ != null && arrQ.Count == 4)
                    qf.Value = new floatQ(arrQ[0].Value<float>(), arrQ[1].Value<float>(), arrQ[2].Value<float>(), arrQ[3].Value<float>());
                else if (arrQ != null && arrQ.Count == 3)
                    qf.Value = floatQ.Euler(arrQ[0].Value<float>(), arrQ[1].Value<float>(), arrQ[2].Value<float>());
                break;
            case Sync<int2> i2f:
                var arri2 = value as JArray;
                if (arri2 != null && arri2.Count == 2)
                    i2f.Value = new int2(arri2[0].Value<int>(), arri2[1].Value<int>());
                break;
            case Sync<int3> i3f:
                var arri3 = value as JArray;
                if (arri3 != null && arri3.Count == 3)
                    i3f.Value = new int3(arri3[0].Value<int>(), arri3[1].Value<int>(), arri3[2].Value<int>());
                break;
            case Sync<Rect> rf:
                var arrR = value as JArray;
                if (arrR != null && arrR.Count == 4)
                    rf.Value = new Rect(arrR[0].Value<float>(), arrR[1].Value<float>(), arrR[2].Value<float>(), arrR[3].Value<float>());
                break;
            case Sync<Uri> uf:
                uf.Value = new Uri(value.ToString());
                break;
            case Sync<colorX> cf:
                var arrC = value as JArray;
                if (arrC != null && arrC.Count >= 3)
                {
                    float r = arrC[0].Value<float>();
                    float g = arrC[1].Value<float>();
                    float b = arrC[2].Value<float>();
                    float a = arrC.Count > 3 ? arrC[3].Value<float>() : 1f;
                    cf.Value = new colorX(r, g, b, a);
                }
                else if (value.Type == JTokenType.String)
                {
                    string hex = value.ToString();
                    if (colorX.TryParse(hex, out var parsedColor))
                        cf.Value = parsedColor;
                }
                break;
            default:
                // Try SyncRef (reference fields) via ISyncRef interface
                if (member is ISyncRef syncRefW)
                {
                    string refValue = value.ToString();

                    // Try clearing the reference
                    if (string.IsNullOrEmpty(refValue) || refValue == "null")
                    {
                        syncRefW.Clear();
                        break;
                    }

                    // Try resolving as a tracked slot name first
                    var trackedSlot = tracker.Get(refValue);
                    if (trackedSlot != null)
                    {
                        if (!syncRefW.TrySet(trackedSlot))
                            throw new Exception($"Type mismatch: cannot set {syncRefW.TargetType.Name} reference to Slot");
                        break;
                    }

                    // Try resolving as a RefID (numeric or hex)
                    var world = Engine.Current?.WorldManager?.FocusedWorld;
                    if (world != null)
                    {
                        IWorldElement target = null;

                        if (ulong.TryParse(refValue, out var rawId))
                            target = world.ReferenceController.GetObjectOrNull(new RefID(rawId));

                        if (target == null && ulong.TryParse(refValue,
                            System.Globalization.NumberStyles.HexNumber, null, out var hexId))
                            target = world.ReferenceController.GetObjectOrNull(new RefID(hexId));

                        if (target != null)
                        {
                            if (!syncRefW.TrySet(target))
                                throw new Exception($"Type mismatch: cannot set {syncRefW.TargetType.Name} reference to {target.GetType().Name}");
                            break;
                        }
                    }

                    throw new Exception($"Could not resolve reference '{refValue}' for field '{fieldName}'. Use a tracked slot name, RefID number, or 'null' to clear.");
                }

                // Try to handle enum fields via reflection
                var memberType = member.GetType();
                if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Sync<>))
                {
                    var valueType = memberType.GetGenericArguments()[0];
                    if (valueType.IsEnum)
                    {
                        var enumValue = Enum.Parse(valueType, value.ToString(), ignoreCase: true);
                        var valueProp = memberType.GetProperty("Value");
                        valueProp.SetValue(member, enumValue);
                        break;
                    }
                }
                throw new Exception($"Unsupported field type: {member.GetType().Name} for field '{fieldName}'");
        }
    }

    /// <summary>
    /// Read a field value from a component, returning a JSON-compatible value.
    /// Returns the value in the same format as SetFieldValue expects.
    /// </summary>
    public static JToken ReadFieldValue(ISyncMember member)
    {
        switch (member)
        {
            case Sync<string> sf: return sf.Value;
            case Sync<bool> bf: return bf.Value;
            case Sync<byte> byf: return byf.Value;
            case Sync<short> shf: return shf.Value;
            case Sync<ushort> ushf: return ushf.Value;
            case Sync<int> nf: return nf.Value;
            case Sync<uint> uif: return uif.Value;
            case Sync<long> lf: return lf.Value;
            case Sync<double> df: return df.Value;
            case Sync<float> ff: return ff.Value;
            case Sync<float2> f2f: return new JArray(f2f.Value.x, f2f.Value.y);
            case Sync<float3> f3f: return new JArray(f3f.Value.x, f3f.Value.y, f3f.Value.z);
            case Sync<float4> f4f: return new JArray(f4f.Value.x, f4f.Value.y, f4f.Value.z, f4f.Value.w);
            case Sync<floatQ> qf: return new JArray(qf.Value.x, qf.Value.y, qf.Value.z, qf.Value.w);
            case Sync<int2> i2f: return new JArray(i2f.Value.x, i2f.Value.y);
            case Sync<int3> i3f: return new JArray(i3f.Value.x, i3f.Value.y, i3f.Value.z);
            case Sync<Rect> rf: return new JArray(rf.Value.x, rf.Value.y, rf.Value.width, rf.Value.height);
            case Sync<Uri> uf: return uf.Value?.ToString();
            case Sync<colorX> cf: return new JArray(cf.Value.r, cf.Value.g, cf.Value.b, cf.Value.a);
            default:
                // SyncRef
                if (member is ISyncRef syncRef)
                {
                    var target = syncRef.Target;
                    if (target == null) return null;
                    return new JObject
                    {
                        ["refId"] = (target as IWorldElement)?.ReferenceID.ToString(),
                        ["type"] = target.GetType().Name,
                        ["name"] = (target as Slot)?.Name
                    };
                }

                // Enum via reflection
                var mType = member.GetType();
                if (mType.IsGenericType && mType.GetGenericTypeDefinition() == typeof(Sync<>))
                {
                    var valProp = mType.GetProperty("Value");
                    if (valProp != null)
                    {
                        var val = valProp.GetValue(member);
                        if (val != null && val.GetType().IsEnum)
                            return val.ToString();
                    }
                }

                return $"<unsupported:{member.GetType().Name}>";
        }
    }

    /// <summary>
    /// Navigate a dotted field path like "Materials._elements.0" to reach nested sync members.
    /// Returns the final member or null.
    /// </summary>
    public static ISyncMember NavigateToField(Component component, string fieldPath)
    {
        if (!fieldPath.Contains('.'))
            return component.GetSyncMember(fieldPath);

        var parts = fieldPath.Split('.');
        ISyncMember current = component.GetSyncMember(parts[0]);

        for (int i = 1; i < parts.Length && current != null; i++)
        {
            var type = current.GetType();

            // Try indexed access (for lists/arrays)
            if (int.TryParse(parts[i], out int index))
            {
                var indexer = type.GetProperty("Item");
                if (indexer != null)
                {
                    try
                    {
                        var item = indexer.GetValue(current, new object[] { index });
                        current = item as ISyncMember;
                        continue;
                    }
                    catch { return null; }
                }
            }

            // Try property access
            var prop = type.GetProperty(parts[i]);
            if (prop != null)
            {
                var val = prop.GetValue(current);
                current = val as ISyncMember;
                continue;
            }

            // Try field access
            var field = type.GetField(parts[i]);
            if (field != null)
            {
                var val = field.GetValue(current);
                current = val as ISyncMember;
                continue;
            }

            // Try GetSyncMember on Worker
            if (current is Worker worker)
            {
                current = worker.GetSyncMember(parts[i]);
                continue;
            }

            return null;
        }

        return current;
    }
}
