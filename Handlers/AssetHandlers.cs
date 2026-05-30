using System;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handles asset import commands: textures, meshes, audio, and video.
/// </summary>
internal class AssetHandlers : HandlerBase
{
    public AssetHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleImportTexture(string id, JObject p)
    {
        string url = p["url"]?.ToString();
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();
        bool createSprite = p["createSprite"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(url))
            return Error(id, "INVALID_PARAMS", "importTexture requires 'url'");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, "SLOT_NOT_FOUND", $"Parent slot '{parentName}' not found");

        // Create asset slot
        string slotName = trackAs ?? "ImportedTexture";
        var slot = parent.AddSlot(slotName);
        _tracker.Register(slotName, slot);

        // Attach StaticTexture2D and set URL
        var texture = slot.AttachComponent<StaticTexture2D>();
        var urlField = texture.GetSyncMember("URL") as Sync<Uri>;
        if (urlField != null)
            urlField.Value = new Uri(url);

        var result = new JObject
        {
            ["slot"] = slotName,
            ["refId"] = slot.ReferenceID.ToString(),
            ["textureRefId"] = texture.ReferenceID.ToString(),
            ["url"] = url,
            ["trackedAs"] = slotName
        };

        // Optionally create a SpriteProvider for UIX Image use
        if (createSprite)
        {
            var sprite = slot.AttachComponent<SpriteProvider>();
            // Wire the sprite's Texture to our StaticTexture2D
            var spriteTexRef = sprite.GetSyncMember("Texture") as ISyncRef;
            spriteTexRef?.TrySet(texture);

            result["spriteRefId"] = sprite.ReferenceID.ToString();
        }

        return Ok(id, result);
    }

    public JObject HandleImportMesh(string id, JObject p)
    {
        string url = p["url"]?.ToString();
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();

        if (string.IsNullOrEmpty(url))
            return Error(id, "INVALID_PARAMS", "importMesh requires 'url'");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, "SLOT_NOT_FOUND", $"Parent slot '{parentName}' not found");

        string slotName = trackAs ?? "ImportedMesh";
        var slot = parent.AddSlot(slotName);
        _tracker.Register(slotName, slot);

        var mesh = slot.AttachComponent<StaticMesh>();
        var urlField = mesh.GetSyncMember("URL") as Sync<Uri>;
        if (urlField != null)
            urlField.Value = new Uri(url);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["refId"] = slot.ReferenceID.ToString(),
            ["meshRefId"] = mesh.ReferenceID.ToString(),
            ["url"] = url,
            ["trackedAs"] = slotName
        });
    }

    public JObject HandleImportAudio(string id, JObject p)
    {
        string url = p["url"]?.ToString();
        string parentName = p["parent"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();
        bool spatial = p["spatial"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(url))
            return Error(id, "INVALID_PARAMS", "importAudio requires 'url'");

        // Find parent slot
        Slot parentSlot = null;
        if (!string.IsNullOrEmpty(parentName))
            parentSlot = _tracker.Get(parentName);

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        var root = parentSlot ?? world.RootSlot;
        var audioSlot = root.AddSlot("AudioSource");

        // Create audio clip
        var audioClip = audioSlot.AttachComponent<StaticAudioClip>();
        audioClip.URL.Value = new Uri(url);

        // Create player
        var player = audioSlot.AttachComponent<AudioClipPlayer>();
        player.Clip.Target = audioClip;

        // Create output
        var output = audioSlot.AttachComponent<AudioOutput>();
        output.Source.Target = player;
        output.SpatialBlend.Value = spatial ? 1.0f : 0.0f;

        // Track
        string name = trackAs ?? audioSlot.Name;
        audioSlot.Name = name;
        _tracker.Register(name, audioSlot);

        return Ok(id, new JObject
        {
            ["slotName"] = name,
            ["refId"] = audioSlot.ReferenceID.ToString(),
            ["audioClipRefId"] = audioClip.ReferenceID.ToString(),
            ["playerRefId"] = player.ReferenceID.ToString(),
            ["outputRefId"] = output.ReferenceID.ToString(),
            ["spatial"] = spatial,
            ["trackedAs"] = name,
            ["url"] = url
        });
    }

    public JObject HandleImportVideo(string id, JObject p)
    {
        string url = p["url"]?.ToString();
        string parentName = p["parent"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();
        bool autoPlay = p["autoPlay"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(url))
            return Error(id, "INVALID_PARAMS", "importVideo requires 'url'");

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        Slot parent = null;
        if (!string.IsNullOrEmpty(parentName))
            parent = _tracker.Get(parentName);
        parent ??= world.RootSlot;

        var videoSlot = parent.AddSlot("VideoPlayer");

        // Create video texture provider
        var videoProvider = videoSlot.AttachComponent<VideoTextureProvider>();
        videoProvider.URL.Value = new Uri(url);

        // Create a quad to display the video on
        var displaySlot = videoSlot.AddSlot("Display");
        var quad = displaySlot.AttachComponent<QuadMesh>();
        var renderer = displaySlot.AttachComponent<MeshRenderer>();
        var material = displaySlot.AttachComponent<UnlitMaterial>();

        // Wire mesh
        renderer.Mesh.Target = quad;

        // Wire material to renderer (add to materials list)
        renderer.Materials.Add().Target = material;

        // Wire video texture to material
        material.Texture.Target = videoProvider;

        string name = trackAs ?? "VideoPlayer";
        videoSlot.Name = name;
        _tracker.Register(name, videoSlot);
        _tracker.Register(name + "_Display", displaySlot);

        return Ok(id, new JObject
        {
            ["slotName"] = name,
            ["refId"] = videoSlot.ReferenceID.ToString(),
            ["videoProviderRefId"] = videoProvider.ReferenceID.ToString(),
            ["displaySlotRefId"] = displaySlot.ReferenceID.ToString(),
            ["materialRefId"] = material.ReferenceID.ToString(),
            ["trackedAs"] = name,
            ["url"] = url
        });
    }
}
