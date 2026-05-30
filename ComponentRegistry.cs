using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Component type registry — maps short names to FrooxEngine types.
/// Also handles generic type resolution like "ValueGradientDriver<float>".
/// </summary>
internal static class ComponentRegistry
{
    // Component type lookup — short name → FrooxEngine type
    public static readonly Dictionary<string, Type> ComponentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // UIX Core
        ["Canvas"] = typeof(Canvas),
        ["Image"] = typeof(Image),
        ["Text"] = typeof(FrooxEngine.UIX.Text),
        ["Button"] = typeof(Button),
        ["Checkbox"] = typeof(FrooxEngine.UIX.Checkbox),
        ["TextField"] = typeof(FrooxEngine.UIX.TextField),
        ["RawImage"] = typeof(RawImage),
        ["Mask"] = typeof(Mask),
        ["ReferenceField"] = typeof(ReferenceField<Slot>),
        ["ScrollRect"] = typeof(ScrollRect),
        ["IgnoreLayout"] = typeof(FrooxEngine.UIX.IgnoreLayout),

        // UIX Layout
        ["RectTransform"] = typeof(RectTransform),
        ["VerticalLayout"] = typeof(VerticalLayout),
        ["HorizontalLayout"] = typeof(HorizontalLayout),
        ["GridLayout"] = typeof(FrooxEngine.UIX.GridLayout),
        ["LayoutElement"] = typeof(LayoutElement),
        ["ContentSizeFitter"] = typeof(ContentSizeFitter),

        // UIX Controls
        ["Slider"] = typeof(FrooxEngine.Slider),
        ["ProgressBar"] = typeof(FrooxEngine.UIX.ProgressBar),

        // Materials
        ["PBS_Metallic"] = typeof(PBS_Metallic),
        ["PBS_Specular"] = typeof(PBS_Specular),
        ["PBS_DualSidedMetallic"] = typeof(PBS_DualSidedMetallic),
        ["UnlitMaterial"] = typeof(UnlitMaterial),
        ["FresnelMaterial"] = typeof(FresnelMaterial),
        ["XiexeToonMaterial"] = typeof(XiexeToonMaterial),

        // Mesh Types
        ["BoxMesh"] = typeof(BoxMesh),
        ["SphereMesh"] = typeof(SphereMesh),
        ["CylinderMesh"] = typeof(CylinderMesh),
        ["ConeMesh"] = typeof(ConeMesh),
        ["QuadMesh"] = typeof(QuadMesh),
        ["TorusMesh"] = typeof(TorusMesh),
        ["BevelBoxMesh"] = typeof(BevelBoxMesh),
        ["BevelPlaneMesh"] = typeof(BevelPlaneMesh),
        ["BevelStripeMesh"] = typeof(BevelStripeMesh),
        ["TriangleMesh"] = typeof(TriangleMesh),
        ["CapsuleMesh"] = typeof(CapsuleMesh),
        ["CircleMesh"] = typeof(CircleMesh),
        ["CurvedPlaneMesh"] = typeof(CurvedPlaneMesh),
        ["IcoSphereMesh"] = typeof(IcoSphereMesh),
        ["GridMesh"] = typeof(GridMesh),
        ["TubeMesh"] = typeof(TubeMesh),
        ["RingMesh"] = typeof(RingMesh),

        // Rendering
        ["MeshRenderer"] = typeof(MeshRenderer),
        ["SkinnedMeshRenderer"] = typeof(SkinnedMeshRenderer),
        ["TextRenderer"] = typeof(TextRenderer),
        ["SpriteProvider"] = typeof(SpriteProvider),
        ["StaticMesh"] = typeof(StaticMesh),

        // Assets / Textures
        ["StaticTexture2D"] = typeof(StaticTexture2D),

        // Audio
        ["StaticAudioClip"] = typeof(StaticAudioClip),
        ["AudioClipPlayer"] = typeof(AudioClipPlayer),
        ["AudioOutput"] = typeof(AudioOutput),
        ["AudioListener"] = typeof(AudioListener),

        // Video
        ["VideoTextureProvider"] = typeof(VideoTextureProvider),

        // Physics & Interaction
        ["BoxCollider"] = typeof(BoxCollider),
        ["SphereCollider"] = typeof(SphereCollider),
        ["CapsuleCollider"] = typeof(CapsuleCollider),
        ["MeshCollider"] = typeof(MeshCollider),
        ["CharacterController"] = typeof(CharacterController),
        ["Grabbable"] = typeof(Grabbable),
        ["PhysicalButton"] = typeof(PhysicalButton),
        ["TouchButton"] = typeof(TouchButton),
        ["ContextMenuItemSource"] = typeof(ContextMenuItemSource),
        ["InteractionHandler"] = typeof(InteractionHandler),

        // Lighting & Environment
        ["Light"] = typeof(Light),
        ["AmbientLightSH2"] = typeof(AmbientLightSH2),
        ["ReflectionProbe"] = typeof(ReflectionProbe),
        ["Skybox"] = typeof(Skybox),

