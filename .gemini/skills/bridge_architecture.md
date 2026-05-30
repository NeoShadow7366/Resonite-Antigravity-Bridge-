---
name: bridge-architecture
description: Architecture reference for the AntigravityBridge mod. Documents the code structure, file organization, data flow, threading model, and key design decisions. Use when refactoring, debugging, or understanding how the mod works.
---

# AntigravityBridge Architecture Guide

## File Overview

### Core Infrastructure

| File | Lines | Purpose |
|:---|:---:|:---|
| `AntigravityBridge.cs` | ~111 | Mod entry point. RML lifecycle, config keys, wiring systems together |
| `CommandRouter.cs` | ~651 | **Dispatcher** — routes commands to handlers, validation, batch execution |
| `HttpServer.cs` | ~546 | HTTP + WebSocket server. Routes requests, manages WS connections |
| `SlotTracker.cs` | ~136 | Thread-safe name→Slot mapping with RefID fallback resolution |
| `EventSystem.cs` | ~522 | Event subscriptions (fieldChanged, slotDestroyed, userJoin/Leave) |
| `TemplateSystem.cs` | ~435 | Snapshot/save/stamp slot hierarchies as reusable templates |

### Shared Utilities

| File | Purpose |
|:---|:---|
| `ComponentRegistry.cs` | Component type dictionary (short name → FrooxEngine Type) + resolution with generic parsing |
| `FieldParser.cs` | SetFieldValue, ReadFieldValue, ParseValueForType — type-safe field operations |
| `HandlerBase.cs` | Base class for all handlers. Provides tracker access, Ok/Error helpers, ResolveComponent, GetFocusedWorld |

### Handler Files (in `Handlers/`)

| File | Lines | Commands |
|:---|:---:|:---|
| `SlotHandlers.cs` | ~351 | createSlot, setSlotActive, destroySlot, destroyChildren, reparentSlot, setSlotName, setSlotTag, setSlotOrderIndex, duplicateSlot, setSlotPersist, setSlotTransform |
| `ComponentHandlers.cs` | ~538 | attachComponent, removeComponent, copyComponent, setField, setFields, getComponentField, getComponentFields, findComponents, getRegisteredComponents, getComponentByRefId, getAllComponents |
| `HierarchyHandlers.cs` | ~544 | getSlotInfo, getSlotTransform, listChildren, getSlotsByTag, findSlot, findSlots, findSlotByPath, trackExistingSlot, getParent, getSlotHierarchy |
| `AssetHandlers.cs` | ~204 | importTexture, importMesh, importAudio, importVideo |
| `UIXHandlers.cs` | ~137 | buildUIXTree (recursive declarative tree builder) |
| `ProtoFluxHandlers.cs` | ~313 | createProtoFluxNode, connectProtoFlux, setProtoFluxInput, getProtoFluxNode |
| `BuilderHandlers.cs` | ~403 | createPrimitive, createMaterial, create3DText, createLight |
| `PhysicsHandlers.cs` | ~131 | makePhysicsObject, createParticleSystem |
| `AnimationHandlers.cs` | ~160 | createAnimation (keyframes via ValueGradientDriver) |
| `WorldHandlers.cs` | ~155 | getWorldInfo, getUserInfo, getUsers, moveUser |
| `EnvironmentHandlers.cs` | ~83 | setupEnvironment (skybox, ambient light, reflection probes) |
| `ReferenceHandlers.cs` | ~303 | wireReference, addToList, removeFromList |
| `UtilityHandlers.cs` | ~461 | log, clearTracker, dynvar CRUD, events, templates, measureDistance, setFieldOnChildren, duplicateSlotArray |

## Data Flow

