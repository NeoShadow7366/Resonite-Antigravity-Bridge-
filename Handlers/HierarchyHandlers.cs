using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handlers for slot finding, navigation, hierarchy inspection, and tracking.
/// </summary>
internal class HierarchyHandlers : HandlerBase
{
    public HierarchyHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleGetSlotInfo(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var children = new JArray();
        foreach (var child in slot.Children)
        {
            children.Add(new JObject
            {
                ["name"] = child.Name,
                ["refId"] = child.ReferenceID.ToString(),
                ["active"] = child.ActiveSelf,
                ["childCount"] = child.ChildrenCount,
                ["componentCount"] = child.ComponentCount
            });
        }

        var components = new JArray();
        foreach (var comp in slot.Components)
        {
            components.Add(new JObject
            {
                ["type"] = comp.GetType().Name,
                ["refId"] = comp.ReferenceID.ToString()
            });
        }

        return Ok(id, new JObject
        {
            ["name"] = slot.Name,
            ["refId"] = slot.ReferenceID.ToString(),
            ["active"] = slot.ActiveSelf,
            ["tag"] = slot.Tag,
            ["parent"] = slot.Parent != null ? new JObject
            {
                ["name"] = slot.Parent.Name,
                ["refId"] = slot.Parent.ReferenceID.ToString()
            } : null,
            ["childCount"] = slot.ChildrenCount,
            ["children"] = children,
            ["components"] = components
        });
    }

