using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Manages event subscriptions for WebSocket clients.
/// Hooks into FrooxEngine events and broadcasts changes to subscribers.
/// </summary>
internal class EventSystem
{
    private readonly SlotTracker _tracker;
    private readonly Func<JObject, Task> _broadcast;
    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

    // Polling interval for value-change subscriptions (ticks per check)
    private const int PollIntervalFrames = 5;
    private int _frameCounter;
    private bool _isActive;

    public int SubscriptionCount => _subscriptions.Count;

    public EventSystem(SlotTracker tracker, Func<JObject, Task> broadcast)
    {
        _tracker = tracker;
        _broadcast = broadcast;
    }

    /// <summary>Start the event polling system. Call from engine update loop.</summary>
    public void Tick()
    {
        if (!_isActive || _subscriptions.IsEmpty) return;

        _frameCounter++;
        if (_frameCounter < PollIntervalFrames) return;
        _frameCounter = 0;

        // Check value-change subscriptions
        foreach (var kvp in _subscriptions)
        {
            var sub = kvp.Value;
            if (sub.Type == SubscriptionType.FieldChanged)
            {
                CheckFieldChanged(kvp.Key, sub);
            }
            else if (sub.Type == SubscriptionType.SlotChildrenChanged)
            {
                CheckChildrenChanged(kvp.Key, sub);
            }
        }
    }

    public void Start() => _isActive = true;
    public void Stop()
    {
        _isActive = false;
        _subscriptions.Clear();
    }

    /// <summary>Subscribe to an event. Returns the subscription ID.</summary>
    public JObject Subscribe(string id, JObject p)
    {
        string eventType = p["eventType"]?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(eventType))
            return MakeError(id, "Missing 'eventType'");

        string subId = $"sub_{Guid.NewGuid().ToString("N")[..8]}";

