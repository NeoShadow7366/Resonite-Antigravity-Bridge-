# Resonite AntigravityBridge — Operations Reference

## Overview
AntigravityBridge is a ResoniteModLoader (RML) mod that runs an HTTP server inside Resonite,
allowing external tools to programmatically create UIX elements, set properties, and manage
the scene graph via JSON commands.

## Connection
- **URL**: `http://localhost:9090` (configurable via RML settings)
- **Protocol**: HTTP POST with JSON body
- **Content-Type**: `application/json`

## Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/ping` | GET | Health check |
| `/cmd` | POST | Execute single command |
| `/batch` | POST | Execute batch (single engine dispatch) |
| `/tracker` | GET | List tracked slots |
| `/help` | GET | Self-documenting API — returns all commands, params, field types, and registered components |
| `/status` | GET | Server status — uptime, total commands processed, error count |
| `/ws` | WebSocket | Bidirectional streaming — send JSON commands, receive responses over a persistent connection |

## Batch Format
```json
{
  "commands": [
    {"id": "unique_id", "action": "actionName", "params": {...}},
    {"id": "unique_id2", "action": "actionName2", "params": {...}}
  ],
  "options": {
    "stopOnError": true
  }
}
```

- **`options`** is optional. Omitting it preserves default behavior (run all commands).
- **`stopOnError`**: When `true`, the batch halts on the first failed command instead of continuing.
  This prevents cascading errors when later commands depend on earlier ones.

> **Performance**: All commands in a batch execute within a single engine thread dispatch.
> A 100-command batch makes 1 roundtrip to the engine thread instead of 100.
> This is dramatically faster than sending individual `/cmd` requests.

> **Auto-generated IDs**: Commands without an `id` field are auto-assigned IDs (`batch_0`, `batch_1`, ...).
> Each result includes a `commandIndex` field for easy correlation with the input array.

**Response fields**:
| Field | Description |
|---|---|
| `status` | `"ok"` (all passed), `"partial"` (some failed), or `"stopped"` (halted by stopOnError) |
| `total` | Total commands in the batch |
| `executed` | How many commands actually ran |
| `success` | Successful command count |
| `errors` | Failed command count |
| `results` | Array of individual command results |
| `stoppedAtIndex` | (Only when stopped) Index of the command that caused the halt |

**IMPORTANT**: When loading a build script JSON file that has extra fields like `description` or `note`,
you must extract only the `commands` array before sending. Use this PowerShell pattern:
```powershell
$raw = Get-Content "path/to/build.json" -Raw
$obj = $raw | ConvertFrom-Json
$batch = @{commands=$obj.commands; options=@{stopOnError=$true}} | ConvertTo-Json -Depth 10 -Compress
$result = Invoke-RestMethod -Uri "http://localhost:9090/batch" -Method POST -Body ([System.Text.Encoding]::UTF8.GetBytes($batch)) -ContentType "application/json"
Write-Host "Status: $($result.status) | Executed: $($result.executed)/$($result.total) | Success: $($result.success) | Errors: $($result.errors)"
```

## Commands Reference

### createSlot
Creates a new slot with optional transform, tag, and inline components+fields.
```json
{"id": "s1", "action": "createSlot", "params": {
  "name": "MyPanel",
  "parent": "__root__",
  "tag": "ui-panel",
  "active": true,
  "position": [0, 1.5, 0],
  "scale": [0.001],
  "components": [
    {"type": "Image", "fields": {"Tint": [0.14, 0.14, 0.18, 1.0]}},
    {"type": "LayoutElement", "fields": {"MinHeight": 100, "PreferredWidth": 400}}
  ]
}}
```
- **parent**: Use `__root__`, `__worldroot__`, `__localuser__`, or any tracked slot name.
- **tag**, **active**, **position**, **rotation**, **scale**: All optional. Set on the slot before components are attached.
- **components**: Optional array of `{"type": "...", "fields": {...}}`. Each is attached and its fields set in order.
- The created slot is automatically registered in the tracker by its `name`.
- **IMPORTANT**: Slot names must be unique in the tracker. If you reuse a name, it overwrites the previous reference.

### Slot Resolution
Any command that accepts a slot name also accepts a **RefID string** (hex format from `ReferenceID.ToString()`). If the name isn't found in the tracker, the bridge tries to resolve it as a RefID directly from the world.

### setSlotActive
```json
{"id": "s2", "action": "setSlotActive", "params": {"slot": "MySlot", "active": false}}
```

### setSlotTransform
```json
{"id": "s3", "action": "setSlotTransform", "params": {
  "slot": "MySlot",
  "position": [0, 1, 0],
  "rotation": [0, 90, 0],
  "scale": [1, 1, 1]
}}
```
- **rotation**: Accepts Euler angles `[x, y, z]` or quaternion `[x, y, z, w]`
- **scale**: Accepts `[x, y, z]` or uniform `[s]` (e.g. `[2]` = `[2, 2, 2]`)
- **Response** includes all three: `position`, `rotation` (as quaternion `[x,y,z,w]`), and `scale`. All fields are optional — only provided ones are set.

### getSlotTransform
Reads the local and global transform of a slot without modifying anything.
```json
{"id": "gt1", "action": "getSlotTransform", "params": {"slot": "MySlot"}}
```
Returns:
```json
{"local": {"position": [...], "rotation": [...], "scale": [...]},
 "global": {"position": [...], "rotation": [...], "scale": [...]}}
```

### listChildren
Lists children of a slot with details, optionally recursive.
```json
{"id": "lc1", "action": "listChildren", "params": {
  "slot": "__root__",
  "depth": 2,
  "trackAll": false
}}
```
- **depth**: How deep to recurse (default `1` = immediate children only, `-1` = unlimited).
- **trackAll**: When `true`, registers every found child in the tracker by its name.
- Returns a flat array with `depth` field on each entry indicating nesting level.
- Each entry includes: `name`, `refId`, `tag`, `active`, `depth`, `childCount`, `components` (type names array).

### destroySlot
```json
{"id": "s4", "action": "destroySlot", "params": {"slot": "MySlot"}}
```
Also purges any tracked descendants that were destroyed.
Response includes `trackerEntriesPurged` count.

### destroyChildren
Destroys all children but keeps the slot itself and its components.
```json
{"id": "s5", "action": "destroyChildren", "params": {"slot": "MySlot"}}
```
Also purges any tracked children that were destroyed.
Response includes `trackerEntriesPurged` count.

### attachComponent
```json
{"id": "c1", "action": "attachComponent", "params": {
  "slot": "MySlot",
  "type": "Image",
  "fields": {"Tint": [0.14, 0.14, 0.18, 1.0]}
}}
```
- `fields` is optional — sets component fields inline after attachment.
- Component types can be short names (see UIX Patterns skill for full list).
- Some components auto-add others (Canvas adds RectTransform + BoxCollider, Image adds RectTransform).

### setField
```json
{"id": "f1", "action": "setField", "params": {
  "slot": "MySlot",
  "component": "Image",
  "field": "Tint",
  "value": [1.0, 0.0, 0.0, 1.0]
}}
```
- **componentIndex**: Optional (default `0`). When a slot has multiple components of the same type, use this 0-based index to target the 2nd, 3rd, etc. instance.

#### Supported field types and value formats:
| Field Type | JSON Format | Example |
|---|---|---|
| `string` | `"text"` | `"Hello"` |
| `bool` | `true/false` | `true` |
| `int` | number | `42` |
| `long` | number | `9999999999` |
| `float` | number | `3.14` |
| `double` | number | `3.14159265358979` |
| `float2` | `[x, y]` | `[1920, 1080]` |
| `float3` | `[x, y, z]` | `[0, 1, 0]` |
| `float4` | `[x, y, z, w]` | `[0, 0, 0, 1]` |
| `floatQ` | `[x, y, z, w]` or `[euler_x, euler_y, euler_z]` | `[0, 0, 0, 1]` or `[0, 90, 0]` |
| `colorX` | `[r, g, b, a]` or `"#RRGGBB"` | `[0.14, 0.14, 0.18, 1.0]` or `"#1A1A21"` |
| `Uri` | `"https://..."` | `"resdb:///abc123"` |
| `enum` | `"EnumValueName"` | `"PreferredSize"`, `"Horizontal"` |
| `SyncRef` | `"trackedSlotName"` or `"null"` | `"MyPanel"` |

