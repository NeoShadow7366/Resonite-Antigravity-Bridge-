using System;
using System.Linq;
using System.Reflection;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handlers for ProtoFlux visual programming: node creation, wiring, input setting, and inspection.
/// </summary>
internal class ProtoFluxHandlers : HandlerBase
{
    // Cache the ProtoFluxBindings assembly for type resolution
    private static Assembly _protoFluxBindingsAsm;
    private static Assembly ProtoFluxBindingsAssembly
    {
        get
        {
            if (_protoFluxBindingsAsm == null)
            {
                _protoFluxBindingsAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "ProtoFluxBindings");
            }
            return _protoFluxBindingsAsm;
        }
    }

    public ProtoFluxHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleCreateProtoFluxNode(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string nodeType = p["nodeType"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();

        var parentSlot = _tracker.Get(slotName);
        if (parentSlot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Type type = ResolveProtoFluxNodeType(nodeType);
        if (type == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"ProtoFlux node type '{nodeType}' not found. Use full path like 'FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators.ValueAdd`1[System.Single]' or short name like 'If', 'For', 'ButtonEvents'");

        if (!typeof(Component).IsAssignableFrom(type))
            return Error(id, "TYPE_MISMATCH", $"Type '{nodeType}' is not a Component/ProtoFluxNode");

        string nodeName = type.Name.Split('`')[0];
        var nodeSlot = parentSlot.AddSlot(nodeName);

        var component = nodeSlot.AttachComponent(type);
        if (component == null)
        {
            nodeSlot.Destroy();
            return Error(id, "COMPONENT_ATTACH_FAILED", $"Failed to attach ProtoFlux node '{nodeType}'");
        }

        if (!string.IsNullOrEmpty(trackAs))
            _tracker.Register(trackAs, nodeSlot);
        else
            _tracker.Register(nodeName, nodeSlot);

        var members = new JArray();
        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            var member = component.GetSyncMember(i);
            var memberName = component.GetSyncMemberName(i);
            members.Add(new JObject
            {
                ["name"] = memberName,
                ["type"] = member?.GetType()?.Name ?? "unknown",
                ["memberIndex"] = i
            });
        }

        return Ok(id, new JObject
        {
            ["slotName"] = nodeSlot.Name,
            ["refId"] = nodeSlot.ReferenceID.ToString(),
            ["componentRefId"] = component.ReferenceID.ToString(),
            ["nodeType"] = type.FullName,
            ["trackedAs"] = trackAs ?? nodeName,
            ["members"] = members
        });
    }

    public JObject HandleConnectProtoFlux(string id, JObject p)
    {
        string sourceSlotName = p["sourceSlot"]?.ToString();
        string sourceOutputName = p["sourceOutput"]?.ToString();
        string targetSlotName = p["targetSlot"]?.ToString();
        string targetInputName = p["targetInput"]?.ToString();
        string sourceComponentName = p["sourceComponent"]?.ToString();
        string targetComponentName = p["targetComponent"]?.ToString();

        var sourceSlot = _tracker.Get(sourceSlotName);
        if (sourceSlot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Source slot '{sourceSlotName}' not found");

        var targetSlot = _tracker.Get(targetSlotName);
        if (targetSlot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Target slot '{targetSlotName}' not found");

        Component sourceComponent = FindProtoFluxComponent(sourceSlot, sourceComponentName);
        if (sourceComponent == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"No ProtoFlux node component found on source slot '{sourceSlotName}'" +
                (sourceComponentName != null ? $" matching '{sourceComponentName}'" : ""));

        Component targetComponent = FindProtoFluxComponent(targetSlot, targetComponentName);
        if (targetComponent == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"No ProtoFlux node component found on target slot '{targetSlotName}'" +
                (targetComponentName != null ? $" matching '{targetComponentName}'" : ""));

        var sourceOutput = sourceComponent.GetSyncMember(sourceOutputName);
        if (sourceOutput == null)
            return Error(id, "FIELD_NOT_FOUND", $"Source output '{sourceOutputName}' not found on component {sourceComponent.GetType().Name}");

        var targetInput = targetComponent.GetSyncMember(targetInputName);
        if (targetInput == null)
            return Error(id, "FIELD_NOT_FOUND", $"Target input '{targetInputName}' not found on component {targetComponent.GetType().Name}");

        if (targetInput is ISyncRef syncRef)
        {
            bool success = syncRef.TrySet(sourceOutput as IWorldElement);
            if (!success)
                return Error(id, "TYPE_MISMATCH", $"Failed to wire: type mismatch between source output '{sourceOutputName}' ({sourceOutput.GetType().Name}) and target input '{targetInputName}' ({targetInput.GetType().Name})");

            return Ok(id, new JObject
            {
                ["sourceSlot"] = sourceSlotName,
                ["sourceOutput"] = sourceOutputName,
                ["targetSlot"] = targetSlotName,
                ["targetInput"] = targetInputName,
                ["sourceType"] = sourceOutput.GetType().Name,
                ["targetType"] = targetInput.GetType().Name
            });
        }
        else
        {
            return Error(id, "TYPE_MISMATCH", $"Target input '{targetInputName}' is not a reference field (type: {targetInput.GetType().Name}). It must be a SyncRef<> to accept a wire connection.");
        }
    }

    public JObject HandleSetProtoFluxInput(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string fieldName = p["field"]?.ToString();
        var value = p["value"];
        string componentName = p["component"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Component component = FindProtoFluxComponent(slot, componentName);
        if (component == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"No ProtoFlux node component found on slot '{slotName}'");

        try
        {
            FieldParser.SetFieldValue(component, fieldName, value, _tracker);
            return Ok(id, new JObject
            {
                ["slot"] = slotName,
                ["component"] = component.GetType().Name,
                ["field"] = fieldName,
                ["value"] = value
            });
        }
        catch (Exception ex)
        {
            return Error(id, "FIELD_SET_FAILED", $"Failed to set ProtoFlux input '{fieldName}': {ex.Message}");
        }
    }

    public JObject HandleGetProtoFluxNode(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Component component = FindProtoFluxComponent(slot, componentName);
        if (component == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"No ProtoFlux node component found on slot '{slotName}'");

        var inputs = new JArray();
        var outputs = new JArray();
        var other = new JArray();

        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            var member = component.GetSyncMember(i);
            var memberName = component.GetSyncMemberName(i);
            var memberType = member?.GetType()?.Name ?? "unknown";

            var info = new JObject
            {
                ["name"] = memberName,
                ["type"] = memberType,
                ["memberIndex"] = i
            };

            if (memberType.Contains("SyncRef"))
            {
                if (member is ISyncRef sr && sr.Target != null)
                {
                    info["connected"] = true;
                    info["targetRefId"] = (sr.Target as IWorldElement)?.ReferenceID.ToString();
                }
                else
                {
                    info["connected"] = false;
                }
                inputs.Add(info);
            }
            else if (memberType.Contains("NodeValueOutput") || memberType.Contains("NodeObjectOutput"))
            {
                info["refId"] = (member as IWorldElement)?.ReferenceID.ToString();
                outputs.Add(info);
            }
            else
            {
                try
                {
                    info["value"] = FieldParser.ReadFieldValue(member)?.ToString();
                }
                catch { }
                other.Add(info);
            }
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["slotRefId"] = slot.ReferenceID.ToString(),
            ["nodeType"] = component.GetType().FullName,
            ["componentRefId"] = component.ReferenceID.ToString(),
            ["inputs"] = inputs,
            ["outputs"] = outputs,
            ["other"] = other
        });
    }

    // ─── Helpers ────────────────────────────────────────────────

    /// <summary>Resolve a ProtoFlux node binding type by name.</summary>
    private Type ResolveProtoFluxNodeType(string nodeType)
    {
        if (string.IsNullOrEmpty(nodeType)) return null;

        var bindingsAsm = ProtoFluxBindingsAssembly;
        if (bindingsAsm == null) return null;

        var type = bindingsAsm.GetType(nodeType, false, true);
        if (type != null) return type;

        var prefixes = new[]
        {
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Actions.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Strings.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Variables.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Interaction.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Slots.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Avatar.",
            "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Users.",
            "FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.",
        };

        foreach (var prefix in prefixes)
        {
            type = bindingsAsm.GetType(prefix + nodeType, false, true);
            if (type != null) return type;
        }

        type = typeof(Slot).Assembly.GetType("FrooxEngine.ProtoFlux.CoreNodes." + nodeType, false, true);
        if (type != null) return type;

        return null;
    }

    /// <summary>Find a ProtoFlux node component on a slot. If componentName is specified, filter by it.</summary>
    private Component FindProtoFluxComponent(Slot slot, string componentName)
    {
        foreach (var comp in slot.Components)
        {
            if (comp is ProtoFluxNode)
            {
                if (string.IsNullOrEmpty(componentName) || comp.GetType().Name.Contains(componentName, StringComparison.OrdinalIgnoreCase))
                    return comp;
            }
        }

        foreach (var comp in slot.Components)
        {
            var ns = comp.GetType().FullName ?? "";
            if (ns.Contains("ProtoFlux"))
            {
                if (string.IsNullOrEmpty(componentName) || comp.GetType().Name.Contains(componentName, StringComparison.OrdinalIgnoreCase))
                    return comp;
            }
        }

        return null;
    }
}
