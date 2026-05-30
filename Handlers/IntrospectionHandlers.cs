using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handlers for type introspection and discovery commands:
/// describeComponentType, searchComponents, getFieldType.
/// These help AI agents and developers understand the Resonite type system
/// without needing to attach components first.
/// </summary>
internal class IntrospectionHandlers : HandlerBase
{
    public IntrospectionHandlers(SlotTracker tracker) : base(tracker) { }

    /// <summary>
    /// Describe all fields on a component TYPE (not an instance).
    /// Uses reflection to enumerate ISyncMember fields and properties.
    /// </summary>
    public JObject HandleDescribeComponentType(string id, JObject p)
    {
        string typeName = p["type"]?.ToString();
        if (string.IsNullOrEmpty(typeName))
            return Error(id, "INVALID_PARAMS", "describeComponentType requires 'type'");

        var type = ComponentRegistry.Resolve(typeName);
        if (type == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"Component type '{typeName}' not found. Use 'searchComponents' to find available types.");

        var fields = new JArray();
        var seen = new HashSet<string>();

        // Scan C# fields (Resonite declares sync members as fields: public readonly Sync<T> FieldName)
        foreach (var fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
        {
            if (!typeof(ISyncMember).IsAssignableFrom(fi.FieldType))
                continue;
            if (!seen.Add(fi.Name)) continue;

            fields.Add(BuildFieldDescriptor(fi.Name, fi.FieldType));
        }

        // Also scan properties (some base Component members are properties)
        foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(ISyncMember).IsAssignableFrom(pi.PropertyType))
                continue;
            if (!seen.Add(pi.Name)) continue;

            fields.Add(BuildFieldDescriptor(pi.Name, pi.PropertyType));
        }

        return Ok(id, new JObject
        {
            ["type"] = type.Name,
            ["fullName"] = type.FullName,
            ["fieldCount"] = fields.Count,
            ["fields"] = fields,
            ["isGeneric"] = type.IsGenericType,
            ["baseType"] = type.BaseType?.Name
        });
    }

    /// <summary>Build a field descriptor JObject from a name and type.</summary>
    private static JObject BuildFieldDescriptor(string name, Type fieldType)
    {
        var fieldInfo = new JObject
        {
            ["name"] = name,
            ["syncType"] = fieldType.Name
        };

        // Extract the inner type for Sync<T>
        if (fieldType.IsGenericType)
        {
            var genericArgs = fieldType.GetGenericArguments();
            if (genericArgs.Length > 0)
            {
                fieldInfo["valueType"] = FormatTypeName(genericArgs[0]);

                if (genericArgs[0].IsEnum)
                    fieldInfo["enumValues"] = new JArray(Enum.GetNames(genericArgs[0]));
            }
        }

        // Check if it's a SyncRef (reference field)
        if (typeof(ISyncRef).IsAssignableFrom(fieldType))
        {
            fieldInfo["isReference"] = true;
            if (fieldType.IsGenericType)
            {
                var refArgs = fieldType.GetGenericArguments();
                if (refArgs.Length > 0)
                    fieldInfo["targetType"] = FormatTypeName(refArgs[0]);
            }
        }

        return fieldInfo;
    }

    /// <summary>
    /// Fuzzy search component type names. Searches both the registry and FrooxEngine assembly.
    /// </summary>
    public JObject HandleSearchComponents(string id, JObject p)
    {
        string query = p["query"]?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(query))
            return Error(id, "INVALID_PARAMS", "searchComponents requires 'query'");

        int maxResults = p["maxResults"]?.Value<int>() ?? 20;
        bool registeredOnly = p["registeredOnly"]?.Value<bool>() ?? false;

        var results = new JArray();

        // Search registered component shortcuts first
        foreach (var kvp in ComponentRegistry.ComponentTypes)
        {
            if (kvp.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new JObject
                {
                    ["shortName"] = kvp.Key,
                    ["fullName"] = kvp.Value.FullName,
                    ["registered"] = true
                });
            }
        }

        // Optionally search the full FrooxEngine assembly
        if (!registeredOnly)
        {
            var asm = typeof(FrooxEngine.Slot).Assembly;
            var componentBaseType = typeof(Component);

            foreach (var type in asm.GetTypes())
            {
                if (!componentBaseType.IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                if (!type.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !(type.FullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    continue;

                // Skip if already in registry results
                if (ComponentRegistry.ComponentTypes.ContainsKey(type.Name))
                    continue;

                results.Add(new JObject
                {
                    ["shortName"] = type.Name,
                    ["fullName"] = type.FullName,
                    ["registered"] = false
                });

                if (results.Count >= maxResults)
                    break;
            }
        }

        // Sort: registered first, then alphabetical
        var sorted = results.OrderBy(r => !(r["registered"]?.Value<bool>() ?? false))
                            .ThenBy(r => r["shortName"]?.ToString())
                            .Take(maxResults)
                            .ToList();

        return Ok(id, new JObject
        {
            ["query"] = query,
            ["count"] = sorted.Count,
            ["results"] = new JArray(sorted)
        });
    }

    /// <summary>
    /// Get the exact type of a specific field on an existing component instance.
    /// Returns type info, valid values (for enums), and current value.
    /// </summary>
    public JObject HandleGetFieldType(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;

        if (string.IsNullOrEmpty(slotName) || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(fieldName))
            return Error(id, "INVALID_PARAMS", "getFieldType requires 'slot', 'component', and 'field'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var (component, error) = ResolveComponent(slot, componentName, componentIndex, id);
        if (error != null)
            return error;

        var member = component.GetSyncMember(fieldName);
        if (member == null)
        {
            // List available fields to help the user
            var available = new JArray();
            foreach (var m in component.SyncMembers)
                available.Add(m.Name);

            return Error(id, "FIELD_NOT_FOUND", $"Field '{fieldName}' not found on {component.GetType().Name}. Available fields: {string.Join(", ", available.Select(a => a.ToString()))}");
        }

        var result = new JObject
        {
            ["field"] = fieldName,
            ["syncType"] = member.GetType().Name,
            ["component"] = component.GetType().Name,
            ["slot"] = slotName
        };

        // Extract inner type
        var memberType = member.GetType();
        if (memberType.IsGenericType)
        {
            var innerType = memberType.GetGenericArguments()[0];
            result["valueType"] = FormatTypeName(innerType);

            if (innerType.IsEnum)
                result["enumValues"] = new JArray(Enum.GetNames(innerType));
        }

        // Include current value
        result["currentValue"] = FieldParser.ReadFieldValue(member);

        // Check if it's a reference field
        if (member is ISyncRef)
            result["isReference"] = true;

        return Ok(id, result);
    }

    /// <summary>Format a type name for human readability.</summary>
    private static string FormatTypeName(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(long)) return "long";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(short)) return "short";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(ushort)) return "ushort";
        if (type.IsGenericType)
        {
            var args = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
            var baseName = type.Name.Split('`')[0];
            return $"{baseName}<{args}>";
        }
        return type.Name;
    }
}
