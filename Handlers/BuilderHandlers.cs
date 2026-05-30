using System;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using Newtonsoft.Json.Linq;

namespace AntigravityBridge;

/// <summary>
/// Handles builder commands: creating primitives, materials, 3D text, and lights.
/// </summary>
internal class BuilderHandlers : HandlerBase
{
    public BuilderHandlers(SlotTracker tracker) : base(tracker) { }

    public JObject HandleCreatePrimitive(string id, JObject p)
    {
        string name = p["name"]?.ToString() ?? "Primitive";
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string meshType = p["meshType"]?.ToString();
        string meshUrl = p["meshUrl"]?.ToString();
        string materialType = p["material"]?.ToString() ?? "PBS_Metallic";

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, "SLOT_NOT_FOUND", $"Parent slot '{parentName}' not found");

        // Create main slot
        var slot = parent.AddSlot(name);
        _tracker.Register(name, slot);

        // Optional transform
        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        var result = new JObject
        {
            ["slot"] = name,
            ["refId"] = slot.ReferenceID.ToString()
        };

        // Attach mesh
        Component meshComponent = null;
        if (!string.IsNullOrEmpty(meshUrl))
        {
            // Static mesh from URL
            var staticMesh = slot.AttachComponent<StaticMesh>();
            var urlField = staticMesh.GetSyncMember("URL") as Sync<Uri>;
            if (urlField != null)
                urlField.Value = new Uri(meshUrl);
            meshComponent = staticMesh;
            result["meshType"] = "StaticMesh";
            result["meshUrl"] = meshUrl;
        }
        else
        {
            // Procedural mesh
            Type procMeshType = ComponentRegistry.Resolve(meshType ?? "BoxMesh");
            if (procMeshType == null)
                procMeshType = typeof(BoxMesh);
            meshComponent = slot.AttachComponent(procMeshType);
            result["meshType"] = procMeshType.Name;
        }
        result["meshRefId"] = meshComponent.ReferenceID.ToString();

        // Attach MeshRenderer
        var renderer = slot.AttachComponent<MeshRenderer>();
        result["rendererRefId"] = renderer.ReferenceID.ToString();

        // Wire mesh to renderer
        var meshRef = renderer.GetSyncMember("Mesh") as ISyncRef;
        meshRef?.TrySet(meshComponent);

        // Attach material
        Type matType = ComponentRegistry.Resolve(materialType);
        if (matType == null) matType = typeof(PBS_Metallic);
        var material = slot.AttachComponent(matType);
        result["materialType"] = matType.Name;
        result["materialRefId"] = material.ReferenceID.ToString();

        // Wire material to renderer's Materials list
        var materialsField = renderer.GetSyncMember("Materials");
        if (materialsField != null)
        {
            // SyncAssetList<Material> — use reflection to call Add(IAssetProvider<Material>)
            var addMethod = materialsField.GetType().GetMethod("Add",
                new[] { typeof(IAssetProvider<Material>) });
            if (addMethod != null)
                addMethod.Invoke(materialsField, new object[] { material });
        }

        // Set color if provided
        var color = p["color"] as JArray;
        if (color != null && color.Count >= 3)
        {
            string colorFieldName = matType.Name.Contains("PBS") ? "AlbedoColor" : "TintColor";
            var colorField = material.GetSyncMember(colorFieldName) as Sync<colorX>;
            if (colorField != null)
            {
                float a = color.Count >= 4 ? color[3].Value<float>() : 1.0f;
                colorField.Value = new colorX(color[0].Value<float>(), color[1].Value<float>(), color[2].Value<float>(), a);
            }
        }

