# AntigravityBridge

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod that exposes a localhost HTTP API for building things in [Resonite](https://resonite.com) programmatically. Designed to be used by **AI agents** (like Antigravity) and **developer scripts** — not as a standalone GUI tool.

## How It Works

AntigravityBridge is the **connection layer** between an external tool and Resonite's engine:

```
You (natural language) → AI Agent (Antigravity) → HTTP API → AntigravityBridge mod → Resonite
     "build me a                translates to              executes on          objects appear
      wiki panel"               JSON commands              engine thread         in-world
```

### For AI Agents
The primary use case. You chat with an AI agent that has access to your local machine. The agent sends HTTP requests to `localhost:9090` to create slots, attach components, build UI, and manipulate the scene — all while you describe what you want in plain English.

The agent can:
- **Build entire UI panels** from a single `buildUIXTree` command
- **Create 3D objects** with meshes, materials, and transforms via `createPrimitive`
- **Read and modify** existing world content using `trackExistingSlot` and field getters/setters
- **Iterate on designs** by inspecting what was built (`getSlotInfo`, `getComponentFields`) and adjusting

### For Developers
You can also script against the API directly using any HTTP client — PowerShell, Python, curl, Node.js, etc. The `/help` endpoint returns the full API schema as JSON, and the `/batch` endpoint lets you execute hundreds of commands in a single engine frame.

## Requirements

- [Resonite](https://store.steampowered.com/app/2519830/Resonite/) (Steam)
- [ResoniteModLoader (RML)](https://github.com/resonite-modding-group/ResoniteModLoader) v5.0.0+
- .NET 10 SDK (only if building from source)

## Installation

### Option 1: Pre-built Release
1. Download `AntigravityBridge.dll` from the [Releases](https://github.com/NeoShadow7366/Resonite-Antigravity-Bridge-/releases) page
2. Place it in your Resonite mods folder:
   ```
   <Resonite Install>/rml_mods/AntigravityBridge.dll
   ```
   Example: `E:\SteamLibrary\steamapps\common\Resonite\rml_mods\AntigravityBridge.dll`

### Option 2: Build from Source
```bash
git clone https://github.com/NeoShadow7366/Resonite-Antigravity-Bridge-.git
cd Resonite-Antigravity-Bridge-

# Edit AntigravityBridge.csproj if your Resonite is not at the default path
dotnet build -c Release

# Copy the DLL to your mods folder
copy bin\Release\AntigravityBridge.dll "<Resonite Install>\rml_mods\"
```

### Verify ResoniteModLoader is Installed
If you don't have RML yet:
1. Download the latest RML from [GitHub](https://github.com/resonite-modding-group/ResoniteModLoader/releases)
2. Follow the [installation guide](https://github.com/resonite-modding-group/ResoniteModLoader#installation)
3. Make sure a `rml_mods` folder exists in your Resonite directory

## Verifying the Connection

1. **Launch Resonite** with mods enabled
2. Open a world (the mod needs a focused world to operate)
3. Check the Resonite log — you should see:
   ```
   AntigravityBridge v1.0.0 listening on http://localhost:9090/
   Endpoints: /cmd, /batch, /ping, /tracker, /help, /status, /ws
   ```
4. **Test the connection** from any terminal:

   **PowerShell:**
   ```powershell
   Invoke-RestMethod http://localhost:9090/ping
   ```

   **curl:**
   ```bash
   curl http://localhost:9090/ping
   ```

   You should get back:
   ```json
   {"status": "ok", "message": "pong", "trackedSlots": 0}
   ```

If you see that response, **AntigravityBridge is connected and ready** for your AI agent or scripts.

## Sending Commands

All commands are sent as JSON via HTTP POST. Every command has an `action` and `params`:

### Single Command (`POST /cmd`)

**PowerShell:**
```powershell
$body = @{
    id = "1"
    action = "createSlot"
    params = @{ name = "MySlot" }
} | ConvertTo-Json

Invoke-RestMethod -Uri http://localhost:9090/cmd -Method POST -Body $body -ContentType "application/json"
```

**curl:**
```bash
curl -X POST http://localhost:9090/cmd \
  -H "Content-Type: application/json" \
  -d '{"id":"1","action":"createSlot","params":{"name":"MySlot"}}'
```

### Batch Commands (`POST /batch`)

Send multiple commands in one request — they all execute in a single engine frame:

```powershell
$body = @{
    commands = @(
        @{ id = "1"; action = "createSlot"; params = @{ name = "Panel" } },
        @{ id = "2"; action = "attachComponent"; params = @{ slot = "Panel"; type = "Canvas" } },
        @{ id = "3"; action = "setField"; params = @{ slot = "Panel"; component = "Canvas"; field = "Size"; value = @(800, 600) } }
    )
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri http://localhost:9090/batch -Method POST -Body $body -ContentType "application/json"
```

### Build a Complete UI in One Call

The `buildUIXTree` command creates an entire UI hierarchy from a JSON tree:

```json
{
  "id": "1",
  "action": "buildUIXTree",
  "params": {
    "root": {
      "name": "MyPanel",
      "scale": [0.001],
      "components": [
        {"type": "Canvas", "fields": {"Size": [800, 600]}}
      ],
      "children": [
        {
          "name": "Title",
          "components": [
            {"type": "Text", "fields": {"Content": "Hello Resonite!", "Size": 32}},
            {"type": "Image", "fields": {"Tint": [0.1, 0.1, 0.15, 1.0]}}
          ]
        }
      ]
    }
  }
}
```

### Create a 3D Object

```json
{
  "id": "1",
  "action": "createPrimitive",
  "params": {
    "name": "RedSphere",
    "meshType": "SphereMesh",
    "color": [0.8, 0.2, 0.2, 1.0],
    "position": [0, 1.5, 0],
    "scale": [0.3, 0.3, 0.3]
  }
}
```

## API Reference

### Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/ping` | GET | Health check — returns `pong` and tracked slot count |
| `/cmd` | POST | Execute a single command |
| `/batch` | POST | Execute multiple commands in one engine frame |
| `/tracker` | GET | List all tracked slot name→RefID mappings |
| `/help` | GET | Full self-documenting API schema (JSON) |
| `/status` | GET | Server status — uptime, total commands processed, error count |
| `/ws` | WebSocket | Bidirectional streaming — send commands and receive responses over a persistent connection |

### Commands (79 total)

| Category | Commands |
|---|---|
| **Scene Graph** | `createSlot`, `destroySlot`, `destroyChildren`, `reparentSlot`, `findSlot`, `duplicateSlot`, `listChildren`, `trackExistingSlot`, `getSlotsByTag` |
| **Slot Properties** | `setSlotActive`, `setSlotTransform`, `getSlotTransform`, `setSlotName`, `setSlotTag`, `setSlotOrderIndex`, `getSlotInfo`, `setSlotPersist` |
| **Components** | `attachComponent`, `removeComponent`, `findComponents`, `getRegisteredComponents` |
| **Fields** | `setField`, `setFields`, `getComponentField`, `getComponentFields` |
| **Dynamic Variables** | `createDynVarSpace`, `createDynVar`, `readDynVar`, `writeDynVar` |
| **Assets** | `importTexture`, `importMesh` |
| **High-Level** | `createPrimitive`, `buildUIXTree` |
| **Physics** | `makePhysicsObject` |
| **Audio Import** | `importAudio` |
| **Video Import** | `importVideo` |
| **Environment** | `setupEnvironment`, `createLight` |
| **Particles** | `createParticleSystem` |
| **Animation** | `createAnimation` |
| **ProtoFlux** | `createProtoFluxNode`, `connectProtoFlux`, `setProtoFluxInput`, `getProtoFluxNode` |
| **World & Session** | `getWorldInfo`, `getUserInfo`, `getUsers` |
| **Templates** | `snapshotSlot`, `saveTemplate`, `stampTemplate`, `listTemplates`, `deleteTemplate` |
| **Events** | `subscribe`, `unsubscribe`, `listSubscriptions` |
| **Reference Wiring** | `wireReference`, `addToList`, `getComponentByRefId`, `getAllComponents` |
| **Component Utils** | `copyComponent`, `removeFromList` |
| **Hierarchy Navigation** | `findSlotByPath`, `findSlots`, `getParent`, `getSlotHierarchy` |
| **Materials** | `createMaterial` |
| **3D Text** | `create3DText` |
| **Measurement** | `measureDistance` |
| **Bulk Operations** | `setFieldOnChildren`, `duplicateSlotArray` |
| **User Control** | `moveUser` |
| **Utility** | `ping`, `log`, `clearTracker` |

### Component Index Disambiguation

Commands that target a component on a slot support an optional `componentIndex` parameter (0-based, default `0`). This lets you target the 2nd, 3rd, etc. component of the same type when a slot has multiple instances. Affects: `setField`, `setFields`, `getComponentField`, `getComponentFields`, `removeComponent`.

### Slot Lookup by RefID

Any command that accepts a slot name can also accept a RefID string (hex format from `ReferenceID.ToString()`). If the name isn't found in the tracker, the bridge tries to resolve it as a RefID directly from the world.

### Supported Field Types (18)

`string`, `bool`, `int`, `long`, `float`, `double`, `float2`, `float3`, `float4`, `floatQ`, `colorX`, `Uri`, `enum` (auto-detected), `SyncRef` (auto-detected)

### Registered Component Types (73)

<details>
<summary>Click to expand full list</summary>

**UIX Core:** Canvas, Image, Text, Button, Mask, RawImage, TextField, Checkbox

**UIX Layout:** RectTransform, VerticalLayout, HorizontalLayout, GridLayout, LayoutElement, ContentSizeFitter, ScrollRect, IgnoreLayout

**UIX Controls:** Slider, ProgressBar

**Textures & Sprites:** StaticTexture2D, SpriteProvider

**Materials:** UnlitMaterial, PBS_Metallic, PBS_Specular, FresnelMaterial, XiexeToonMaterial, PBS_DualSidedMetallic

**Meshes & Rendering:** BoxMesh, QuadMesh, SphereMesh, CylinderMesh, ConeMesh, StaticMesh, MeshRenderer, SkinnedMeshRenderer, TextRenderer, TorusMesh, BevelBoxMesh, BevelPlaneMesh, BevelStripeMesh, TriangleMesh, CapsuleMesh, CircleMesh, CurvedPlaneMesh, IcoSphereMesh, GridMesh, TubeMesh, RingMesh

**Lighting:** Light, ReflectionProbe, Skybox, AmbientLightSH2

**Colliders:** BoxCollider, SphereCollider, CapsuleCollider, MeshCollider

**Physics:** CharacterController

**Audio:** AudioClipPlayer, AudioOutput, StaticAudioClip, AudioListener

**Video:** VideoTextureProvider

**Interaction:** Grabbable, PhysicalButton, TouchButton, ContextMenuItemSource, InteractionHandler

**Animation / Motion:** Spinner, Wiggler, Panner1D, Panner2D, LinearMapper1D, LinearMapper2D, LinearMapper3D, LinearMapper4D

**Particles (PhotonDust):** ParticleSystem, ParticleStyle, PointEmitter, ConeEmitter, BoxEmitter, SphereEmitter

**Dynamic Variables:** DynamicVariableSpace

**Utility:** SmoothTransform, Comment

> Components not in this list can still be attached using their full FrooxEngine type name.
> Generic components like `ValueGradientDriver<float>`, `Tween<colorX>`, `ValueCopy<float3>` are also supported — pass the generic syntax directly as the type name.

</details>

### Physics & Audio

Two high-level commands handle common multi-component setups in a single call:

- **`makePhysicsObject`** — Attaches a collider (box, sphere, capsule, or mesh), a `CharacterController`, and optionally a `Grabbable` to an existing slot. One call replaces three separate `attachComponent` commands.
- **`importAudio`** — Creates a complete audio pipeline from a URL: `StaticAudioClip` → `AudioClipPlayer` → `AudioOutput`, with configurable spatial blend. Returns RefIDs for all three components.

### ProtoFlux

ProtoFlux is Resonite's visual programming system. The bridge exposes four commands for creating and wiring ProtoFlux nodes programmatically:

- **`createProtoFluxNode`** — Instantiate any ProtoFlux node type on a slot
- **`connectProtoFlux`** — Wire an output of one node to an input of another
- **`setProtoFluxInput`** — Set a constant/literal value on a node input
- **`getProtoFluxNode`** — Inspect a node's current inputs, outputs, and impulses

This enables AI agents and scripts to build logic graphs — conditionals, math, event handlers, and more — entirely through the API.

### Templates & Snapshots

The bridge can serialize slot hierarchies to JSON — capturing names, transforms, components, and field values — and save them as named templates for reuse. This enables:

- **Snapshots** — capture the state of a slot tree at a point in time (`snapshotSlot`)
- **Save as template** — store a snapshot under a name for later use (`saveTemplate`)
- **Stamp copies** — instantiate a template under any parent slot (`stampTemplate`)
- **Undo patterns** — snapshot before making changes, then refer back to the snapshot to see what was there if you need to restore it

Commands: `snapshotSlot`, `saveTemplate`, `stampTemplate`, `listTemplates`, `deleteTemplate`.

### Event Subscriptions

The bridge supports a real-time event system for monitoring changes in the Resonite world. Subscribe to events via `/cmd` or `/ws`, and receive event notifications as JSON messages on the WebSocket connection at `/ws`.

**Event types:**
- `fieldChanged` — fires when a tracked field value changes
- `slotChildrenChanged` — fires when children are added/removed from a slot
- `slotDestroyed` — fires when a tracked slot is destroyed
- `userJoin` — fires when a user joins the session
- `userLeave` — fires when a user leaves the session

Commands: `subscribe` (create a subscription), `unsubscribe` (remove by ID or all), `listSubscriptions` (list active subscriptions). Requires a WebSocket connection at `/ws` to receive event messages.

### Hierarchy Navigation

The bridge provides commands for navigating and inspecting the slot hierarchy:

- **`findSlotByPath`** — Navigate to a slot using a slash-delimited path (e.g., `"Root/Panel/Header"`). Supports `..` (parent) and `.` (current), case-insensitive matching, and substring fallback.
- **`findSlots`** — Multi-result search by name, tag, or regex pattern. Returns up to 50 results with optional `trackAll`.
- **`getParent`** — Returns the parent slot of a given slot and tracks it for further commands.
- **`getSlotHierarchy`** — Returns a nested tree view of a slot's children with component info, child counts, and truncation markers for deep hierarchies.

These complement the existing `findSlot` and `listChildren` commands with richer navigation and search capabilities.

### Reference Wiring

The bridge supports wiring any `ISyncRef` field to any world element by RefID, and inspecting components anywhere in the world — not just ones created through the bridge.

- **`wireReference`** — Wire any `ISyncRef` field to a target element by RefID. Supports dotted field paths for nested access (e.g., `Materials._elements.0`).
- **`addToList`** — Append items to `SyncList` fields (e.g., adding a material to `MeshRenderer.Materials`).
- **`getComponentByRefId`** — Look up any component or slot by RefID anywhere in the world. Returns its type, fields, and current values.
- **`getAllComponents`** — List ALL components on a slot with their types, RefIDs, and field names — not limited to registered shortcuts.

These commands enable precise low-level wiring (e.g., pointing a material's texture reference at a specific `StaticTexture2D` by RefID) and full component inspection of any slot in the world.

### Environment & Lighting

Two high-level commands for setting up world environments and placing lights:

- **`setupEnvironment`** — One-call skybox, ambient light, and reflection probe setup. Pass a skybox texture URL and ambient light color to configure the entire environment in a single command.
- **`createLight`** — Creates a fully configured light source (point, directional, or spot) with color, intensity, shadow settings, and position. One call replaces multiple `createSlot` + `attachComponent` + `setField` commands.

### Particles

High-level particle system creation in a single call:

- **`createParticleSystem`** — Creates a complete PhotonDust particle system with emitter, style, and renderer. Supports `point`, `cone`, `box`, and `sphere` emitter types with configurable color, size, emission rate, lifetime, and speed. One call replaces what would be 5+ manual component attachments.

### Animation

Drive animated property changes with gradient keyframes:

- **`createAnimation`** — Creates a `ValueGradientDriver` with JSON-defined keyframes and wires it to a target field. Supports animating float, color, and other value types over time. The driver and keyframes are configured in a single call.

### Video & Media

High-level video import and display:

- **`importVideo`** — Creates a complete video playback setup: `VideoTextureProvider` + display quad + material, all auto-wired. Similar to `importAudio` but for video content. Returns RefIDs for the video provider, renderer, and material.

### Component Utilities

Commands for duplicating and managing components:

- **`copyComponent`** — Duplicates a component from one slot to another. Copies all field values from the source component to a new instance on the target slot.
- **`removeFromList`** — Removes an item from a `SyncList` field by index. The inverse of `addToList` — useful for cleaning up material lists, removing entries from collections, etc.

### Materials

High-level material creation and configuration:

- **`createMaterial`** — Creates a PBR material on a slot with color, metallic, and smoothness settings. Automatically wires the material to a `MeshRenderer` on a specified renderer slot if provided. One call replaces multiple `attachComponent` + `setField` + `wireReference` commands.

### 3D Text

High-level 3D text creation:

- **`create3DText`** — Creates a complete 3D text object with `TextRenderer` and `UnlitMaterial`, all wired together. Supports font size, color, alignment, and positioning. One call replaces the full text rendering pipeline setup.

### Measurement

Spatial measurement utilities:

- **`measureDistance`** — Measures the world-space distance between two tracked slots. Returns the distance, both positions, and the delta vector between them.

### Bulk Operations

Commands for operating on multiple slots or components at once:

- **`setFieldOnChildren`** — Sets a field value on all matching components across a slot's descendant hierarchy. Useful for bulk-updating colors, visibility, or any shared property. Supports optional depth limiting.
- **`duplicateSlotArray`** — Creates N copies of a slot with uniform spacing between them. All copies are automatically tracked with a numbered prefix. Useful for building grids, rows, or arrays of repeated objects.

### User Control

Commands for controlling the local user:

- **`moveUser`** — Teleports the local user to specific world coordinates or to the position of a tracked slot. Accepts position and rotation, or a target slot reference.

### Persistence

Control whether slots survive across sessions:

- **`setSlotPersist`** — Sets or clears the `Persistent` flag on a slot, controlling whether it persists when the world is saved and reloaded.

### WebSocket

The `/ws` endpoint provides a persistent WebSocket connection for bidirectional streaming. Connect to `ws://localhost:9090/ws` and send JSON commands in the same format as `/cmd`. Responses are pushed back over the same connection as they complete. This is useful for long-running sessions, real-time feedback, and receiving event subscription notifications.

## Configuration

The mod has two config options (set via RML's config system):

| Key | Default | Description |
|---|---|---|
| `Port` | `9090` | HTTP server port |
| `VerboseLogging` | `false` | Log every command and response to the Resonite console |

## Troubleshooting

| Problem | Solution |
|---|---|
| Can't connect / connection refused | Make sure Resonite is running with mods enabled and you're in a world |
| "No focused world" errors | Join or create a world first — the mod needs an active world |
| Mod not loading | Check that `AntigravityBridge.dll` is in the `rml_mods` folder and RML is installed |
| Commands timing out | The engine thread may be busy — try simpler commands first |
| Can't update the DLL | Close Resonite first — the DLL is locked while running |

## Security

The HTTP server **only listens on localhost** (`http://localhost:9090/`). It is not accessible from other machines on your network. There is a 10 MB request body limit to prevent abuse.

## License

MIT
