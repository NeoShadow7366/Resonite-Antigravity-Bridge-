---
name: verify-frooxengine-types
description: Verify that FrooxEngine component types exist before registering them in the mod. Use MetadataLoadContext reflection to inspect Resonite DLLs without loading them into the runtime. Use this before adding new component shortcuts or when debugging type resolution failures.
---

# Verify FrooxEngine Types Skill

## When to Use
- Before adding new component shortcuts to `ComponentTypes` dictionary
- When a user reports `attachComponent` failing with an unknown type
- When implementing generic type resolution for new base types
- To discover available fields on a component

## MetadataLoadContext Setup

The reflection project lives at:
```
C:\Users\H6\.gemini\antigravity\brain\77eda754-6f08-4dbc-8d8f-1ad4ae9d1750\scratch\pfx_inspect\
```

### Project file: `pfx_inspect.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Reflection.MetadataLoadContext" Version="9.0.4" />
  </ItemGroup>
</Project>
```

### Template Script: `Program.cs`
```csharp
using System.Reflection;
using System.Runtime.InteropServices;

var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();

// Load ALL runtime + Resonite DLLs to avoid resolution failures
var paths = new List<string>();
foreach (var f in Directory.GetFiles(runtimeDir, "*.dll"))
    paths.Add(f);
foreach (var f in Directory.GetFiles(@"E:\SteamLibrary\steamapps\common\Resonite", "*.dll"))
    paths.Add(f);

var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver);
var asm = mlc.LoadFromAssemblyPath(@"E:\SteamLibrary\steamapps\common\Resonite\FrooxEngine.dll");

Type[] allTypes;
try { allTypes = asm.GetTypes(); }
catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray()!; }

// === YOUR QUERIES HERE ===
```

## Common Queries

### Search for types by name
```csharp
void SafeSearch(string label, Func<Type, bool> filter)
{
    Console.WriteLine($"\n=== {label} ===");
    foreach (var t in allTypes.Where(filter).OrderBy(t => t.FullName))
    {
        string info = t.IsGenericTypeDefinition ? $" GenericArgs={t.GetGenericArguments().Length}" : "";
        Console.WriteLine($"  {t.FullName}{info}");
    }
}

SafeSearch("MyComponent", t => t.Name.Contains("MyComponent"));
```

### Enumerate methods on a type
```csharp
var myType = allTypes.FirstOrDefault(t => t.Name == "UniversalImporter");
if (myType != null)
    foreach (var m in myType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
```

### Check if a type is generic
```csharp
SafeSearch("Generic Types", t => t.IsGenericTypeDefinition && t.Name.Contains("ValueGradientDriver"));
// Output: FrooxEngine.ValueGradientDriver`1 GenericArgs=1
```

### List fields on a component
```csharp
var comp = allTypes.FirstOrDefault(t => t.Name == "Light");
if (comp != null)
    foreach (var f in comp.GetFields(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {f.Name}: {f.FieldType.Name}");
```

## Running the Script
```powershell
cd C:\Users\H6\.gemini\antigravity\brain\77eda754-6f08-4dbc-8d8f-1ad4ae9d1750\scratch\pfx_inspect
dotnet run 2>&1
```

## Key Facts Discovered

### Resonite DLL Locations
```
E:\SteamLibrary\steamapps\common\Resonite\FrooxEngine.dll          — Core engine, components
E:\SteamLibrary\steamapps\common\Resonite\Elements.Core.dll        — float3, colorX, etc.
E:\SteamLibrary\steamapps\common\Resonite\Elements.Assets.dll      — Asset types
E:\SteamLibrary\steamapps\common\Resonite\ProtoFlux.Core.dll       — ProtoFlux base types
E:\SteamLibrary\steamapps\common\Resonite\ProtoFlux.Nodes.Core.dll — ProtoFlux node implementations
E:\SteamLibrary\steamapps\common\Resonite\Awwdio.dll               — Audio engine interfaces
```

### Namespace Patterns
- Most components: `FrooxEngine.{TypeName}`
- UIX components: `FrooxEngine.UIX.{TypeName}`  
- Particles: `FrooxEngine.PhotonDust.{TypeName}`
- Generic components: `FrooxEngine.{TypeName}\`N` where N = generic arity

### Known Generic Components (1 type parameter)
- `ValueGradientDriver<T>` — animation driver with keyframe points
- `Tween<T>` — smooth value transitions
- `SmoothValue<T>` — smoothed value tracking
- `ValueCopy<T>` — copy one field value to another

### Known Non-Generic Variants
- `LinearMapper1D`, `LinearMapper2D`, `LinearMapper3D`, `LinearMapper4D` — NOT generic

### API Gotchas
- `Component.Duplicate(Slot)` does NOT exist. Use `Slot.DuplicateComponent(Component)` instead.
- `Light.ShadowsEnabled` does NOT exist as a direct property. Use `GetSyncMember()` reflection.
- `AudioOutput.Source.Target` requires `Awwdio.dll` assembly reference.
- `MeshRenderer.Materials.Add().Target` expects `IAssetProvider<Material>`, not `Component`.