**Enum support**: All enum-typed fields (e.g., `SizeFit`, `Alignment`, `LayoutAxis`) are handled
automatically via reflection. Pass the enum value name as a case-insensitive string.

**SyncRef support**: Reference fields (e.g., material references, slot references) can be set
using a tracked slot name. Pass `"null"` to clear a reference. Reading a SyncRef returns
`{"refId": "...", "type": "...", "name": "..."}` or `null`.

### setFields
Set multiple fields on a component in a single call. Much more efficient than separate `setField` calls.
```json
{"id": "sf1", "action": "setFields", "params": {
  "slot": "MatSlot",
  "component": "UnlitMaterial",
  "fields": {
    "TintColor": [1, 0, 0, 1],
    "BlendMode": "Alpha",
    "Texture": "MyTexSlot"
  }
}}
```
Returns `set` (array of successfully set field names), `setCount`, `totalRequested`, and `errors` (if any).
Each field is set independently — one failure doesn't prevent others from being set.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.

### createDynVarSpace
```json
{"id": "d1", "action": "createDynVarSpace", "params": {
  "slot": "MySlot",
  "spaceName": "WikiNavigator"
}}
```

### createDynVar
```json
{"id": "d2", "action": "createDynVar", "params": {
  "slot": "DataStore",
  "varName": "WikiNavigator/SearchQuery",
  "varType": "string",
  "value": ""
}}
```
- Supported types: `string`, `bool`, `int`, `float`, `float3`, `colorX`

### readDynVar
Reads a dynamic variable value by its full path from any slot within the space hierarchy.
```json
{"id": "dr1", "action": "readDynVar", "params": {
  "slot": "MySlot",
  "path": "WikiNavigator/SearchQuery",
  "type": "string"
}}
```
- **slot**: Any tracked slot within or below the DynamicVariableSpace.
- **path**: Full path including space name (e.g., `"SpaceName/VarName"`).
- **type**: `string` (default), `bool`, `int`, `float`, `float3`, `colorX`.

### writeDynVar
Writes a dynamic variable value by path. The variable must already exist.
```json
{"id": "dw1", "action": "writeDynVar", "params": {
  "slot": "MySlot",
  "path": "WikiNavigator/SearchQuery",
  "type": "string",
  "value": "Hello World"
}}
```
- Same type support as `readDynVar`.
- Uses `DynamicVariableHelper.WriteDynamicVariable` which finds the space automatically.

### getSlotInfo
```json
{"id": "q1", "action": "getSlotInfo", "params": {"slot": "MySlot"}}
```
Returns: name, refId, active, tag, parent (name and refId), childCount, children array, components array.

### getComponentField
Reads a field value back from a component. Essential for verifying that writes took effect.
```json
{"id": "r1", "action": "getComponentField", "params": {
  "slot": "MyPanel",
  "component": "Image",
  "field": "Tint"
}}
```
Returns: slot, component, field, value (in the same format as setField), fieldType.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.

Example response:
```json
{"id": "r1", "status": "ok", "slot": "MyPanel", "component": "Image",
 "field": "Tint", "value": [0.14, 0.14, 0.18, 1.0], "fieldType": "Sync`1"}
