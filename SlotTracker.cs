using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FrooxEngine;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Maps user-assigned string names to live Slot references.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
internal class SlotTracker
{
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a slot with a given name. Overwrites if name exists.</summary>
    public void Register(string name, Slot slot)
    {
        _slots[name] = slot;
        if (AntigravityBridge.IsVerbose)
            ResoniteMod.Msg($"[Tracker] Registered: {name} → {slot.ReferenceID}");
    }

    /// <summary>Get a slot by name. Returns null if not found.</summary>
    public Slot Get(string name)
    {
        if (name == "__root__" || name == "__worldroot__")
            return GetWorldRoot();

        if (name == "__localuser__")
            return GetLocalUserRoot();

        if (_slots.TryGetValue(name, out var slot))
        {
            // Verify slot is still alive
            if (slot != null && !slot.IsDestroyed)
                return slot;

            // Dead slot — remove from tracker
            _slots.TryRemove(name, out _);
            return null;
        }

        return null;
    }

    /// <summary>Remove a slot from tracking.</summary>
    public bool Unregister(string name)
    {
        return _slots.TryRemove(name, out _);
    }

    /// <summary>Number of tracked slots.</summary>
    public int Count => _slots.Count;

    /// <summary>Get all tracked slot names and their reference IDs.</summary>
    public JObject GetAllAsJson()
    {
        var result = new JObject();
        foreach (var kvp in _slots)
        {
            if (kvp.Value != null && !kvp.Value.IsDestroyed)
            {
                result[kvp.Key] = new JObject
                {
                    ["refId"] = kvp.Value.ReferenceID.ToString(),
                    ["name"] = kvp.Value.Name,
                    ["childCount"] = kvp.Value.ChildrenCount,
                    ["active"] = kvp.Value.ActiveSelf
                };
            }
        }
        return result;
    }

    /// <summary>Remove all tracked slots that have been destroyed. Returns count removed.</summary>
    public int PurgeDestroyed()
    {
        int removed = 0;
        foreach (var kvp in _slots)
        {
            if (kvp.Value == null || kvp.Value.IsDestroyed)
            {
                if (_slots.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }
        return removed;
    }

    /// <summary>Clear all tracked slots.</summary>
    public void Clear()
    {
        _slots.Clear();
    }

    private Slot GetWorldRoot()
    {
        var world = Engine.Current?.WorldManager?.FocusedWorld;
        return world?.RootSlot;
    }

    private Slot GetLocalUserRoot()
    {
        var world = Engine.Current?.WorldManager?.FocusedWorld;
        return world?.LocalUser?.Root?.Slot;
    }
}
