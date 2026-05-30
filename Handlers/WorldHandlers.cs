using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handles world and user awareness commands: world info, user info, user list, and user movement.
/// </summary>
internal class WorldHandlers : HandlerBase
{
    public WorldHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleGetWorldInfo(string id)
    {
        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        return Ok(id, new JObject
        {
            ["worldName"] = world.Name,
            ["sessionId"] = world.SessionId,
            ["userCount"] = world.UserCount,
            ["hostUser"] = world.HostUser?.UserName,
            ["isPrivate"] = world.AccessLevel.ToString() == "Private",
            ["uptime"] = world.Time.WorldTime.ToString("F1")
        });
    }

    public JObject HandleGetUserInfo(string id)
    {
        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        var localUser = world.LocalUser;
        if (localUser == null)
            return Error(id, "REF_NOT_FOUND", "No local user");

        var userRoot = localUser.Root?.Slot;
        var headSlot = userRoot?.FindChild("Head", matchSubstring: false, ignoreCase: true, maxDepth: 3);

        var result = new JObject
        {
            ["userName"] = localUser.UserName,
            ["userId"] = localUser.UserID,
            ["machineId"] = localUser.MachineID,
        };

        if (userRoot != null)
        {
            result["rootSlotRefId"] = userRoot.ReferenceID.ToString();
            result["position"] = new JArray(userRoot.GlobalPosition.x, userRoot.GlobalPosition.y, userRoot.GlobalPosition.z);
            result["rotation"] = new JArray(userRoot.GlobalRotation.x, userRoot.GlobalRotation.y, userRoot.GlobalRotation.z, userRoot.GlobalRotation.w);

            // Track user root for convenience
            _tracker.Register("__localuser__", userRoot);
        }

        if (headSlot != null)
        {
            result["headPosition"] = new JArray(headSlot.GlobalPosition.x, headSlot.GlobalPosition.y, headSlot.GlobalPosition.z);
            result["headRotation"] = new JArray(headSlot.GlobalRotation.x, headSlot.GlobalRotation.y, headSlot.GlobalRotation.z, headSlot.GlobalRotation.w);
        }

        return Ok(id, result);
    }

    public JObject HandleGetUsers(string id)
    {
        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        var users = new JArray();
        foreach (var user in world.AllUsers)
        {
            var userObj = new JObject
            {
                ["userName"] = user.UserName,
                ["userId"] = user.UserID,
                ["isHost"] = user == world.HostUser,
                ["isLocal"] = user == world.LocalUser,
            };

            var root = user.Root?.Slot;
            if (root != null)
            {
                userObj["position"] = new JArray(root.GlobalPosition.x, root.GlobalPosition.y, root.GlobalPosition.z);
                userObj["rootRefId"] = root.ReferenceID.ToString();
            }

            users.Add(userObj);
        }

        return Ok(id, new JObject
        {
            ["count"] = users.Count,
            ["users"] = users
        });
    }

    public JObject HandleMoveUser(string id, JObject p)
    {
        var posArr = p["position"] as JArray;
        var rotArr = p["rotation"] as JArray;

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        var localUser = world.LocalUser;
        if (localUser == null)
            return Error(id, "REF_NOT_FOUND", "No local user");

        var userRoot = localUser.Root?.Slot;
        if (userRoot == null)
            return Error(id, "REF_NOT_FOUND", "Local user has no root slot");

        if (posArr != null && posArr.Count >= 3)
        {
            var newPos = new float3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());
            userRoot.GlobalPosition = newPos;
        }

        if (rotArr != null && rotArr.Count >= 4)
        {
            var newRot = new floatQ(rotArr[0].Value<float>(), rotArr[1].Value<float>(), rotArr[2].Value<float>(), rotArr[3].Value<float>());
            userRoot.GlobalRotation = newRot;
        }

        // Also support moving to a slot's position
        string targetSlot = p["targetSlot"]?.ToString();
        if (!string.IsNullOrEmpty(targetSlot))
        {
            var target = _tracker.Get(targetSlot);
            if (target != null)
            {
                userRoot.GlobalPosition = target.GlobalPosition;
            }
            else
            {
                return Error(id, "SLOT_NOT_FOUND", $"Target slot '{targetSlot}' not found");
            }
        }

        return Ok(id, new JObject
        {
            ["position"] = new JArray(userRoot.GlobalPosition.x, userRoot.GlobalPosition.y, userRoot.GlobalPosition.z),
            ["rotation"] = new JArray(userRoot.GlobalRotation.x, userRoot.GlobalRotation.y, userRoot.GlobalRotation.z, userRoot.GlobalRotation.w)
        });
    }
}