```

Supports all readable types: string, bool, int, float, float2, float3, float4, floatQ, colorX, Uri, enums, and SyncRef.
Unsupported field types return `"<unsupported:TypeName>"` as the value instead of erroring.

### getComponentFields
Lists **all** sync members on a component with their names, types, and current values.
```json
{"id": "gf2", "action": "getComponentFields", "params": {
  "slot": "MySlot",
  "component": "UnlitMaterial"
}}
```
Returns `fieldCount` and a `fields` array of `{"name": "...", "type": "...", "value": ...}`.
Useful for discovering what fields a component has without prior knowledge.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.

### removeComponent
Removes (destroys) a component from a slot. Reverses `attachComponent`.
```json
{"id": "rc1", "action": "removeComponent", "params": {
  "slot": "MyPanel",
  "type": "Image"
}}
```
Removes the first component of the given type. If the slot has multiple components of the same type, only the first is removed.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.

### reparentSlot
Moves a slot under a different parent.
```json
{"id": "rp1", "action": "reparentSlot", "params": {
  "slot": "MyPanel",
  "newParent": "ContainerSlot",
  "preserveGlobalTransform": false
}}
```
- **newParent**: Any tracked slot name, or `__root__`, `__worldroot__`, `__localuser__`.
- **preserveGlobalTransform**: Optional (default `false`). When `true`, adjusts local transform to maintain the same world-space position/rotation/scale.

### setSlotName
Renames a slot and optionally updates its tracker key.
```json
{"id": "sn1", "action": "setSlotName", "params": {
  "slot": "OldName",
  "newName": "BetterName",
  "updateTracker": true
}}
```
- **updateTracker**: Optional (default `true`). When `true`, re-registers the slot under the new name in the tracker so future commands use the new name.
- When `false`, the slot is renamed in-engine but the tracker still uses the old key.

### setSlotTag
Sets a slot's tag. Tags are used for categorization and can be searched with `findSlot`.
```json
{"id": "st1", "action": "setSlotTag", "params": {
  "slot": "MyPanel",
  "tag": "ui-panel"
}}
```
- Pass empty string `""` to clear the tag.

### setSlotOrderIndex
Sets a slot's ordering index among its siblings. Essential for controlling UIX element ordering.
```json
{"id": "so1", "action": "setSlotOrderIndex", "params": {
  "slot": "MyButton",
  "index": 0
}}
```
- Index `0` = first child, `-1` or high values = last. Response returns actual resulting index.

### findSlot
Searches the scene graph for a slot by name and/or tag. The found slot is automatically tracked.
```json
{"id": "fs1", "action": "findSlot", "params": {
  "name": "WikiPanel",
  "searchRoot": "__root__",
  "trackAs": "FoundPanel",
  "matchSubstring": false,
  "ignoreCase": true,
  "maxDepth": -1
}}
```
- **name**: Search by name (uses `FindChild` with substring/case options).
- **tag**: Search by tag (uses `GetChildrenWithTag`). Can combine with `name`.
- **searchRoot**: Optional (default `__root__`). Any tracked slot name or special root.
- **trackAs**: Optional. Name to register the found slot under in the tracker. Defaults to the slot's own name.
- **matchSubstring**: Optional (default `false`). Match partial names.
- **ignoreCase**: Optional (default `true`). Case-insensitive name matching.
- **maxDepth**: Optional (default `-1` = unlimited). Maximum hierarchy depth to search.

### duplicateSlot
Creates a deep copy of a slot and its entire hierarchy.
```json
{"id": "ds1", "action": "duplicateSlot", "params": {
  "slot": "TemplateWidget",
  "trackAs": "Widget_Copy1",
  "keepGlobalTransform": true
}}
```
- **trackAs**: Optional. Name for the duplicate in the tracker. Defaults to `"{originalName}_copy"`.
- **keepGlobalTransform**: Optional (default `true`). Preserve world-space position/rotation/scale.

### importTexture
High-level command that creates a `StaticTexture2D` from a URL, optionally with a `SpriteProvider` for UIX use.
```json
{"id": "it1", "action": "importTexture", "params": {
  "url": "resdb:///abc123.webp",
  "parent": "AssetsSlot",
  "trackAs": "MyTexture",
  "createSprite": true
}}
```
- **url**: Required. The texture URL (resdb:///, https://, etc.)
- **parent**: Optional (default `__root__`). Parent slot for the texture asset slot.
- **trackAs**: Optional (default `"ImportedTexture"`). Name for the created slot in the tracker.
- **createSprite**: Optional (default `true`). Also creates a `SpriteProvider` wired to the texture.

Response includes `textureRefId` and `spriteRefId` for use with `setField` to wire into materials or UIX elements.

**Usage pattern — UIX Image with sprite**:
```json
{"commands": [
  {"id": "1", "action": "importTexture", "params": {"url": "resdb:///abc.webp", "trackAs": "MyTex", "createSprite": true}},
  {"id": "2", "action": "setField", "params": {"slot": "MyImageSlot", "component": "Image", "field": "Sprite", "value": "MyTex"}}
]}
```
The `setField` uses the tracked texture slot name. The bridge resolves it via `ISyncRef.TrySet()` to wire the SpriteProvider.

**Usage pattern — RawImage with direct texture**:
```json
{"commands": [
  {"id": "1", "action": "importTexture", "params": {"url": "resdb:///abc.webp", "trackAs": "MyTex", "createSprite": false}},
  {"id": "2", "action": "setField", "params": {"slot": "MyRawImageSlot", "component": "RawImage", "field": "Texture", "value": "MyTex"}}
]}
```

### importMesh
Creates a `StaticMesh` component from a URL, parallel to `importTexture`.
```json
{"id": "im1", "action": "importMesh", "params": {
  "url": "resdb:///abc123.glb",
  "parent": "AssetsSlot",
  "trackAs": "MyMesh"
}}
```

### createPrimitive
High-level command that creates a **complete visible 3D object** in one call (mesh + MeshRenderer + material, all wired).
```json
{"id": "cp1", "action": "createPrimitive", "params": {
  "name": "RedBox",
  "parent": "__root__",
  "meshType": "BoxMesh",
  "material": "PBS_Metallic",
  "color": [0.8, 0.2, 0.2, 1.0],
  "position": [0, 1.5, 0],
  "scale": [0.5, 0.5, 0.5]
}}
```
- **meshType**: Procedural mesh type (`BoxMesh`, `SphereMesh`, `CylinderMesh`, `ConeMesh`, `QuadMesh`). Default: `BoxMesh`.
- **meshUrl**: Alternative to `meshType` — loads a `StaticMesh` from URL instead.
- **material**: Material type (`PBS_Metallic`, `PBS_Specular`, `UnlitMaterial`). Default: `PBS_Metallic`.
- **color**: Optional `[r, g, b, a]` — sets `AlbedoColor` (PBS) or `TintColor` (Unlit).
- **position**, **rotation**, **scale**: Optional transform.
- Response includes `meshRefId`, `rendererRefId`, `materialRefId` for further customization via `setField`.

### makePhysicsObject
High-level command that attaches a collider, `CharacterController`, and optionally a `Grabbable` to an existing slot in one call.
```json
{"id": "mp1", "action": "makePhysicsObject", "params": {
  "slot": "MyCube",
  "collider": "box",
  "grabbable": true,
  "mass": 1.0
}}
```
- **slot**: Required. The tracked slot to add physics to.
- **collider**: Optional (default `"box"`). One of `box`, `sphere`, `capsule`, `mesh`.
- **grabbable**: Optional (default `true`). When `true`, also attaches a `Grabbable` component.
- **mass**: Optional (default `1.0`). Passed to the CharacterController.
- Response includes `colliderRefId` and `characterControllerRefId`.

**Usage pattern — create a grabbable physics cube:**
```json
{"commands": [
  {"id": "1", "action": "createPrimitive", "params": {"name": "PhysCube", "meshType": "BoxMesh", "color": [0.6, 0.3, 0.1, 1.0]}},
  {"id": "2", "action": "makePhysicsObject", "params": {"slot": "PhysCube", "collider": "box"}}
]}
```

### importAudio
High-level command that creates a complete audio pipeline from a URL: `StaticAudioClip` → `AudioClipPlayer` → `AudioOutput`, all wired together.
```json
{"id": "ia1", "action": "importAudio", "params": {
  "url": "resdb:///abc123.ogg",
  "parent": "AssetsSlot",
  "trackAs": "BGMusic",
  "spatial": true
}}
```
- **url**: Required. The audio file URL (resdb:///, https://, etc.)
- **parent**: Optional (default `__root__`). Parent slot for the audio source slot.
- **trackAs**: Optional (default `"AudioSource"`). Name for the created slot in the tracker.
- **spatial**: Optional (default `true`). When `true`, sets `SpatialBlend` to `1.0` (3D audio); when `false`, sets to `0.0` (2D/global).
- Response includes `audioClipRefId`, `playerRefId`, and `outputRefId` for further customization.

**Usage pattern — background music (non-spatial):**
```json
{"id": "1", "action": "importAudio", "params": {
  "url": "resdb:///music.ogg",
  "trackAs": "BackgroundMusic",
  "spatial": false
}}
```

### clearTracker
```json
{"id": "x1", "action": "clearTracker", "params": {}}
```
Clears all name→slot mappings. Does NOT destroy any slots.

### trackExistingSlot
Finds an existing in-world slot by navigating a hierarchy path and registers it in the tracker. Essential for working with slots you didn't create via the bridge.
```json
{"id": "te1", "action": "trackExistingSlot", "params": {
  "path": "Assets/Materials/MainMat",
  "from": "__root__",
  "trackAs": "MyMaterial"
}}
```
- **path**: Slash-separated path of child names (e.g., `"Panel/Header/Title"`).
- **from**: Starting slot (default `__root__`). Can be any tracked slot.
- **trackAs**: Name to register in tracker (default: the found slot's actual name).
- Returns the slot's details (name, refId, active, childCount, componentCount).

### buildUIXTree
Build an **entire UI hierarchy** from a single declarative JSON tree. Each node can have name, tag, transform, components with fields, and children.
```json
{"id": "bt1", "action": "buildUIXTree", "params": {
  "parent": "__root__",
  "root": {
    "name": "WikiPanel", "tag": "ui", "scale": [0.001],
    "components": [
      {"type": "Canvas", "fields": {"Size": [800, 600]}}
    ],
    "children": [
      {
        "name": "Header",
        "components": [
          {"type": "Image", "fields": {"Tint": [0.12, 0.12, 0.18, 1.0]}},
          {"type": "LayoutElement", "fields": {"PreferredHeight": 60}}
        ],
        "children": [
          {"name": "Title", "components": [
            {"type": "Text", "fields": {"Content": "Wiki Browser", "Size": 28}}
          ]}
        ]
      },
      {
        "name": "Body",
        "components": [
          {"type": "VerticalLayout"},
          {"type": "Image", "fields": {"Tint": [0.08, 0.08, 0.12, 0.9]}}
        ]
      }
    ]
  }
}}
```
- Every slot is auto-tracked by its `name`.
- Returns `slotsCreated` count, per-slot summary with RefIDs, and any `errorDetails`.
- Component/field errors don't halt the tree build — they're collected and reported.
- **This replaces what would be 15+ individual commands with a single HTTP call.**

### getSlotsByTag
Finds **all** descendant slots matching a tag. Uses the verified `Slot.GetChildrenWithTag()` API.
```json
{"id": "gt1", "action": "getSlotsByTag", "params": {
  "slot": "__root__",
  "tag": "nav-button",
  "trackAll": true
}}
```
Returns `count` and `slots` array. With `trackAll: true`, every found slot is registered in the tracker for immediate use with `setField`, `setSlotActive`, etc.

### log
Writes a message to the Resonite console log. Useful for debugging bridge operations.
```json
{"id": "lg1", "action": "log", "params": {"message": "Building UI...", "level": "info"}}
```
- **level**: `info` (default), `warn`, or `error`.

### getWorldInfo
Returns information about the current focused world/session.
```json
{"id": "wi1", "action": "getWorldInfo", "params": {}}
```
Returns: `worldName`, `sessionId`, `userCount`, `hostUser`, `privacy`, `uptime`.

### getUserInfo
Returns information about the local user (position, rotation, head position).
```json
{"id": "ui1", "action": "getUserInfo", "params": {}}
```
- Automatically tracks `__localuser__` for slot resolution.
- Returns: `name`, `userId`, `position`, `rotation`, `headPosition`.

### getUsers
Lists all users currently in the session.
```json
{"id": "gu1", "action": "getUsers", "params": {}}
```
Returns an array of users, each with: `name`, `position`, `isHost`, `isLocal`.

### findComponents
Searches the hierarchy for slots containing a specific component type.
```json
{"id": "fc1", "action": "findComponents", "params": {
  "type": "Image",
  "slot": "__root__",
  "maxDepth": 5,
  "trackMatches": true
}}
```
- **type**: Required. Component type name (short name or full FrooxEngine type).
- **slot**: Optional (default `__root__`). Root slot to search from.
- **maxDepth**: Optional (default `-1` = unlimited). Maximum hierarchy depth to search.
- **trackMatches**: Optional (default `false`). When `true`, registers each matched slot in the tracker.
- Returns `count` and `matches` array with slot name, refId, and component details.

### getRegisteredComponents
Returns a categorized list of all registered component shortcuts with their full FrooxEngine type names.
```json
{"id": "rc2", "action": "getRegisteredComponents", "params": {}}
```
Returns categories (e.g., "UIX Core", "Materials") each containing an array of `{"shortName": "...", "fullType": "..."}` entries.

### createProtoFluxNode
Creates a ProtoFlux node on a slot. ProtoFlux is Resonite's visual programming system.
```json
{"id": "pf1", "action": "createProtoFluxNode", "params": {
  "slot": "LogicSlot",
  "nodeType": "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.If",
  "trackAs": "IfNode"
}}
```
- **slot**: Target slot to attach the node to.
- **nodeType**: Full FrooxEngine type name of the ProtoFlux node.
- **trackAs**: Optional. Name to register the node's slot under in the tracker.

### connectProtoFlux
Wires an output of one ProtoFlux node to an input of another.
```json
{"id": "pf2", "action": "connectProtoFlux", "params": {
  "sourceSlot": "BoolSource",
  "sourceOutput": "Output",
  "targetSlot": "IfNode",
  "targetInput": "Condition",
  "sourceComponent": 0,
  "targetComponent": 0
}}
```
- **sourceSlot**: Slot containing the source ProtoFlux node.
- **sourceOutput**: Name of the output field on the source node.
- **targetSlot**: Slot containing the target ProtoFlux node.
- **targetInput**: Name of the input field on the target node.
- **sourceComponent**: Optional (default `0`). Component index on the source slot.
- **targetComponent**: Optional (default `0`). Component index on the target slot.

### setProtoFluxInput
Sets a constant/literal value on a ProtoFlux node input.
```json
{"id": "pf3", "action": "setProtoFluxInput", "params": {
  "slot": "ConstantNode",
  "field": "Value",
  "value": 42,
  "component": 0
}}
```
- **slot**: Slot containing the ProtoFlux node.
- **field**: Name of the input field to set.
- **value**: The value to assign (uses the same type formats as `setField`).
- **component**: Optional (default `0`). Component index when multiple nodes exist on one slot.

### getProtoFluxNode
Inspects a ProtoFlux node's current state — inputs, outputs, and impulses.
```json
{"id": "pf4", "action": "getProtoFluxNode", "params": {
  "slot": "IfNode",
  "component": 0
}}
```
- **slot**: Slot containing the ProtoFlux node.
- **component**: Optional (default `0`). Component index when multiple nodes exist on one slot.
- Returns the node's type, inputs (names, types, current values), outputs, and impulses.

### subscribe
Creates an event subscription. Events are delivered as JSON messages over the WebSocket connection at `/ws`.
```json
{"id": "ev1", "action": "subscribe", "params": {
  "eventType": "fieldChanged",
  "slot": "MyPanel",
  "component": "Text",
  "field": "Content"
}}
```
- **eventType**: Required. One of: `fieldChanged`, `slotChildrenChanged`, `slotDestroyed`, `userJoin`, `userLeave`.
- **slot**: Required for `fieldChanged`, `slotChildrenChanged`, `slotDestroyed`.
- **component**: Required for `fieldChanged`. Component type on the slot.
- **field**: Required for `fieldChanged`. Field name to monitor.
- Returns `subscriptionId` for use with `unsubscribe`.

**Example — subscribe to user joins:**
```json
{"id": "ev2", "action": "subscribe", "params": {
  "eventType": "userJoin"
}}
```

### unsubscribe
Removes an event subscription by ID, or removes all subscriptions.
```json
{"id": "ev3", "action": "unsubscribe", "params": {
  "subscriptionId": "sub_abc123"
}}
```
- **subscriptionId**: The ID returned by `subscribe`.
- **all**: When `true`, removes all active subscriptions (ignores `subscriptionId`).

**Example — remove all subscriptions:**
```json
{"id": "ev4", "action": "unsubscribe", "params": {"all": true}}
```

### listSubscriptions
Lists all active event subscriptions.
```json
{"id": "ev5", "action": "listSubscriptions", "params": {}}
```
Returns `count` and `subscriptions` array. Example response:
```json
{"id": "ev5", "status": "ok", "count": 2, "subscriptions": [
  {"subscriptionId": "sub_abc123", "eventType": "fieldChanged", "slot": "MyPanel", "component": "Text", "field": "Content"},
  {"subscriptionId": "sub_def456", "eventType": "userJoin"}
]}
```

### snapshotSlot
Serializes a slot hierarchy to JSON, capturing names, transforms, components, and field values.
```json
{"id": "ss1", "action": "snapshotSlot", "params": {
  "slot": "MyPanel",
  "maxDepth": -1,
  "includeComponents": true
}}
```
- **slot**: Required. The slot to snapshot.
- **maxDepth**: Optional (default `-1` = unlimited). Maximum depth of children to include.
- **includeComponents**: Optional (default `true`). When `false`, only captures slot names and transforms.
- Returns the full serialized tree as a JSON object.

### saveTemplate
Snapshots a slot hierarchy and saves it as a named template for later reuse.
```json
{"id": "st1", "action": "saveTemplate", "params": {
  "slot": "WikiPanel",
  "templateName": "wiki_panel_v1",
  "maxDepth": -1
}}
```
- **slot**: Required. The slot to snapshot.
- **templateName**: Required. Name to store the template under.
- **maxDepth**: Optional (default `-1` = unlimited). Maximum child depth to capture.
- Overwrites any existing template with the same name.

### stampTemplate
Instantiates a previously saved template as new slots under a parent.
```json
{"id": "sp1", "action": "stampTemplate", "params": {
  "templateName": "wiki_panel_v1",
  "slot": "__root__",
  "trackAs": "WikiPanel_Copy"
}}
```
- **templateName**: Required. Name of a saved template.
- **slot**: Required. Parent slot to stamp the template under.
- **trackAs**: Optional. Name to register the stamped root slot under in the tracker. Defaults to the template's root slot name.
- Creates a full hierarchy with all components and field values from the template.

### listTemplates
Lists all saved template names.
```json
{"id": "lt1", "action": "listTemplates", "params": {}}
```
Example response:
```json
{"id": "lt1", "status": "ok", "count": 2, "templates": [
  "wiki_panel_v1",
  "settings_dialog"
]}
```

### deleteTemplate
Deletes a saved template by name.
```json
{"id": "dt1", "action": "deleteTemplate", "params": {
  "templateName": "wiki_panel_v1"
}}
```

### Template Workflow — Undo Pattern
Snapshot a slot before making destructive changes. If something goes wrong, the snapshot tells you exactly what was there so you can rebuild it:
```json
{"commands": [
  {"id": "1", "action": "saveTemplate", "params": {"slot": "Header", "templateName": "header_backup"}},
  {"id": "2", "action": "destroyChildren", "params": {"slot": "Header"}},
  {"id": "3", "action": "stampTemplate", "params": {"templateName": "header_backup", "slot": "Header"}}
]}
```
Step 1 saves the current state. Step 2 clears the slot. Step 3 restores it from the template. In practice, you'd only run step 3 if you need to roll back.

## Reference Wiring

### wireReference
Wires any `ISyncRef` field on a component to a target world element by RefID. Supports dotted field paths for nested/indexed access.
```json
{"id": "wr1", "action": "wireReference", "params": {
  "slot": "MyMaterialSlot",
  "component": "PBS_Metallic",
  "field": "AlbedoTexture",
  "targetRefId": "S-12345",
  "componentIndex": 0
}}
```
- **slot**: Required. Slot containing the component.
- **component**: Required. Component type name.
- **field**: Required. Field name on the component. Supports dotted paths for nested access (e.g., `Materials._elements.0`).
- **targetRefId**: Required. The RefID of the target element to wire to. Falls back to tracker name lookup if not a valid RefID.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.

**Example — wire a material's texture to a StaticTexture2D:**
```json
{"id": "wr2", "action": "wireReference", "params": {
  "slot": "MatSlot",
  "component": "PBS_Metallic",
  "field": "AlbedoTexture",
  "targetRefId": "S-a1b2c3"
}}
```

### addToList
Appends an item to a `SyncList` field on a component. Useful for wiring materials, adding elements to lists, etc.
```json
{"id": "al1", "action": "addToList", "params": {
  "slot": "MyRendererSlot",
  "component": "MeshRenderer",
  "field": "Materials",
  "targetRefId": "S-mat456",
  "componentIndex": 0
}}
```
- **slot**: Required. Slot containing the component.
- **component**: Required. Component type name.
- **field**: Required. The `SyncList` field name (e.g., `Materials`).
- **targetRefId**: Optional. RefID of an element to add as a reference.
- **value**: Optional. A literal value to add instead of a reference.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.

**Example — add a material to a MeshRenderer:**
```json
{"id": "al2", "action": "addToList", "params": {
  "slot": "CubeSlot",
  "component": "MeshRenderer",
  "field": "Materials",
  "targetRefId": "S-mat789"
}}
```

### getComponentByRefId
Looks up any component or slot anywhere in the world by its RefID. Returns the type, all field names, and current values.
```json
{"id": "gr1", "action": "getComponentByRefId", "params": {
  "refId": "S-abc123"
}}
```
- **refId**: Required. The RefID of the component or slot to inspect.
- Returns: `type`, `refId`, `fieldCount`, and a `fields` array of `{"name": "...", "type": "...", "value": ...}`.

**Example response:**
```json
{"id": "gr1", "status": "ok", "type": "PBS_Metallic", "refId": "S-abc123",
 "fieldCount": 12, "fields": [
   {"name": "AlbedoColor", "type": "colorX", "value": [1,1,1,1]},
   {"name": "AlbedoTexture", "type": "SyncRef", "value": null}
 ]}
