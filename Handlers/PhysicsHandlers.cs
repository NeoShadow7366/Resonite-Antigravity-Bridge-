using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handles physics-related commands: making physics objects and creating particle systems.
/// </summary>
internal class PhysicsHandlers : HandlerBase
{
    public PhysicsHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleMakePhysicsObject(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string colliderType = p["collider"]?.ToString() ?? "box";
        bool grabbable = p["grabbable"]?.Value<bool>() ?? true;
        float mass = p["mass"]?.Value<float>() ?? 1.0f;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        // Add collider
        Component collider = colliderType.ToLowerInvariant() switch
        {
            "box" => slot.AttachComponent<BoxCollider>(),
            "sphere" => slot.AttachComponent<SphereCollider>(),
            "capsule" => slot.AttachComponent<CapsuleCollider>(),
            "mesh" => slot.AttachComponent<MeshCollider>(),
            _ => null
        };

        if (collider == null)
            return Error(id, "INVALID_PARAMS", $"Unknown collider type: '{colliderType}'. Use: box, sphere, capsule, mesh");

        // Add CharacterController for physics mass
        var charCtrl = slot.AttachComponent<CharacterController>();

        // Add Grabbable if requested
        if (grabbable)
            slot.AttachComponent<Grabbable>();

        var result = new JObject
        {
            ["slot"] = slotName,
            ["collider"] = collider.GetType().Name,
            ["colliderRefId"] = collider.ReferenceID.ToString(),
            ["characterController"] = true,
            ["characterControllerRefId"] = charCtrl.ReferenceID.ToString(),
            ["grabbable"] = grabbable
        };

        return Ok(id, result);
    }

    public JObject HandleCreateParticleSystem(string id, JObject p)
    {
        string parentName = p["parent"]?.ToString();
        string trackAs = p["trackAs"]?.ToString() ?? "ParticleSystem";
        string emitterType = p["emitter"]?.ToString() ?? "point";
        float rate = p["rate"]?.Value<float>() ?? 50f;
        float lifetime = p["lifetime"]?.Value<float>() ?? 2f;
        float size = p["size"]?.Value<float>() ?? 0.05f;

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        Slot parent = null;
        if (!string.IsNullOrEmpty(parentName))
            parent = _tracker.Get(parentName);
        parent ??= world.RootSlot;

        var psSlot = parent.AddSlot(trackAs);
        _tracker.Register(trackAs, psSlot);

        // Create ParticleSystem
        var particleSystem = psSlot.AttachComponent<FrooxEngine.PhotonDust.ParticleSystem>();

        // Create ParticleStyle
        var style = psSlot.AttachComponent<FrooxEngine.PhotonDust.ParticleStyle>();

        // Create emitter
        Component emitter = emitterType.ToLowerInvariant() switch
        {
            "point" => psSlot.AttachComponent<FrooxEngine.PhotonDust.PointEmitter>(),
            "cone" => psSlot.AttachComponent<FrooxEngine.PhotonDust.ConeEmitter>(),
            "box" => psSlot.AttachComponent<FrooxEngine.PhotonDust.BoxEmitter>(),
            "sphere" => psSlot.AttachComponent<FrooxEngine.PhotonDust.SphereEmitter>(),
            _ => null
        };

        if (emitter == null)
            return Error(id, "INVALID_PARAMS", $"Unknown emitter type '{emitterType}'. Use: point, cone, box, sphere");

        // Create billboard renderer
        var renderer = psSlot.AttachComponent<FrooxEngine.PhotonDust.BillboardParticleRenderer>();

        // Create a material for the particles
        var material = psSlot.AttachComponent<UnlitMaterial>();

        // Set particle color if provided
        var colorArr = p["color"] as JArray;
        if (colorArr != null && colorArr.Count >= 3)
        {
            float r = colorArr[0].Value<float>();
            float g = colorArr[1].Value<float>();
            float b = colorArr[2].Value<float>();
            float a = colorArr.Count >= 4 ? colorArr[3].Value<float>() : 1f;
            material.TintColor.Value = new colorX(r, g, b, a);
        }

        var result = new JObject
        {
            ["slot"] = trackAs,
            ["refId"] = psSlot.ReferenceID.ToString(),
            ["particleSystemRefId"] = particleSystem.ReferenceID.ToString(),
            ["styleRefId"] = style.ReferenceID.ToString(),
            ["emitterType"] = emitterType,
            ["emitterRefId"] = emitter.ReferenceID.ToString(),
            ["rendererRefId"] = renderer.ReferenceID.ToString(),
            ["materialRefId"] = material.ReferenceID.ToString(),
            ["trackedAs"] = trackAs
        };

        return Ok(id, result);
    }
}