        // Animation / Motion
        ["Spinner"] = typeof(Spinner),
        ["Wiggler"] = typeof(Wiggler),
        ["Panner1D"] = typeof(Panner1D),
        ["Panner2D"] = typeof(Panner2D),
        ["LinearMapper1D"] = typeof(LinearMapper1D),
        ["LinearMapper2D"] = typeof(LinearMapper2D),
        ["LinearMapper3D"] = typeof(LinearMapper3D),
        ["LinearMapper4D"] = typeof(LinearMapper4D),

        // Particles (PhotonDust)
        ["ParticleSystem"] = typeof(FrooxEngine.PhotonDust.ParticleSystem),
        ["ParticleStyle"] = typeof(FrooxEngine.PhotonDust.ParticleStyle),
        ["PointEmitter"] = typeof(FrooxEngine.PhotonDust.PointEmitter),
        ["ConeEmitter"] = typeof(FrooxEngine.PhotonDust.ConeEmitter),
        ["BoxEmitter"] = typeof(FrooxEngine.PhotonDust.BoxEmitter),
        ["SphereEmitter"] = typeof(FrooxEngine.PhotonDust.SphereEmitter),

        // Dynamic Variables
        ["DynamicVariableSpace"] = typeof(DynamicVariableSpace),

        // Utility
        ["SmoothTransform"] = typeof(SmoothTransform),
        ["Comment"] = typeof(Comment),
    };

    /// <summary>Resolve a component type by short name, full name, or generic syntax.</summary>
    public static Type Resolve(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Check built-in map first
        if (ComponentTypes.TryGetValue(typeName, out var type))
            return type;

        // Check for generic type syntax like "ValueGradientDriver<float>"
        if (typeName.Contains('<') && typeName.Contains('>'))
        {
            type = ResolveGenericType(typeName);
            if (type != null) return type;
        }

        // Try full qualified name via reflection
        // Search in FrooxEngine assembly
        type = typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.{typeName}", false, true);
        if (type != null) return type;

        // Search in UIX namespace
        type = typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.UIX.{typeName}", false, true);
        if (type != null) return type;

        // Search in PhotonDust namespace
        type = typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.PhotonDust.{typeName}", false, true);
        if (type != null) return type;

        return null;
    }

    /// <summary>Resolve generic types like "ValueGradientDriver&lt;float&gt;" or "Tween&lt;float3&gt;"</summary>
    private static Type ResolveGenericType(string typeName)
    {
        // Parse "BaseName<Arg1, Arg2, ...>"
        int ltPos = typeName.IndexOf('<');
        int gtPos = typeName.LastIndexOf('>');
        if (ltPos < 0 || gtPos < 0 || gtPos <= ltPos) return null;

        string baseName = typeName[..ltPos].Trim();
        string argsStr = typeName[(ltPos + 1)..gtPos].Trim();
        var argNames = argsStr.Split(',').Select(s => s.Trim()).ToArray();
        int arity = argNames.Length;

        // Resolve the open generic type (e.g., ValueGradientDriver`1)
        string genericBaseName = $"{baseName}`{arity}";
        Type openType = typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.{genericBaseName}", false, true);
        if (openType == null)
            openType = typeof(FrooxEngine.Slot).Assembly.GetType(genericBaseName, false, true);
        if (openType == null || !openType.IsGenericTypeDefinition)
            return null;

        // Resolve each type argument
        var typeArgs = new Type[arity];
        for (int i = 0; i < arity; i++)
        {
            typeArgs[i] = ResolveTypeArgument(argNames[i]);
            if (typeArgs[i] == null) return null;
        }

        try
        {
            return openType.MakeGenericType(typeArgs);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolve a type name for use as a generic argument.</summary>
    public static Type ResolveTypeArgument(string name)
    {
        // Common C# aliases
        return name.ToLowerInvariant() switch
        {
            "float" => typeof(float),
            "single" => typeof(float),
            "double" => typeof(double),
            "int" => typeof(int),
            "int32" => typeof(int),
            "long" => typeof(long),
            "int64" => typeof(long),
            "bool" => typeof(bool),
            "boolean" => typeof(bool),
            "string" => typeof(string),
            "byte" => typeof(byte),
            "short" => typeof(short),
            "uint" => typeof(uint),
            // Elements.Core types
            "float2" => typeof(Elements.Core.float2),
            "float3" => typeof(Elements.Core.float3),
            "float4" => typeof(Elements.Core.float4),
            "floatq" => typeof(Elements.Core.floatQ),
            "colorx" => typeof(Elements.Core.colorX),
            "color" => typeof(Elements.Core.colorX),
            // Try FrooxEngine types
            _ => typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.{name}", false, true)
                 ?? Type.GetType(name, false, true)
        };
    }
}