```

### getAllComponents
Lists ALL components on a slot — not limited to registered shortcuts. Returns each component's type, RefID, and field names.
```json
{"id": "ga1", "action": "getAllComponents", "params": {
  "slot": "SomeSlot"
}}
```
- **slot**: Required. The slot to inspect.
- Returns: `componentCount` and a `components` array of `{"type": "...", "refId": "...", "fields": ["fieldName1", "fieldName2", ...]}`.

**Example response:**
```json
{"id": "ga1", "status": "ok", "slot": "SomeSlot", "componentCount": 3, "components": [
  {"type": "MeshRenderer", "refId": "S-111", "fields": ["Mesh", "Materials", "SortingOrder"]},
  {"type": "BoxMesh", "refId": "S-222", "fields": ["Size", "UVScale"]},
  {"type": "PBS_Metallic", "refId": "S-333", "fields": ["AlbedoColor", "AlbedoTexture", "Metallic"]}
]}
```

## Hierarchy Navigation

### findSlotByPath
Navigates to a slot using a slash-delimited path. Supports `..` (parent), `.` (current), case-insensitive matching, and substring fallback when an exact match isn't found.
```json
{"id": "fp1", "action": "findSlotByPath", "params": {
  "path": "Root/Panel/Header",
  "from": "__root__",
  "trackAs": "HeaderSlot"
}}
```
- **path**: Required. Slash-delimited path of child names (e.g., `"Panel/Header/Title"`). Supports `..` to go up to the parent and `.` for the current slot.
- **from**: Optional (default `__root__`). Starting slot — any tracked slot name or special root.
- **trackAs**: Optional. Name to register the found slot under in the tracker. Defaults to the slot's own name.
- Matching is case-insensitive. If no exact match is found for a path segment, falls back to substring matching.

### findSlots
Multi-result search that finds all slots matching the given criteria.
```json
{"id": "fs2", "action": "findSlots", "params": {
  "regex": "^Nav_Button_\\d+$",
  "searchRoot": "__root__",
  "maxDepth": -1,
  "maxResults": 50,
  "trackAll": true
}}
```
- **name**: Optional. Search by exact slot name.
- **tag**: Optional. Search by tag.
- **regex**: Optional. Search by regex pattern against slot names.
- **searchRoot**: Optional (default `__root__`). Root slot to search from.
- **maxDepth**: Optional (default `-1` = unlimited). Maximum hierarchy depth to search.
- **maxResults**: Optional (default `50`). Maximum number of results to return.
- **trackAll**: Optional (default `false`). When `true`, registers every found slot in the tracker.
- Returns `count` and `slots` array with name, refId, and path for each match.
- At least one of `name`, `tag`, or `regex` must be provided.

### getParent
Returns the parent slot of a given slot and tracks it.
```json
{"id": "gp1", "action": "getParent", "params": {
  "slot": "HeaderSlot",
  "trackAs": "ParentSlot"
}}
```
- **slot**: Required. The slot whose parent to retrieve.
- **trackAs**: Optional. Name to register the parent under in the tracker. Defaults to the parent's own name.
- Returns: parent slot's `name`, `refId`, `active`, `tag`, `childCount`, `componentCount`.

### getSlotHierarchy
Returns a nested tree view of a slot's children with component info and child counts.
```json
{"id": "gh1", "action": "getSlotHierarchy", "params": {
  "slot": "MyPanel",
  "maxDepth": 3
}}
```
- **slot**: Required. The root slot to inspect.
- **maxDepth**: Optional (default `3`). Maximum depth of the tree to return.
- Returns a nested JSON tree where each node includes: `name`, `refId`, `active`, `childCount`, `components` (type names), and `children` (nested array).
- When the tree is truncated at `maxDepth`, child nodes beyond the limit include a `truncated: true` marker with `remainingChildren` count.
- Useful for understanding the structure of an existing hierarchy before modifying it.

## Event System

Subscribe to events via `subscribe` (sent over `/cmd` or `/ws`). Event notifications are delivered as JSON messages on the WebSocket connection at `ws://localhost:9090/ws`.