```
HTTP Client                    AntigravityBridge Mod
    │                                │
    ├─ POST /cmd ─────────────►  HttpServer.HandleRequest()
    │                                │
    │                                ├─ Parse JSON body
    │                                ├─ Route to CommandRouter.ExecuteCommand()
    │                                │       │
    │                                │       ├─ ValidateParams() [off-thread]
    │                                │       ├─ RunSynchronously() ◄── engine thread gate
    │                                │       │       │
    │                                │       │       ├─ ExecuteAction() switch dispatch
    │                                │       │       ├─ _xxxHandlers.HandleYzz(id, params)
    │                                │       │       └─ Return JObject result
    │                                │       │
    │                                │       └─ Return JSON response
    │                                │
    ◄─ JSON response ────────────────┘
```

## Threading Model

**Critical**: All FrooxEngine operations MUST run on the engine thread.

1. **HTTP thread**: `HttpServer` receives requests on a background thread
2. **Validation**: `ValidateParams()` runs OFF the engine thread (safe — only reads params)
3. **Engine dispatch**: `World.RunSynchronously()` marshals the handler to the engine thread
4. **Handler execution**: All handler code runs ON the engine thread
5. **Response**: Result is returned to the HTTP thread for sending

**Never** do FrooxEngine operations outside `RunSynchronously`. This includes:
- Creating/destroying slots
- Attaching/removing components
- Reading/writing field values
- Accessing world, user, or slot properties

## Key Design Patterns

### Handler Architecture
All handlers extend `HandlerBase`, which provides:
- `_tracker` — SlotTracker instance
- `Ok(id, result)` / `Error(id, message)` — response helpers
- `ResolveComponent(slot, typeName, index, id)` — component lookup with index support
- `GetFocusedWorld()` — safe focused world access

Shared utilities:
- `ComponentRegistry.Resolve(typeName)` — resolves short or full type names to `System.Type`
- `FieldParser.SetFieldValue(component, field, value, tracker)` — type-safe field writing
- `FieldParser.ReadFieldValue(member)` — type-safe field reading

### Command Registration (4 points)
Every command must be registered in:
1. **Help endpoint** — `GetCommandHelp()` in `CommandRouter.cs`
2. **Switch dispatch** — `ExecuteAction()` in `CommandRouter.cs` (lowercase key!)
3. **RequiredParams** — validation dictionary in `CommandRouter.cs`
4. **Handler method** — the actual implementation in `Handlers/*.cs`

See the `add_bridge_command` skill for details.

### Component Resolution (ComponentRegistry)
```
User input "PBS_Metallic" 
  → ComponentTypes dictionary lookup (fast, case-insensitive)
  → FrooxEngine.{name} assembly search
  → FrooxEngine.UIX.{name} search  
  → FrooxEngine.PhotonDust.{name} search
  → Generic parsing if contains '<' and '>'
```

### Slot Resolution (SlotTracker.Get)
```
User input "MySlot"
  → Special aliases (__root__, __localuser__)
  → Tracker dictionary lookup (case-insensitive)
  → Dead slot purge if found but destroyed
  → RefID parse fallback (hex string)
```

### Error Handling Pattern
```csharp
// All handlers use this pattern:
return Error(id, "Human-readable message");
return Ok(id, new JObject { ... });
```

## Config Keys (RML Settings)

| Key | Type | Default | Purpose |
|:---|:---:|:---:|:---|
| `Port` | int | 9090 | HTTP server port |
| `VerboseLogging` | bool | false | Log every command/response |

## Assembly References

| Assembly | Purpose |
|:---|:---|
| FrooxEngine.dll | Core engine — Slot, Component, World |
| Elements.Core.dll | Math types — float3, colorX, floatQ |
| Elements.Assets.dll | Asset types |
| SkyFrost.Base.dll | Cloud/networking types |
| ProtoFlux.Core.dll | ProtoFlux base types |
| ProtoFlux.Nodes.Core.dll | ProtoFlux node implementations |
| ProtoFlux.Nodes.FrooxEngine.dll | FrooxEngine-specific PF nodes |
| ProtoFluxBindings.dll | PF binding layer |
| Renderite.Shared.dll | Rendering types |
| Awwdio.dll | Audio engine interfaces |
| Newtonsoft.Json.dll | JSON parsing |
| ResoniteModLoader.dll | RML mod base class |
| 0Harmony.dll | Harmony patching (available but unused) |