        switch (eventType)
        {
            case "fieldchanged":
                return SubscribeFieldChanged(id, subId, p);

            case "slotchildrenchanged":
                return SubscribeSlotChildrenChanged(id, subId, p);

            case "slotdestroyed":
                return SubscribeSlotDestroyed(id, subId, p);

            case "userjoin":
            case "userleave":
                return SubscribeUserEvent(id, subId, p, eventType);

            default:
                return MakeError(id, $"Unknown eventType: '{eventType}'. Supported: fieldChanged, slotChildrenChanged, slotDestroyed, userJoin, userLeave");
        }
    }

    /// <summary>Unsubscribe by subscription ID, or all.</summary>
    public JObject Unsubscribe(string id, JObject p)
    {
        string subId = p["subscriptionId"]?.ToString();
        bool all = p["all"]?.Value<bool>() ?? false;

        if (all)
        {
            int count = _subscriptions.Count;
            _subscriptions.Clear();
            return MakeOk(id, new JObject
            {
                ["unsubscribed"] = count,
                ["message"] = "All subscriptions cleared"
            });
        }

        if (string.IsNullOrEmpty(subId))
            return MakeError(id, "Provide 'subscriptionId' or set 'all' to true");

        if (_subscriptions.TryRemove(subId, out _))
        {
            return MakeOk(id, new JObject
            {
                ["subscriptionId"] = subId,
                ["message"] = "Unsubscribed"
            });
        }

        return MakeError(id, $"Subscription '{subId}' not found");
    }

    /// <summary>List all active subscriptions.</summary>
    public JObject ListSubscriptions(string id)
    {
        var subs = new JArray();
        foreach (var kvp in _subscriptions)
        {
            subs.Add(new JObject
            {
                ["subscriptionId"] = kvp.Key,
                ["type"] = kvp.Value.Type.ToString(),
                ["target"] = kvp.Value.Description
            });
        }

        return MakeOk(id, new JObject
        {
            ["count"] = subs.Count,
            ["subscriptions"] = subs
        });
    }

    // ─── Subscription Handlers ──────────────────────────────────

    private JObject SubscribeFieldChanged(string id, string subId, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();

        if (string.IsNullOrEmpty(slotName) || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(fieldName))
            return MakeError(id, "fieldChanged requires 'slot', 'component', and 'field'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return MakeError(id, $"Slot '{slotName}' not found");

        // Find component
        Component component = null;
        foreach (var comp in slot.Components)
        {
            if (comp.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase) ||
                comp.GetType().FullName?.EndsWith(componentName, StringComparison.OrdinalIgnoreCase) == true)
            {
                component = comp;
                break;
            }
        }
        if (component == null)
            return MakeError(id, $"Component '{componentName}' not found on slot '{slotName}'");

        var member = component.GetSyncMember(fieldName);
        if (member == null)
            return MakeError(id, $"Field '{fieldName}' not found on component '{componentName}'");

        // Store current value for change detection
        string currentValue = ReadMemberValueSafe(member);

        _subscriptions[subId] = new Subscription
        {
            Type = SubscriptionType.FieldChanged,
            SlotName = slotName,
            ComponentName = componentName,
            FieldName = fieldName,
            Slot = slot,
            Component = component,
            Member = member,
            LastValue = currentValue,
            Description = $"{slotName}/{componentName}.{fieldName}"
        };

        return MakeOk(id, new JObject
        {
            ["subscriptionId"] = subId,
            ["eventType"] = "fieldChanged",
            ["target"] = $"{slotName}/{componentName}.{fieldName}",
            ["currentValue"] = currentValue
        });
    }

    private JObject SubscribeSlotChildrenChanged(string id, string subId, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        if (string.IsNullOrEmpty(slotName))
            return MakeError(id, "slotChildrenChanged requires 'slot'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return MakeError(id, $"Slot '{slotName}' not found");

        _subscriptions[subId] = new Subscription
        {
            Type = SubscriptionType.SlotChildrenChanged,
            SlotName = slotName,
            Slot = slot,
            LastChildCount = slot.ChildrenCount,
            Description = $"{slotName} children"
        };

        return MakeOk(id, new JObject
        {
            ["subscriptionId"] = subId,
            ["eventType"] = "slotChildrenChanged",
            ["target"] = slotName,
            ["currentChildCount"] = slot.ChildrenCount
        });
    }

    private JObject SubscribeSlotDestroyed(string id, string subId, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        if (string.IsNullOrEmpty(slotName))
            return MakeError(id, "slotDestroyed requires 'slot'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return MakeError(id, $"Slot '{slotName}' not found");

        _subscriptions[subId] = new Subscription
        {
            Type = SubscriptionType.SlotDestroyed,
            SlotName = slotName,
            Slot = slot,
            Description = $"{slotName} destroyed"
        };

        return MakeOk(id, new JObject
        {
            ["subscriptionId"] = subId,
            ["eventType"] = "slotDestroyed",
            ["target"] = slotName
        });
    }

    private JObject SubscribeUserEvent(string id, string subId, JObject p, string eventType)
    {
        var world = Engine.Current?.WorldManager?.FocusedWorld;
        if (world == null)
            return MakeError(id, "No focused world");

        _subscriptions[subId] = new Subscription
        {
            Type = eventType == "userjoin" ? SubscriptionType.UserJoin : SubscriptionType.UserLeave,
            LastUserCount = world.UserCount,
            LastUserNames = world.AllUsers.Select(u => u.UserName).ToHashSet(),
            Description = eventType
        };

        return MakeOk(id, new JObject
        {
            ["subscriptionId"] = subId,
            ["eventType"] = eventType,
            ["currentUserCount"] = world.UserCount
        });
    }

    // ─── Polling Checks ─────────────────────────────────────────

    private void CheckFieldChanged(string subId, Subscription sub)
    {
        if (sub.Slot == null || sub.Slot.IsDestroyed || sub.Component == null || sub.Component.IsDestroyed)
        {
            // Target destroyed — auto-unsubscribe and notify
            _subscriptions.TryRemove(subId, out _);
            FireEvent(subId, "fieldChanged", new JObject
            {
                ["target"] = sub.Description,
                ["destroyed"] = true,
                ["message"] = "Target slot/component was destroyed. Subscription removed."
            });
            return;
        }

        string currentValue = ReadMemberValueSafe(sub.Member);
        if (currentValue != sub.LastValue)
        {
            string oldValue = sub.LastValue;
            sub.LastValue = currentValue;

            FireEvent(subId, "fieldChanged", new JObject
            {
                ["target"] = sub.Description,
                ["oldValue"] = oldValue,
                ["newValue"] = currentValue,
                ["slotName"] = sub.SlotName,
                ["component"] = sub.ComponentName,
                ["field"] = sub.FieldName
            });
        }
    }

    private void CheckChildrenChanged(string subId, Subscription sub)
    {
        if (sub.Slot == null || sub.Slot.IsDestroyed)
        {
            _subscriptions.TryRemove(subId, out _);
            FireEvent(subId, "slotChildrenChanged", new JObject
            {
                ["target"] = sub.Description,
                ["destroyed"] = true,
                ["message"] = "Target slot was destroyed. Subscription removed."
            });
            return;
        }

        int currentCount = sub.Slot.ChildrenCount;
        if (currentCount != sub.LastChildCount)
        {
            int oldCount = sub.LastChildCount;
            sub.LastChildCount = currentCount;

            FireEvent(subId, "slotChildrenChanged", new JObject
            {
                ["target"] = sub.SlotName,
                ["oldCount"] = oldCount,
                ["newCount"] = currentCount,
                ["delta"] = currentCount - oldCount
            });
        }
    }

    /// <summary>Called from engine update to check slot destroyed and user events.</summary>
    public void CheckDestroyedAndUserEvents()
    {
        var toRemove = new List<string>();

        foreach (var kvp in _subscriptions)
        {
            var sub = kvp.Value;

            if (sub.Type == SubscriptionType.SlotDestroyed)
            {
                if (sub.Slot == null || sub.Slot.IsDestroyed)
                {
                    toRemove.Add(kvp.Key);
                    FireEvent(kvp.Key, "slotDestroyed", new JObject
                    {
                        ["slotName"] = sub.SlotName,
                        ["message"] = $"Slot '{sub.SlotName}' was destroyed"
                    });
                }
            }
            else if (sub.Type == SubscriptionType.UserJoin || sub.Type == SubscriptionType.UserLeave)
            {
                CheckUserEvent(kvp.Key, sub);
            }
        }

        foreach (var key in toRemove)
            _subscriptions.TryRemove(key, out _);
    }

    private void CheckUserEvent(string subId, Subscription sub)
    {
        var world = Engine.Current?.WorldManager?.FocusedWorld;
        if (world == null) return;

        var currentNames = world.AllUsers.Select(u => u.UserName).ToHashSet();

        if (sub.Type == SubscriptionType.UserJoin)
        {
            var newUsers = currentNames.Except(sub.LastUserNames).ToList();
            if (newUsers.Count > 0)
            {
                sub.LastUserNames = currentNames;
                sub.LastUserCount = world.UserCount;

                foreach (var userName in newUsers)
                {
                    FireEvent(subId, "userJoin", new JObject
                    {
                        ["userName"] = userName,
                        ["userCount"] = world.UserCount
                    });
                }
            }
        }
        else if (sub.Type == SubscriptionType.UserLeave)
        {
            var leftUsers = sub.LastUserNames.Except(currentNames).ToList();
            if (leftUsers.Count > 0)
            {
                sub.LastUserNames = currentNames;
                sub.LastUserCount = world.UserCount;

                foreach (var userName in leftUsers)
                {
                    FireEvent(subId, "userLeave", new JObject
                    {
                        ["userName"] = userName,
                        ["userCount"] = world.UserCount
                    });
                }
            }
        }
    }

    // ─── Helpers ────────────────────────────────────────────────

    private void FireEvent(string subId, string eventType, JObject data)
    {
        var message = new JObject
        {
            ["type"] = "event",
            ["eventType"] = eventType,
            ["subscriptionId"] = subId,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["data"] = data
        };

        // Fire-and-forget broadcast (async)
        Task.Run(async () =>
        {
            try { await _broadcast(message); }
            catch (Exception ex) { ResoniteMod.Error($"[Events] Broadcast failed: {ex.Message}"); }
        });
    }

    private string ReadMemberValueSafe(ISyncMember member)
    {
        try
        {
            if (member == null) return null;
            var type = member.GetType();

            // Check Sync<T> value types
            var valueProperty = type.GetProperty("Value");
            if (valueProperty != null)
            {
                var val = valueProperty.GetValue(member);
                return val?.ToString();
            }

            // Check ISyncRef
            if (member is ISyncRef syncRef)
                return syncRef.Target?.ToString() ?? "<null>";

            return $"<{type.Name}>";
        }
        catch
        {
            return "<error>";
        }
    }

    private JObject MakeOk(string id, JObject data)
    {
        data["status"] = "ok";
        data["id"] = id;
        return data;
    }

    private JObject MakeError(string id, string error)
    {
        return new JObject
        {
            ["status"] = "error",
            ["id"] = id,
            ["error"] = error
        };
    }

    // ─── Types ──────────────────────────────────────────────────

    private enum SubscriptionType
    {
        FieldChanged,
        SlotChildrenChanged,
        SlotDestroyed,
        UserJoin,
        UserLeave
    }

    private class Subscription
    {
        public SubscriptionType Type;
        public string Description;

        // Field tracking
        public string SlotName;
        public string ComponentName;
        public string FieldName;
        public Slot Slot;
        public Component Component;
        public ISyncMember Member;
        public string LastValue;

        // Children tracking
        public int LastChildCount;

        // User tracking
        public int LastUserCount;
        public HashSet<string> LastUserNames;
    }
}