**Event message format:**
```json
{"type": "event", "eventType": "...", "subscriptionId": "...", "data": {...}}
```

### Event Types

| Event Type | Required Params | Data Fields |
|---|---|---|
| `fieldChanged` | `slot`, `component`, `field` | `slot`, `component`, `field`, `oldValue`, `newValue`, `fieldType` |
| `slotChildrenChanged` | `slot` | `slot`, `addedChildren`, `removedChildren` |
| `slotDestroyed` | `slot` | `slot`, `slotName`, `refId` |
| `userJoin` | (none) | `userName`, `userId`, `isHost` |
| `userLeave` | (none) | `userName`, `userId` |

**Example — fieldChanged event message:**
```json
{"type": "event", "eventType": "fieldChanged", "subscriptionId": "sub_abc123", "data": {
  "slot": "MyPanel", "component": "Text", "field": "Content",
  "oldValue": "Hello", "newValue": "World", "fieldType": "string"
}}
```

**Example — userJoin event message:**
```json
{"type": "event", "eventType": "userJoin", "subscriptionId": "sub_def456", "data": {
  "userName": "SomeUser", "userId": "U-1234", "isHost": false
}}
```

## WebSocket

The `/ws` endpoint provides a persistent WebSocket connection for bidirectional streaming.

- **URL**: `ws://localhost:9090/ws`
- **Protocol**: Send JSON messages in the same format as `/cmd` (with `id`, `action`, `params`).
- **Responses**: Each command response is pushed back over the WebSocket as a JSON message.
- **Events**: Event subscription notifications are also delivered over this connection (see Event System above).
- **Advantages**: No per-request HTTP overhead, persistent connection, real-time feedback, event delivery.

