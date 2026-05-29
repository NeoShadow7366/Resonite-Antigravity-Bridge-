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
   Endpoints: /cmd, /batch, /ping, /tracker, /help
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

### Commands (35 total)

| Category | Commands |
|---|---|
| **Scene Graph** | `createSlot`, `destroySlot`, `destroyChildren`, `reparentSlot`, `findSlot`, `duplicateSlot`, `listChildren`, `trackExistingSlot`, `getSlotsByTag` |
| **Slot Properties** | `setSlotActive`, `setSlotTransform`, `getSlotTransform`, `setSlotName`, `setSlotTag`, `setSlotOrderIndex`, `getSlotInfo` |
| **Components** | `attachComponent`, `removeComponent` |
| **Fields** | `setField`, `setFields`, `getComponentField`, `getComponentFields` |
| **Dynamic Variables** | `createDynVarSpace`, `createDynVar`, `readDynVar`, `writeDynVar` |
| **Assets** | `importTexture`, `importMesh` |
| **High-Level** | `createPrimitive`, `buildUIXTree` |
| **Utility** | `ping`, `log`, `clearTracker` |

### Supported Field Types (18)

`string`, `bool`, `int`, `long`, `float`, `double`, `float2`, `float3`, `float4`, `floatQ`, `colorX`, `Uri`, `enum` (auto-detected), `SyncRef` (auto-detected)

### Registered Component Types (43)

<details>
<summary>Click to expand full list</summary>

**UIX Core:** Canvas, Image, Text, Button, Mask, RawImage, TextField, Checkbox

**UIX Layout:** RectTransform, VerticalLayout, HorizontalLayout, GridLayout, LayoutElement, ContentSizeFitter, ScrollRect, IgnoreLayout

**Textures & Sprites:** StaticTexture2D, SpriteProvider

**Materials:** UnlitMaterial, PBS_Metallic, PBS_Specular

**Meshes & Rendering:** BoxMesh, QuadMesh, SphereMesh, CylinderMesh, ConeMesh, StaticMesh, MeshRenderer, SkinnedMeshRenderer, TextRenderer

**Lighting:** Light

**Colliders:** BoxCollider, SphereCollider, CapsuleCollider, MeshCollider

**Audio:** AudioClipPlayer, AudioOutput

**Interaction:** Grabbable

**Animation:** Spinner, Wiggler, Panner1D, Panner2D

**Dynamic Variables:** DynamicVariableSpace

**Utility:** SmoothTransform, Comment

> Components not in this list can still be attached using their full FrooxEngine type name.

</details>

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
