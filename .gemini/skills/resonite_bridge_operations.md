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
Returns: name, refId, active, tag, childCount, children array, components array.

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

### removeComponent
Removes (destroys) a component from a slot. Reverses `attachComponent`.
```json
{"id": "rc1", "action": "removeComponent", "params": {
  "slot": "MyPanel",
  "type": "Image"
}}
```
Removes the first component of the given type. If the slot has multiple components of the same type, only the first is removed.

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
