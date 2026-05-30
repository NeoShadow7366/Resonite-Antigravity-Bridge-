using System;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handlers for wiring references (SyncRef fields), adding/removing list elements.
/// </summary>
internal class ReferenceHandlers : HandlerBase
{
    public ReferenceHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleWireReference(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        string targetRefIdStr = p["targetRefId"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null) return error;

        // Find the field (supports dotted paths like Materials._elements.0)
        var member = component.GetSyncMember(fieldName);
        if (member == null)
        {
            member = ResolveDottedMember(component, fieldName);
        }

        if (member == null)
            return Error(id, "FIELD_NOT_FOUND", $"Field '{fieldName}' not found on component '{componentName}'");

        if (member is not ISyncRef syncRef)
            return Error(id, "TYPE_MISMATCH", $"Field '{fieldName}' is not a reference field (ISyncRef). It is: {member.GetType().Name}");

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        IWorldElement target = null;

        try
        {
            if (ulong.TryParse(targetRefIdStr, out var rawId))
            {
                var refId = new RefID(rawId);
                target = world.ReferenceController.GetObjectOrNull(refId);
            }
            else if (targetRefIdStr.StartsWith("S-") || targetRefIdStr.StartsWith("C-") ||
                     targetRefIdStr.StartsWith("I-"))
            {
                if (ulong.TryParse(targetRefIdStr[2..], out var prefixedId))
                {
                    var refId = new RefID(prefixedId);
                    target = world.ReferenceController.GetObjectOrNull(refId);
                }
            }

            if (target == null && ulong.TryParse(targetRefIdStr,
                System.Globalization.NumberStyles.HexNumber, null, out var hexId))
            {
                var refId = new RefID(hexId);
                target = world.ReferenceController.GetObjectOrNull(refId);
            }
        }
        catch { /* fall through */ }

        // Fallback: try tracker lookup
        if (target == null)
        {
            var targetSlot = _tracker.Get(targetRefIdStr);
            if (targetSlot != null)
                target = targetSlot;
        }

        if (target == null)
            return Error(id, "REF_NOT_FOUND", $"Target '{targetRefIdStr}' could not be resolved as a RefID or tracked slot name");

        try
        {
            syncRef.TrySet(target);
        }
        catch (Exception ex)
        {
            return Error(id, "OPERATION_FAILED", $"Failed to wire reference: {ex.Message}. Target type: {target.GetType().Name}, Field expects: {syncRef.GetType().GenericTypeArguments?.FirstOrDefault()?.Name ?? "unknown"}");
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["field"] = fieldName,
            ["targetRefId"] = targetRefIdStr,
            ["targetType"] = target.GetType().Name,
            ["wired"] = true
        });
    }

    public JObject HandleAddToList(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        string targetRefId = p["targetRefId"]?.ToString();
        string value = p["value"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null) return error;

        var member = component.GetSyncMember(fieldName);
        if (member == null)
            return Error(id, "FIELD_NOT_FOUND", $"Field '{fieldName}' not found on component '{componentName}'");

        var memberType = member.GetType();

        var addMethod = memberType.GetMethod("Add", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (addMethod == null)
        {
            addMethod = memberType.GetMethod("AddUnique", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        }

        if (addMethod == null)
            return Error(id, "UNSUPPORTED_TYPE", $"Field '{fieldName}' does not appear to be a list type (no Add method). Type: {memberType.Name}");

        // For reference lists, add by RefID
        if (!string.IsNullOrEmpty(targetRefId))
        {
            var world = GetFocusedWorld();
            IWorldElement target = null;

            if (ulong.TryParse(targetRefId, out var rawId))
                target = world?.ReferenceController.GetObjectOrNull(new RefID(rawId));
            if (target == null && ulong.TryParse(targetRefId,
                System.Globalization.NumberStyles.HexNumber, null, out var hexId))
                target = world?.ReferenceController.GetObjectOrNull(new RefID(hexId));
            if (target == null)
            {
                var trackedSlot = _tracker.Get(targetRefId);
                if (trackedSlot != null) target = trackedSlot;
            }

            if (target == null)
                return Error(id, "REF_NOT_FOUND", $"Target '{targetRefId}' not found");

            try
            {
                var result = addMethod.Invoke(member, null);
                if (result is ISyncRef syncRef)
                    syncRef.TrySet(target);

                return Ok(id, new JObject
                {
                    ["slot"] = slotName,
                    ["component"] = componentName,
                    ["field"] = fieldName,
                    ["added"] = targetRefId,
                    ["targetType"] = target.GetType().Name
                });
            }
            catch (Exception ex)
            {
                return Error(id, "OPERATION_FAILED", $"Failed to add to list: {ex.Message}");
            }
        }

        // For value lists, add a value
        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                addMethod.Invoke(member, null);
                return Ok(id, new JObject
                {
                    ["slot"] = slotName,
                    ["component"] = componentName,
                    ["field"] = fieldName,
                    ["addedValue"] = value
                });
            }
            catch (Exception ex)
            {
                return Error(id, "OPERATION_FAILED", $"Failed to add value to list: {ex.Message}");
            }
        }

        return Error(id, "INVALID_PARAMS", "addToList requires either 'targetRefId' (for reference lists) or 'value' (for value lists)");
    }

    public JObject HandleRemoveFromList(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        int index = p["index"]?.Value<int>() ?? -1;
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null) return error;

        var member = component.GetSyncMember(fieldName);
        if (member == null)
            return Error(id, "FIELD_NOT_FOUND", $"Field '{fieldName}' not found on component '{componentName}'");

        var memberType = member.GetType();

        var countProp = memberType.GetProperty("Count");
        if (countProp == null)
            return Error(id, "UNSUPPORTED_TYPE", $"Field '{fieldName}' is not a list type. Type: {memberType.Name}");

        int count = (int)countProp.GetValue(member);

        if (index < 0) index = count - 1;
        if (index < 0 || index >= count)
            return Error(id, "OUT_OF_RANGE", $"Index {index} out of range. List has {count} item(s).");

        var removeAtMethod = memberType.GetMethod("RemoveAt", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (removeAtMethod == null)
            return Error(id, "UNSUPPORTED_TYPE", $"Field '{fieldName}' does not support RemoveAt. Type: {memberType.Name}");

        try
        {
            removeAtMethod.Invoke(member, new object[] { index });
            return Ok(id, new JObject
            {
                ["slot"] = slotName,
                ["component"] = componentName,
                ["field"] = fieldName,
                ["removedIndex"] = index,
                ["remainingCount"] = count - 1
            });
        }
        catch (Exception ex)
        {
            return Error(id, "OPERATION_FAILED", $"Failed to remove from list: {ex.Message}");
        }
    }

    // ─── Helpers ────────────────────────────────────────────────

    /// <summary>Navigate dotted field paths like "Materials._elements.0"</summary>
    private ISyncMember ResolveDottedMember(Component component, string dottedPath)
    {
        if (!dottedPath.Contains('.')) return null;

        var parts = dottedPath.Split('.');
        ISyncMember current = component.GetSyncMember(parts[0]);
        if (current == null) return null;

        for (int i = 1; i < parts.Length; i++)
        {
            var type = current.GetType();
            var prop = type.GetProperty(parts[i]);
            if (prop != null)
            {
                var val = prop.GetValue(current);
                if (val is ISyncMember subMember)
                {
                    current = subMember;
                    continue;
                }
            }

            if (int.TryParse(parts[i], out var idx))
            {
                var indexer = type.GetProperty("Item");
                if (indexer != null)
                {
                    try
                    {
                        var val = indexer.GetValue(current, new object[] { idx });
                        if (val is ISyncMember subMember)
                        {
                            current = subMember;
                            continue;
                        }
                    }
                    catch { /* fall through */ }
                }
            }

            return null; // Could not navigate further
        }

        return current;
    }
}