Example session (pseudocode):
```
ws = connect("ws://localhost:9090/ws")
ws.send('{"id":"1","action":"ping","params":{}}')
response = ws.recv()  // {"id":"1","status":"ok","message":"pong",...}
ws.send('{"id":"2","action":"subscribe","params":{"eventType":"userJoin"}}')
response = ws.recv()  // {"id":"2","status":"ok","subscriptionId":"sub_abc"}
event = ws.recv()      // {"type":"event","eventType":"userJoin",...}
```

All commands available via `/cmd` are also available over the WebSocket.

## Registered Component Shortcuts (73)

These short names can be used with `attachComponent`, `createPrimitive`, `createSlot` (inline components), `buildUIXTree`, etc. instead of the full FrooxEngine type name.

| Category | Short Names |
|---|---|
| **UIX Core** | Canvas, Image, Text, Button, Mask, RawImage, TextField, Checkbox |
| **UIX Layout** | RectTransform, VerticalLayout, HorizontalLayout, GridLayout, LayoutElement, ContentSizeFitter, ScrollRect, IgnoreLayout |
| **UIX Controls** | Slider, ProgressBar |
| **Textures & Sprites** | StaticTexture2D, SpriteProvider |
| **Materials** | UnlitMaterial, PBS_Metallic, PBS_Specular, FresnelMaterial, XiexeToonMaterial, PBS_DualSidedMetallic |
| **Meshes & Rendering** | BoxMesh, QuadMesh, SphereMesh, CylinderMesh, ConeMesh, StaticMesh, MeshRenderer, SkinnedMeshRenderer, TextRenderer, TorusMesh, BevelBoxMesh, BevelPlaneMesh, BevelStripeMesh, TriangleMesh, CapsuleMesh, CircleMesh, CurvedPlaneMesh, IcoSphereMesh, GridMesh, TubeMesh, RingMesh |
| **Lighting** | Light, ReflectionProbe, Skybox, AmbientLightSH2 |
| **Colliders** | BoxCollider, SphereCollider, CapsuleCollider, MeshCollider |
| **Physics** | CharacterController |
| **Audio** | AudioClipPlayer, AudioOutput, StaticAudioClip, AudioListener |
| **Video** | VideoTextureProvider |
| **Interaction** | Grabbable, PhysicalButton, TouchButton, ContextMenuItemSource, InteractionHandler |
| **Animation / Motion** | Spinner, Wiggler, Panner1D, Panner2D, LinearMapper1D, LinearMapper2D, LinearMapper3D, LinearMapper4D |
| **Particles (PhotonDust)** | ParticleSystem, ParticleStyle, PointEmitter, ConeEmitter, BoxEmitter, SphereEmitter |
| **Dynamic Variables** | DynamicVariableSpace |
| **Utility** | SmoothTransform, Comment |

> Components not in this list can still be attached using their full FrooxEngine type name (e.g., `FrooxEngine.SomeComponent`).
> The resolver also searches `FrooxEngine.UIX.*` and `FrooxEngine.PhotonDust.*` namespaces automatically.

## Generic Type Resolution

The bridge supports attaching generic FrooxEngine components by passing the generic syntax directly as the type name. The resolver parses `BaseName<Arg1, Arg2>`, locates the open generic type in FrooxEngine, and constructs the closed type.

**Syntax**: `"type": "ComponentName<TypeArg>"` or `"type": "ComponentName<Arg1, Arg2>"`

**Examples:**
```json
{"id": "1", "action": "attachComponent", "params": {"slot": "MySlot", "type": "ValueGradientDriver<float>"}}
{"id": "2", "action": "attachComponent", "params": {"slot": "MySlot", "type": "Tween<colorX>"}}
{"id": "3", "action": "attachComponent", "params": {"slot": "MySlot", "type": "ValueCopy<float3>"}}
{"id": "4", "action": "attachComponent", "params": {"slot": "MySlot", "type": "ValueMultiDriver<string>"}}
```

**Supported type arguments**: `float`, `double`, `int`, `long`, `bool`, `string`, `byte`, `short`, `uint`, `float2`, `float3`, `float4`, `floatQ`, `colorX`, and any FrooxEngine type by name.

Generic types also work in `createSlot` inline components and `buildUIXTree` component definitions:
```json
{"id": "1", "action": "createSlot", "params": {
  "name": "DriverSlot",
  "components": [
    {"type": "ValueGradientDriver<float>", "fields": {}},
    {"type": "ValueCopy<float3>", "fields": {}}
  ]
}}
```

## Environment & Lighting

### setupEnvironment
One-call environment setup: configures skybox, ambient light, and reflection probe.
```json
{"id": "env1", "action": "setupEnvironment", "params": {
  "skyboxUrl": "resdb:///sunset_sky.hdr",
  "ambientColor": [0.4, 0.3, 0.2, 1.0],
  "parent": "__root__",
  "trackAs": "WorldEnvironment"
}}
```
- **skyboxUrl**: Optional. URL of a skybox texture (HDR, equirectangular, etc.).
- **ambientColor**: Optional. `[r, g, b, a]` color for ambient light.
- **parent**: Optional (default `__root__`). Parent slot for the environment setup.
- **trackAs**: Optional (default `"Environment"`). Name for the created slot in the tracker.
- Creates a slot with `Skybox`, `AmbientLightSH2`, and `ReflectionProbe` components, all configured.
- Response includes `skyboxRefId`, `ambientRefId`, `reflectionProbeRefId`.

### createLight
Creates a fully configured light source in one call.
```json
{"id": "lt1", "action": "createLight", "params": {
  "type": "directional",
  "color": [1.0, 0.95, 0.8, 1.0],
  "intensity": 1.5,
  "shadows": true,
  "position": [0, 5, 0],
  "rotation": [50, -30, 0],
  "parent": "__root__",
  "trackAs": "SunLight"
}}
```
- **type**: Required. One of `point`, `directional`, `spot`.
- **color**: Optional (default white). `[r, g, b, a]` light color.
- **intensity**: Optional (default `1.0`). Light intensity.
- **shadows**: Optional (default `true`). Enable shadow casting.
- **position**: Optional. `[x, y, z]` world position.
- **rotation**: Optional. `[x, y, z]` Euler rotation (important for directional/spot lights).
- **parent**: Optional (default `__root__`). Parent slot.
- **trackAs**: Optional (default `"Light"`). Tracker name.
- Response includes `lightRefId`.

## Particles