        return Ok(id, result);
    }

    public JObject HandleCreateMaterial(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string materialType = p["materialType"]?.ToString() ?? "PBS_Metallic";
        string trackAs = p["trackAs"]?.ToString();
        string rendererSlot = p["rendererSlot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, "SLOT_NOT_FOUND", $"Slot '{slotName}' not found");

        Type matType = ComponentRegistry.Resolve(materialType);
        if (matType == null)
            return Error(id, "COMPONENT_NOT_FOUND", $"Material type '{materialType}' not found");

        var material = slot.AttachComponent(matType);

        // Set color if provided
        var colorArr = p["color"] as JArray;
        if (colorArr != null && colorArr.Count >= 3)
        {
            try
            {
                var colorField = material.GetSyncMember("TintColor")
                              ?? material.GetSyncMember("Color")
                              ?? material.GetSyncMember("AlbedoColor");
                if (colorField != null)
                {
                    var valProp = colorField.GetType().GetProperty("Value");
                    if (valProp != null)
                    {
                        float r = colorArr[0].Value<float>();
                        float g = colorArr[1].Value<float>();
                        float b = colorArr[2].Value<float>();
                        float a = colorArr.Count >= 4 ? colorArr[3].Value<float>() : 1f;
                        valProp.SetValue(colorField, new colorX(r, g, b, a));
                    }
                }
            }
            catch { }
        }

        // Set metallic/smoothness if provided (for PBS materials)
        if (p["metallic"] != null)
        {
            try
            {
                var metalField = material.GetSyncMember("Metallic");
                if (metalField != null)
                {
                    var valProp = metalField.GetType().GetProperty("Value");
                    valProp?.SetValue(metalField, p["metallic"].Value<float>());
                }
            }
            catch { }
        }

        if (p["smoothness"] != null)
        {
            try
            {
                var smoothField = material.GetSyncMember("Smoothness");
                if (smoothField != null)
                {
                    var valProp = smoothField.GetType().GetProperty("Value");
                    valProp?.SetValue(smoothField, p["smoothness"].Value<float>());
                }
            }
            catch { }
        }

        // Auto-wire to renderer if specified
        string wireResult = null;
        if (!string.IsNullOrEmpty(rendererSlot))
        {
            var rSlot = _tracker.Get(rendererSlot);
            if (rSlot != null)
            {
                foreach (var comp in rSlot.Components)
                {
                    if (comp is MeshRenderer mr)
                    {
                        if (material is IAssetProvider<Material> matProvider)
                            mr.Materials.Add().Target = matProvider;
                        wireResult = $"Wired to MeshRenderer on '{rendererSlot}'";
                        break;
                    }
                }
            }
        }

        string name = trackAs ?? $"Material_{material.GetType().Name}";
        if (!string.IsNullOrEmpty(trackAs))
            _tracker.Register(trackAs, slot);

        var result = new JObject
        {
            ["slot"] = slotName,
            ["materialType"] = material.GetType().Name,
            ["materialRefId"] = material.ReferenceID.ToString(),
        };
        if (wireResult != null) result["wired"] = wireResult;
        if (trackAs != null) result["trackedAs"] = trackAs;

        return Ok(id, result);
    }

    public JObject HandleCreate3DText(string id, JObject p)
    {
        string parentName = p["parent"]?.ToString();
        string text = p["text"]?.ToString() ?? "Hello";
        string trackAs = p["trackAs"]?.ToString() ?? "3DText";
        float fontSize = p["fontSize"]?.Value<float>() ?? 0.1f;

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        Slot parent = null;
        if (!string.IsNullOrEmpty(parentName))
            parent = _tracker.Get(parentName);
        parent ??= world.RootSlot;

        var textSlot = parent.AddSlot(trackAs);

        // Set position if provided
        var posArr = p["position"] as JArray;
        if (posArr != null && posArr.Count >= 3)
            textSlot.LocalPosition = new float3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());

        // Create TextRenderer
        var textRenderer = textSlot.AttachComponent<TextRenderer>();
        textRenderer.Text.Value = text;
        textRenderer.Size.Value = fontSize;

        // Set horizontal alignment
        var halign = p["horizontalAlign"]?.ToString();
        if (!string.IsNullOrEmpty(halign))
        {
            try
            {
                var alignField = textRenderer.GetSyncMember("HorizontalAlign");
                if (alignField != null)
                {
                    var valProp = alignField.GetType().GetProperty("Value");
                    if (valProp != null)
                        valProp.SetValue(alignField, Enum.Parse(valProp.PropertyType, halign, true));
                }
            }
            catch { }
        }

        // Create material
        var material = textSlot.AttachComponent<UnlitMaterial>();

        // Set color
        var colorArr = p["color"] as JArray;
        if (colorArr != null && colorArr.Count >= 3)
        {
            float r = colorArr[0].Value<float>();
            float g = colorArr[1].Value<float>();
            float b = colorArr[2].Value<float>();
            float a = colorArr.Count >= 4 ? colorArr[3].Value<float>() : 1f;
            material.TintColor.Value = new colorX(r, g, b, a);
        }
        else
        {
            material.TintColor.Value = new colorX(1f, 1f, 1f, 1f);
        }

        // Wire material
        textRenderer.Material.Target = material;

        _tracker.Register(trackAs, textSlot);

        return Ok(id, new JObject
        {
            ["slotName"] = trackAs,
            ["refId"] = textSlot.ReferenceID.ToString(),
            ["textRendererRefId"] = textRenderer.ReferenceID.ToString(),
            ["materialRefId"] = material.ReferenceID.ToString(),
            ["text"] = text,
            ["fontSize"] = fontSize,
            ["trackedAs"] = trackAs
        });
    }

    public JObject HandleCreateLight(string id, JObject p)
    {
        string parentName = p["parent"]?.ToString();
        string trackAs = p["trackAs"]?.ToString() ?? "Light";
        string lightType = p["lightType"]?.ToString() ?? "point";
        float intensity = p["intensity"]?.Value<float>() ?? 1.0f;
        float range = p["range"]?.Value<float>() ?? 10.0f;
        bool shadows = p["shadows"]?.Value<bool>() ?? true;

        var world = GetFocusedWorld();
        if (world == null)
            return Error(id, "WORLD_NOT_FOUND", "No focused world");

        Slot parent = null;
        if (!string.IsNullOrEmpty(parentName))
            parent = _tracker.Get(parentName);
        parent ??= world.RootSlot;

        var lightSlot = parent.AddSlot(trackAs);

        // Set position if provided
        var posArr = p["position"] as JArray;
        if (posArr != null && posArr.Count >= 3)
            lightSlot.LocalPosition = new float3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());

        var light = lightSlot.AttachComponent<Light>();
        light.Intensity.Value = intensity;
        light.Range.Value = range;

        // Set shadows via reflection (field name may vary)
        try
        {
            var shadowMember = light.GetSyncMember("ShadowsEnabled")
                            ?? light.GetSyncMember("Shadows")
                            ?? light.GetSyncMember("ShadowType");
            if (shadowMember != null)
            {
                var valProp = shadowMember.GetType().GetProperty("Value");
                if (valProp != null)
                {
                    if (valProp.PropertyType == typeof(bool))
                        valProp.SetValue(shadowMember, shadows);
                    else if (valProp.PropertyType.IsEnum)
                    {
                        // For enum shadow types, use the first non-None value for enabled
                        var names = Enum.GetNames(valProp.PropertyType);
                        if (shadows && names.Length > 1)
                            valProp.SetValue(shadowMember, Enum.Parse(valProp.PropertyType, names[1]));
                    }
                }
            }
        }
        catch { /* Shadows not critical */ }

        // Set light type
        try
        {
            var lightTypeField = light.GetSyncMember("LightType");
            if (lightTypeField != null)
            {
                var valueProp = lightTypeField.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    var enumType = valueProp.PropertyType;
                    var parsed = Enum.Parse(enumType, lightType, true);
                    valueProp.SetValue(lightTypeField, parsed);
                }
            }
        }
        catch { /* Default light type is fine */ }

        // Set color if provided
        var colorArr = p["color"] as JArray;
        if (colorArr != null && colorArr.Count >= 3)
        {
            float r = colorArr[0].Value<float>();
            float g = colorArr[1].Value<float>();
            float b = colorArr[2].Value<float>();
            light.Color.Value = new colorX(r, g, b, 1f);
        }

        _tracker.Register(trackAs, lightSlot);

        return Ok(id, new JObject
        {
            ["slotName"] = trackAs,
            ["refId"] = lightSlot.ReferenceID.ToString(),
            ["lightRefId"] = light.ReferenceID.ToString(),
            ["lightType"] = lightType,
            ["intensity"] = intensity,
            ["range"] = range,
            ["shadows"] = shadows,
            ["trackedAs"] = trackAs
        });
    }
}
