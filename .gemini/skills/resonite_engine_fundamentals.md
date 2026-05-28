# Resonite Engine Fundamentals

## Runtime Environment
- **Framework**: .NET 10 (`net10.0`)
- **Entry point**: `Renderite.Host.exe` (not `Resonite.exe` — that's the launcher)
- **Runtime config**: `Renderite.Host.runtimeconfig.json` confirms `net10.0`
- **Mod system**: ResoniteModLoader (RML) — DLLs placed in `rml_mods/` directory

## Slot Hierarchy

### What is a Slot?
A Slot is the fundamental scene graph node in FrooxEngine. It's equivalent to a GameObject
in Unity or a Node in Godot. Every object in Resonite is a Slot with Components attached.

### Key Properties
| Property | Type | Description |
|---|---|---|
| `Name` | string | Display name |
| `Tag` | string | Optional tag for filtering |
| `ActiveSelf` | bool | Whether this slot is active (independent of parent) |
| `LocalPosition` | float3 | Position relative to parent |
| `LocalRotation` | floatQ | Rotation relative to parent |
| `LocalScale` | float3 | Scale relative to parent |
| `Children` | IEnumerable | Child slots |
| `ChildrenCount` | int | Number of direct children |
| `Components` | IEnumerable | Attached components |
| `ReferenceID` | RefID | Unique identifier in the world |

### World Root
- `Engine.Current.WorldManager.FocusedWorld.RootSlot` — the absolute root
- All user-created content lives under the root slot
- The AntigravityBridge's `__root__` alias maps to the focused world's root slot

## Components

### What is a Component?
Components are behaviors/data attached to Slots. A Slot can have multiple components.
Components have fields (properties) that are Sync objects.

### Sync<T> Fields
All component properties are wrapped in `Sync<T>` objects for networking/persistence:
```csharp
// Reading a value
float spacing = verticalLayout.Spacing.Value;

// Writing a value  
verticalLayout.Spacing.Value = 4.0f;

// Getting a sync member by name (used by the bridge)
ISyncMember member = component.GetSyncMember("Spacing");
```

### Common UIX Components
| Component | Purpose | Auto-adds |
|---|---|---|
| `Canvas` | Root of UIX hierarchy, defines pixel space | RectTransform, BoxCollider |
| `RectTransform` | 2D positioning within Canvas | — |
| `Image` | Colored rectangle / sprite renderer | RectTransform |
| `Text` | Text renderer | RectTransform |
| `Button` | Click interaction handler | — |
| `Mask` | Clips children to parent bounds | — |
| `VerticalLayout` | Stack children vertically | — |
| `HorizontalLayout` | Stack children horizontally | — |
| `LayoutElement` | Override layout sizing | — |
| `ContentSizeFitter` | Auto-size based on content | — |
| `ScrollRect` | Scrollable content area | — |

## Threading Model

### Engine Thread
FrooxEngine is **single-threaded** for scene operations. All Slot/Component manipulation
must happen on the engine thread.

### RunSynchronously
```csharp
Engine.Current.WorldManager.FocusedWorld.RunSynchronously(() =>
{
    // This code runs on the engine thread
    // Safe to create slots, attach components, set fields
    var slot = parent.AddSlot("Name");
    slot.AttachComponent<Image>();
});
```

### Background Work
HTTP requests, file I/O, and computation can run on background threads.
Only marshal to the engine thread when touching FrooxEngine objects.

## Dynamic Variables (DynVars)

### DynVarSpace
A `DynamicVariableSpace` component defines a namespace for variables.
Variables are resolved by walking up the slot hierarchy to find the nearest matching space.

```csharp
var space = slot.AttachComponent<DynamicVariableSpace>();
space.SpaceName.Value = "WikiNavigator";
```

### DynamicValueVariable<T>
Stores a typed value that can be read/written from ProtoFlux.

```csharp
var dynVar = slot.AttachComponent<DynamicValueVariable<string>>();
dynVar.VariableName.Value = "WikiNavigator/SearchQuery";
dynVar.Value.Value = "default value";
```

### Supported Types
`string`, `bool`, `int`, `float`, `float2`, `float3`, `colorX`, `Uri`, etc.

### Naming Convention
`SpaceName/VariableName` — e.g., `WikiNavigator/SearchQuery`
Internal/private vars use underscore prefix: `WikiNavigator/_DebounceSeq`

## UIX Canvas System

### Coordinate Space
- Canvas `Size` defines the pixel space (e.g., 1920×1080)
- `RectTransform` positions elements within this space using anchors and offsets
- The canvas Slot's `LocalScale` controls physical size in world space

### Canvas → World Size Formula
```
Physical width = Canvas.Size.X × Slot.LocalScale.X
Example: 1920 × 0.0003 = 0.576m (57.6cm)
```

### Anchor System
- `AnchorMin` / `AnchorMax` define relative positioning (0-1 range)
- `[0,0]` = bottom-left, `[1,1]` = top-right
- Full stretch: AnchorMin=[0,0], AnchorMax=[1,1], OffsetMin=[0,0], OffsetMax=[0,0]

## RML Mod Lifecycle

### Loading Order
1. Resonite starts → loads `Renderite.Host.exe`
2. RML hooks into initialization
3. RML scans `rml_mods/` for DLLs
4. Calls `OnEngineInit()` on each mod
5. Engine finishes initialization
6. Mods can now access `Engine.Current`

### Mod Entry Point
```csharp
public class AntigravityBridge : ResoniteMod
{
    public override string Name => "AntigravityBridge";
    public override string Version => "1.0.0";
    
    public override void OnEngineInit()
    {
        // Start HTTP server, register hooks, etc.
        Engine.Current.RunPostInit(() => {
            // Engine is fully initialized here
        });
    }
}
```

### Logging
```csharp
ResoniteMod.Msg("Info message");     // Normal log
ResoniteMod.Warn("Warning");         // Yellow warning
ResoniteMod.Error("Error");          // Red error
```
Logs appear in the Resonite log file and in-engine developer console.

## Key Namespaces
| Namespace | Contains |
|---|---|
| `FrooxEngine` | Core engine: Slot, Component, Engine, World |
| `FrooxEngine.UIX` | UIX components: Canvas, Image, Text, Button, layouts |
| `Elements.Core` | Math types: float2, float3, float4, floatQ, colorX |
| `SkyFrost.Base` | Networking, cloud services |
| `ResoniteModLoader` | Mod API: ResoniteMod base class |