### createParticleSystem
Creates a complete PhotonDust particle system with emitter, style, and renderer in one call.
```json
{"id": "ps1", "action": "createParticleSystem", "params": {
  "emitterType": "cone",
  "color": [1.0, 0.5, 0.1, 1.0],
  "emissionRate": 50,
  "lifetime": 2.0,
  "speed": 1.5,
  "size": 0.05,
  "position": [0, 0.5, 0],
  "parent": "__root__",
  "trackAs": "FireParticles"
}}
```
- **emitterType**: Optional (default `"point"`). One of `point`, `cone`, `box`, `sphere`.
- **color**: Optional (default white). `[r, g, b, a]` particle color.
- **emissionRate**: Optional (default `20`). Particles emitted per second.
- **lifetime**: Optional (default `3.0`). Particle lifetime in seconds.
- **speed**: Optional (default `1.0`). Initial particle speed.
- **size**: Optional (default `0.02`). Particle size.
- **position**: Optional. `[x, y, z]` world position of the emitter.
- **parent**: Optional (default `__root__`). Parent slot.
- **trackAs**: Optional (default `"ParticleSystem"`). Tracker name.
- Creates: `ParticleSystem`, emitter (`PointEmitter`/`ConeEmitter`/`BoxEmitter`/`SphereEmitter`), `ParticleStyle`, and a renderer, all wired together.
- Response includes `particleSystemRefId`, `emitterRefId`, `styleRefId`.

## Animation

### createAnimation
Creates a `ValueGradientDriver` with JSON keyframes and wires it to a target field.
```json
{"id": "an1", "action": "createAnimation", "params": {
  "targetSlot": "MyLight",
  "targetComponent": "Light",
  "targetField": "Intensity",
  "valueType": "float",
  "keyframes": [
    {"time": 0.0, "value": 0.5},
    {"time": 0.5, "value": 2.0},
    {"time": 1.0, "value": 0.5}
  ],
  "duration": 2.0,
  "parent": "__root__",
  "trackAs": "LightPulse"
}}
```
- **targetSlot**: Required. Slot containing the component to animate.
- **targetComponent**: Required. Component type on the target slot.
- **targetField**: Required. Field name to drive.
- **valueType**: Required. Type of the animated value (e.g., `float`, `colorX`, `float3`).
- **keyframes**: Required. Array of `{"time": 0.0-1.0, "value": ...}` entries. Time is normalized (0–1).
- **duration**: Optional (default `1.0`). Duration of one animation cycle in seconds.
- **parent**: Optional (default `__root__`). Parent slot for the driver.
- **trackAs**: Optional (default `"Animation"`). Tracker name.
- Creates a `ValueGradientDriver` component with the specified keyframes and wires its output to the target field.
- Response includes `driverRefId`.

## Video & Media