    public JObject HandleGetSlotTransform(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["local"] = new JObject
            {
                ["position"] = new JArray(slot.LocalPosition.x, slot.LocalPosition.y, slot.LocalPosition.z),
                ["rotation"] = new JArray(slot.LocalRotation.x, slot.LocalRotation.y, slot.LocalRotation.z, slot.LocalRotation.w),
                ["scale"] = new JArray(slot.LocalScale.x, slot.LocalScale.y, slot.LocalScale.z)
            },
            ["global"] = new JObject
            {
                ["position"] = new JArray(slot.GlobalPosition.x, slot.GlobalPosition.y, slot.GlobalPosition.z),
                ["rotation"] = new JArray(slot.GlobalRotation.x, slot.GlobalRotation.y, slot.GlobalRotation.z, slot.GlobalRotation.w),
                ["scale"] = new JArray(slot.GlobalScale.x, slot.GlobalScale.y, slot.GlobalScale.z)
            }
        });
    }

    public JObject HandleListChildren(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int maxDepth = p["depth"]?.Value<int>() ?? 1;
        bool trackAll = p["trackAll"]?.Value<bool>() ?? false;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var children = new JArray();
        CollectChildren(slot, children, 0, maxDepth, trackAll);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["depth"] = maxDepth,
            ["totalFound"] = children.Count,
            ["children"] = children
        });
    }

    private void CollectChildren(Slot parent, JArray results, int currentDepth, int maxDepth, bool trackAll)
    {
        foreach (var child in parent.Children)
        {
            var components = new JArray();
            foreach (var comp in child.Components)
                components.Add(comp.GetType().Name);

            var entry = new JObject
            {
                ["name"] = child.Name,
                ["refId"] = child.ReferenceID.ToString(),
                ["tag"] = child.Tag,
                ["active"] = child.ActiveSelf,
                ["depth"] = currentDepth + 1,
                ["childCount"] = child.ChildrenCount,
                ["components"] = components
            };

            if (trackAll)
                _tracker.Register(child.Name, child);

            results.Add(entry);

            if (maxDepth == -1 || currentDepth + 1 < maxDepth)
                CollectChildren(child, results, currentDepth + 1, maxDepth, trackAll);
        }
    }

    public JObject HandleGetSlotsByTag(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString() ?? "__root__";
        string tag = p["tag"]?.ToString();
        bool trackAll = p["trackAll"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(tag))
            return Error(id, "INVALID_PARAMS", "getSlotsByTag requires 'tag'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var matches = slot.GetChildrenWithTag(tag);
        var results = new JArray();

        foreach (var match in matches)
        {
            if (trackAll)
                _tracker.Register(match.Name, match);

            results.Add(new JObject
            {
                ["name"] = match.Name,
                ["refId"] = match.ReferenceID.ToString(),
                ["active"] = match.ActiveSelf,
                ["childCount"] = match.ChildrenCount,
                ["componentCount"] = match.ComponentCount
            });
        }

        return Ok(id, new JObject
        {
            ["tag"] = tag,
            ["count"] = results.Count,
            ["slots"] = results
        });
    }

    public JObject HandleFindSlot(string id, JObject p)
    {
        string searchRoot = p["searchRoot"]?.ToString() ?? "__root__";
        string name = p["name"]?.ToString();
        string tag = p["tag"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();
        bool matchSubstring = p["matchSubstring"]?.Value<bool>() ?? false;
        bool ignoreCase = p["ignoreCase"]?.Value<bool>() ?? true;
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(tag))
            return Error(id, "INVALID_PARAMS", "findSlot requires 'name' or 'tag' (or both)");

        var root = _tracker.Get(searchRoot);
        if (root == null)
            return Error(id, "SLOT_NOT_FOUND", $"Search root '{searchRoot}' not found");

        Slot found = null;

        if (!string.IsNullOrEmpty(name))
        {
            found = root.FindChild(name, matchSubstring, ignoreCase, maxDepth);
            if (found != null && !string.IsNullOrEmpty(tag) && found.Tag != tag)
                found = null;
        }
        else if (!string.IsNullOrEmpty(tag))
        {
            var tagged = root.GetChildrenWithTag(tag);
            found = tagged.Count > 0 ? tagged[0] : null;
        }

        if (found == null)
        {
            string criteria = !string.IsNullOrEmpty(name) ? $"name='{name}'" : $"tag='{tag}'";
            return Error(id, "SLOT_NOT_FOUND", $"No slot matching {criteria} found under '{searchRoot}'");
        }

        string trackName = trackAs ?? found.Name;
        _tracker.Register(trackName, found);

        return Ok(id, new JObject
        {
            ["name"] = found.Name,
            ["refId"] = found.ReferenceID.ToString(),
            ["tag"] = found.Tag,
            ["trackedAs"] = trackName,
            ["active"] = found.ActiveSelf,
            ["childCount"] = found.ChildrenCount,
            ["componentCount"] = found.ComponentCount
        });
    }

    public JObject HandleTrackExistingSlot(string id, JObject p)
    {
        string path = p["path"]?.ToString();
        string fromSlot = p["from"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();

        if (string.IsNullOrEmpty(path))
            return Error(id, "INVALID_PARAMS", "trackExistingSlot requires 'path'");

        var start = _tracker.Get(fromSlot);
        if (start == null)
            return Error(id, "SLOT_NOT_FOUND", $"Starting slot '{fromSlot}' not found");

        var segments = path.Split('/');
        Slot current = start;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var next = current.FindChild(seg);
            if (next == null)
            {
                string traversed = string.Join("/", segments.Take(i));
                return Error(id, "SLOT_NOT_FOUND", $"Child '{seg}' not found under '{(traversed.Length > 0 ? traversed : fromSlot)}'");
            }
            current = next;
        }

        string name = trackAs ?? current.Name;
        _tracker.Register(name, current);

        return Ok(id, new JObject
        {
            ["trackedAs"] = name,
            ["name"] = current.Name,
            ["refId"] = current.ReferenceID.ToString(),
            ["path"] = path,
            ["active"] = current.ActiveSelf,
            ["childCount"] = current.ChildrenCount,
            ["componentCount"] = current.ComponentCount
        });
    }

    public JObject HandleFindSlotByPath(string id, JObject p)
    {
        string path = p["path"]?.ToString();
        string fromSlot = p["from"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();

        var current = _tracker.Get(fromSlot);
        if (current == null)
            return Error(id, "SLOT_NOT_FOUND", $"Starting slot '{fromSlot}' not found");

        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return Error(id, "INVALID_PARAMS", "Path cannot be empty");

        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i].Trim();

            if (segment == "..")
            {
                if (current.Parent == null)
                    return Error(id, "OPERATION_FAILED", $"Cannot go above root at path segment {i} '..'");
                current = current.Parent;
            }
            else if (segment == ".")
            {
                continue;
            }
            else
            {
                Slot found = null;
                foreach (var child in current.Children)
                {
                    if (child.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
                    {
                        found = child;
                        break;
                    }
                }

                if (found == null)
                {
                    foreach (var child in current.Children)
                    {
                        if (child.Name.Contains(segment, StringComparison.OrdinalIgnoreCase))
                        {
                            found = child;
                            break;
                        }
                    }
                }

                if (found == null)
                {
                    string childNames = string.Join(", ", current.Children.Select(c => c.Name).Take(10));
                    return Error(id, "SLOT_NOT_FOUND", $"Path segment '{segment}' not found under '{current.Name}'. Children: [{childNames}]");
                }
                current = found;
            }
        }

        string name = trackAs ?? current.Name;
        _tracker.Register(name, current);

        return Ok(id, new JObject
        {
            ["name"] = current.Name,
            ["refId"] = current.ReferenceID.ToString(),
            ["path"] = path,
            ["trackedAs"] = name,
            ["active"] = current.ActiveSelf,
            ["tag"] = current.Tag,
            ["childCount"] = current.ChildrenCount,
            ["componentCount"] = current.ComponentCount
        });
    }

    public JObject HandleFindSlots(string id, JObject p)
    {
        string searchRoot = p["searchRoot"]?.ToString() ?? "__root__";
        string name = p["name"]?.ToString();
        string tag = p["tag"]?.ToString();
        string regex = p["regex"]?.ToString();
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;
        int maxResults = p["maxResults"]?.Value<int>() ?? 50;
        bool trackAll = p["trackAll"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(tag) && string.IsNullOrEmpty(regex))
            return Error(id, "INVALID_PARAMS", "findSlots requires at least one of: 'name', 'tag', or 'regex'");

        var root = _tracker.Get(searchRoot);
        if (root == null)
            return Error(id, "SLOT_NOT_FOUND", $"Search root '{searchRoot}' not found");

        Regex rxPattern = null;
        if (!string.IsNullOrEmpty(regex))
        {
            try
            {
                rxPattern = new Regex(regex, RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                return Error(id, "INVALID_PARAMS", $"Invalid regex '{regex}': {ex.Message}");
            }
        }

        var results = new JArray();
        SearchSlotsRecursive(root, name, tag, rxPattern, 0, maxDepth, maxResults, trackAll, results);

        return Ok(id, new JObject
        {
            ["searchRoot"] = searchRoot,
            ["totalFound"] = results.Count,
            ["maxResults"] = maxResults,
            ["results"] = results
        });
    }

    private void SearchSlotsRecursive(Slot slot, string name, string tag,
        Regex regex, int depth, int maxDepth, int maxResults,
        bool trackAll, JArray results)
    {
        if (results.Count >= maxResults) return;

        bool matches = true;

        if (!string.IsNullOrEmpty(name))
            matches &= slot.Name.Contains(name, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(tag))
            matches &= slot.Tag == tag;

        if (regex != null)
            matches &= regex.IsMatch(slot.Name);

        if (matches && depth > 0) // Skip root itself
        {
            results.Add(new JObject
            {
                ["name"] = slot.Name,
                ["refId"] = slot.ReferenceID.ToString(),
                ["tag"] = slot.Tag,
                ["active"] = slot.ActiveSelf,
                ["depth"] = depth,
                ["childCount"] = slot.ChildrenCount,
                ["path"] = GetSlotPath(slot)
            });

            if (trackAll)
                _tracker.Register(slot.Name, slot);
        }

        if (maxDepth == -1 || depth < maxDepth)
        {
            foreach (var child in slot.Children)
            {
                if (results.Count >= maxResults) return;
                SearchSlotsRecursive(child, name, tag, regex, depth + 1, maxDepth, maxResults, trackAll, results);
            }
        }
    }

    public JObject HandleGetParent(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var parent = slot.Parent;
        if (parent == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' has no parent (is root)");

        string name = trackAs ?? parent.Name;
        _tracker.Register(name, parent);

        var components = new JArray();
        foreach (var comp in parent.Components)
            components.Add(comp.GetType().Name);

        return Ok(id, new JObject
        {
            ["name"] = parent.Name,
            ["refId"] = parent.ReferenceID.ToString(),
            ["trackedAs"] = name,
            ["active"] = parent.ActiveSelf,
            ["tag"] = parent.Tag,
            ["childCount"] = parent.ChildrenCount,
            ["components"] = components,
            ["path"] = GetSlotPath(parent)
        });
    }

    public JObject HandleGetSlotHierarchy(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int maxDepth = p["maxDepth"]?.Value<int>() ?? 3;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        var tree = BuildHierarchyNode(slot, 0, maxDepth);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["maxDepth"] = maxDepth,
            ["tree"] = tree
        });
    }

    private JObject BuildHierarchyNode(Slot slot, int depth, int maxDepth)
    {
        var node = new JObject
        {
            ["name"] = slot.Name,
            ["refId"] = slot.ReferenceID.ToString(),
            ["active"] = slot.ActiveSelf,
            ["componentCount"] = slot.ComponentCount,
            ["childCount"] = slot.ChildrenCount
        };

        if (!string.IsNullOrEmpty(slot.Tag))
            node["tag"] = slot.Tag;

        var compNames = new JArray();
        foreach (var comp in slot.Components)
            compNames.Add(comp.GetType().Name);
        if (compNames.Count > 0)
            node["components"] = compNames;

        if (depth < maxDepth && slot.ChildrenCount > 0)
        {
            var children = new JArray();
            foreach (var child in slot.Children)
                children.Add(BuildHierarchyNode(child, depth + 1, maxDepth));
            node["children"] = children;
        }
        else if (slot.ChildrenCount > 0)
        {
            node["truncated"] = true;
            node["hiddenChildren"] = slot.ChildrenCount;
        }

        return node;
    }

    // ─── Helpers ────────────────────────────────────────────────

    internal string GetSlotPath(Slot slot)
    {
        var parts = new List<string>();
        var current = slot;
        int maxParts = 20;
        while (current != null && parts.Count < maxParts)
        {
            parts.Add(current.Name);
            current = current.Parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