### importVideo
Creates a complete video playback setup: `VideoTextureProvider` + display quad + material, all auto-wired.
```json
{"id": "iv1", "action": "importVideo", "params": {
  "url": "resdb:///abc123.webm",
  "parent": "__root__",
  "trackAs": "VideoPlayer",
  "position": [0, 1.5, 0],
  "scale": [1.6, 0.9, 1.0]
}}
```
- **url**: Required. The video file URL (resdb:///, https://, etc.)
- **parent**: Optional (default `__root__`). Parent slot.
- **trackAs**: Optional (default `"VideoPlayer"`). Tracker name.
- **position**: Optional. `[x, y, z]` world position for the display quad.
- **scale**: Optional. `[x, y, z]` scale for the display quad (use to set aspect ratio).
- Creates a `VideoTextureProvider`, a `QuadMesh` display surface, a `MeshRenderer`, and a material with the video texture auto-wired.
- Response includes `videoProviderRefId`, `rendererRefId`, `materialRefId`.

## Component Utilities

### copyComponent
Duplicates a component from one slot to another, copying all field values.
```json
{"id": "cc1", "action": "copyComponent", "params": {
  "sourceSlot": "TemplateSlot",
  "targetSlot": "NewSlot",
  "type": "PBS_Metallic",
  "componentIndex": 0
}}
```
- **sourceSlot**: Required. Slot containing the component to copy.
- **targetSlot**: Required. Slot to copy the component to.
- **type**: Required. Component type name.
- **componentIndex**: Optional (default `0`). Index of the source component when multiples exist.
- Response includes `newComponentRefId`.

### removeFromList
Removes an item from a `SyncList` field by index. The inverse of `addToList`.
```json
{"id": "rl1", "action": "removeFromList", "params": {
  "slot": "MyRendererSlot",
  "component": "MeshRenderer",
  "field": "Materials",
  "index": 0,
  "componentIndex": 0
}}
```
- **slot**: Required. Slot containing the component.
- **component**: Required. Component type name.
- **field**: Required. The `SyncList` field name (e.g., `Materials`).
- **index**: Required. 0-based index of the item to remove.
- **componentIndex**: Optional (default `0`). Target a specific component instance when multiples exist.
- Returns `removedIndex` and the updated `listCount`.

## Persistence

### setSlotPersist
Sets whether a slot persists when the world is saved and reloaded.
```json
{"id": "sp1", "action": "setSlotPersist", "params": {
  "slot": "MyPanel",
  "persistent": true
}}
```
- **slot**: Required. The slot to modify.
- **persistent**: Required. `true` to persist across sessions, `false` to make transient.
- Returns the slot's `name`, `refId`, and resulting `persistent` state.

## Materials

### createMaterial
Creates and configures a PBR material, optionally auto-wiring it to a renderer on another slot.
```json
{"id": "cm1", "action": "createMaterial", "params": {
  "slot": "MatSlot",
  "materialType": "PBS_Metallic",
  "color": [0.8, 0.2, 0.2, 1.0],
  "metallic": 0.9,
  "smoothness": 0.7,
  "rendererSlot": "MyCube",
  "trackAs": "RedMetal"
}}
```
- **slot**: Required. Slot to attach the material component to.
- **materialType**: Optional (default `"PBS_Metallic"`). Material type to create (e.g., `PBS_Metallic`, `PBS_Specular`, `UnlitMaterial`).
- **color**: Optional. `[r, g, b, a]` — sets `AlbedoColor` (PBS) or `TintColor` (Unlit).
- **metallic**: Optional. Metallic value (0.0–1.0). Only applies to PBS materials.
- **smoothness**: Optional. Smoothness value (0.0–1.0). Only applies to PBS materials.
- **rendererSlot**: Optional. If provided, auto-wires the created material to the `MeshRenderer` on this slot.
- **trackAs**: Optional. Name to register the material slot under in the tracker.
- Response includes `materialRefId`.

**Usage pattern — create a material and wire it to a primitive:**
```json
{"commands": [
  {"id": "1", "action": "createPrimitive", "params": {"name": "Cube", "meshType": "BoxMesh"}},
  {"id": "2", "action": "createMaterial", "params": {
    "slot": "Cube", "color": [0.1, 0.5, 0.9, 1.0], "metallic": 0.8, "smoothness": 0.6,
    "rendererSlot": "Cube", "trackAs": "CubeMat"
  }}
]}
```

## 3D Text

### create3DText
Creates a complete 3D text object with `TextRenderer` and `UnlitMaterial`, all wired together.
```json
{"id": "ct1", "action": "create3DText", "params": {
  "parent": "__root__",
  "text": "Hello World",
  "fontSize": 64,
  "color": [1.0, 1.0, 1.0, 1.0],
  "position": [0, 2, 0],
  "horizontalAlign": "Center",
  "trackAs": "TitleText"
}}
```
- **parent**: Optional (default `__root__`). Parent slot for the text object.
- **text**: Optional (default `""`). The text content to display.
- **trackAs**: Optional. Name to register the created slot under in the tracker.
- **fontSize**: Optional. Font size for the `TextRenderer`.
- **color**: Optional. `[r, g, b, a]` text color, applied to the `UnlitMaterial`.
- **position**: Optional. `[x, y, z]` world position.
- **horizontalAlign**: Optional. Horizontal text alignment (e.g., `"Left"`, `"Center"`, `"Right"`).
- Creates a slot with `TextRenderer` + `UnlitMaterial`, wired together.
- Response includes `textRendererRefId`, `materialRefId`.

**Usage pattern — labeled objects:**
```json
{"commands": [
  {"id": "1", "action": "createPrimitive", "params": {"name": "Pedestal", "meshType": "CylinderMesh", "position": [0, 0.5, 0]}},
  {"id": "2", "action": "create3DText", "params": {
    "parent": "__root__", "text": "Exhibit A", "fontSize": 48,
    "color": [1, 1, 1, 1], "position": [0, 1.2, 0],
    "horizontalAlign": "Center", "trackAs": "Label_A"
  }}
]}
```

## Measurement

### measureDistance
Measures the world-space distance between two tracked slots.
```json
{"id": "md1", "action": "measureDistance", "params": {
  "slotA": "PointA",
  "slotB": "PointB"
}}
```
- **slotA**: Required. First tracked slot name or RefID.
- **slotB**: Required. Second tracked slot name or RefID.
- Returns: `distance` (float), `positionA` (`[x, y, z]`), `positionB` (`[x, y, z]`), and `delta` (`[dx, dy, dz]` vector from A to B).

**Example response:**
```json
{"id": "md1", "status": "ok", "distance": 3.162,
 "positionA": [0, 1, 0], "positionB": [1, 2, 3],
 "delta": [1, 1, 3]}
```

## Bulk Operations

### setFieldOnChildren
Sets a field value on all matching components across a slot's descendant hierarchy.
```json
{"id": "sfc1", "action": "setFieldOnChildren", "params": {
  "slot": "MyPanel",
  "component": "Image",
  "field": "Tint",
  "value": [0.2, 0.2, 0.3, 1.0],
  "maxDepth": 5
}}
```
- **slot**: Required. Root slot to search from.
- **component**: Required. Component type name to match.
- **field**: Required. Field name to set on each matching component.
- **value**: Required. The value to assign (uses the same type formats as `setField`).
- **maxDepth**: Optional (default `-1` = unlimited). Maximum hierarchy depth to search.
- Returns: `matchCount` (number of components updated) and `errors` (if any individual sets failed).

**Usage pattern — bulk-update all Image tints in a UI panel:**
```json
{"id": "1", "action": "setFieldOnChildren", "params": {
  "slot": "WikiPanel", "component": "Image", "field": "Tint",
  "value": [0.1, 0.1, 0.15, 1.0]
}}
```

### duplicateSlotArray
Creates N copies of a slot with uniform spacing, all automatically tracked.
```json
{"id": "da1", "action": "duplicateSlotArray", "params": {
  "slot": "TemplateColumn",
  "count": 5,
  "spacing": [2.0, 0, 0],
  "trackPrefix": "Column"
}}
```
- **slot**: Required. The slot to duplicate.
- **count**: Optional (default `1`). Number of copies to create.
- **spacing**: Optional. `[x, y, z]` offset between each copy. Applied cumulatively (copy 1 gets `spacing * 1`, copy 2 gets `spacing * 2`, etc.).
- **trackPrefix**: Optional. Prefix for tracker names. Copies are registered as `"{prefix}_0"`, `"{prefix}_1"`, etc. Defaults to `"{slotName}_copy"`.
- Returns: `created` (count) and `slots` array with name, refId, and position for each copy.

**Usage pattern — create a row of pillars:**
```json
{"commands": [
  {"id": "1", "action": "createPrimitive", "params": {
    "name": "Pillar", "meshType": "CylinderMesh",
    "scale": [0.3, 2, 0.3], "position": [0, 1, 0]
  }},
  {"id": "2", "action": "duplicateSlotArray", "params": {
    "slot": "Pillar", "count": 4, "spacing": [1.5, 0, 0], "trackPrefix": "Pillar"
  }}
]}
```

## User Control

### moveUser
Teleports the local user to specific world coordinates or to the position of a tracked slot.
```json
{"id": "mu1", "action": "moveUser", "params": {
  "position": [0, 0, 5],
  "rotation": [0, 180, 0]
}}
```
- **position**: Optional. `[x, y, z]` target world position.
- **rotation**: Optional. `[x, y, z]` Euler rotation or `[x, y, z, w]` quaternion.
- **targetSlot**: Optional. Tracked slot name or RefID — teleports the user to this slot's world position. Overrides `position` if both are provided.
- At least one of `position` or `targetSlot` must be provided.

**Example — teleport to a slot:**
```json
{"id": "mu2", "action": "moveUser", "params": {
  "targetSlot": "ViewingPlatform"
}}
```

## Practical Usage Examples

### Creating a Sunset Scene
Set up an environment with warm lighting and a skybox:
```json
{"commands": [
  {"id": "1", "action": "setupEnvironment", "params": {
    "skyboxUrl": "resdb:///sunset_panorama.hdr",
    "ambientColor": [0.4, 0.25, 0.15, 1.0],
    "trackAs": "SunsetEnv"
  }},
  {"id": "2", "action": "createLight", "params": {
    "type": "directional",
    "color": [1.0, 0.7, 0.3, 1.0],
    "intensity": 2.0,
    "shadows": true,
    "rotation": [45, -30, 0],
    "trackAs": "SunsetSun"
  }},
  {"id": "3", "action": "createLight", "params": {
    "type": "point",
    "color": [1.0, 0.4, 0.1, 1.0],
    "intensity": 0.8,
    "position": [2, 1, -1],
    "trackAs": "WarmFill"
  }}
]}
```

### Adding Fire Particles
Create a fire effect with a cone emitter:
```json
{"commands": [
  {"id": "1", "action": "createPrimitive", "params": {
    "name": "FirePit", "meshType": "CylinderMesh",
    "color": [0.3, 0.15, 0.05, 1.0],
    "position": [0, 0.15, 0], "scale": [0.5, 0.3, 0.5]
  }},
  {"id": "2", "action": "createParticleSystem", "params": {
    "emitterType": "cone",
    "color": [1.0, 0.4, 0.05, 1.0],
    "emissionRate": 80,
    "lifetime": 1.5,
    "speed": 0.8,
    "size": 0.04,
    "position": [0, 0.3, 0],
    "trackAs": "FireEffect"
  }}
]}
```

### Animating a Light Pulse
Make a light pulse between dim and bright in a 2-second loop:
```json
{"commands": [
  {"id": "1", "action": "createLight", "params": {
    "type": "point",
    "color": [0.2, 0.6, 1.0, 1.0],
    "intensity": 1.0,
    "position": [0, 2, 0],
    "trackAs": "PulseLight"
  }},
  {"id": "2", "action": "createAnimation", "params": {
    "targetSlot": "PulseLight",
    "targetComponent": "Light",
    "targetField": "Intensity",
    "valueType": "float",
    "keyframes": [
      {"time": 0.0, "value": 0.3},
      {"time": 0.5, "value": 2.5},
      {"time": 1.0, "value": 0.3}
    ],
    "duration": 2.0,
    "trackAs": "PulseAnimation"
  }}
]}
```

## Error Handling
- Errors return `{"id": "...", "status": "error", "error": "descriptive message"}`
- Batch with mixed results returns `"status": "partial"`
- Batch halted by `stopOnError` returns `"status": "stopped"` with `stoppedAtIndex`
- Common errors: slot not found, component type not found, field not found, unsupported type

## Project Paths
- **Mod source**: `g:\Resonite\AntigravityBridgeMod\`
- **Build output**: `g:\Resonite\AntigravityBridgeMod\bin\Release\AntigravityBridge.dll`
- **Deploy target**: `E:\SteamLibrary\steamapps\common\Resonite\rml_mods\AntigravityBridge.dll`
- **Build scripts**: `g:\Resonite\WikiFacet\bridge\`

## Key Technical Notes
- Resonite runs on **.NET 10** (`net10.0` target framework)
- RML version: **5.0.1**
- FrooxEngine.dll is in the Resonite root directory (not a Managed subfolder)
- The mod DLL is **locked while Resonite is running** — must close Resonite to update the mod
- All FrooxEngine operations run via `RunSynchronously()` for thread safety
- HttpListener runs on a background thread
