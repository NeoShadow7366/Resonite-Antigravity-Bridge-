# Extending the AntigravityBridge

## Overview
This skill documents how to add new commands and capabilities to the AntigravityBridge mod.
The mod follows a consistent pattern: switch-based routing → handler method → FrooxEngine API.

## Architecture
```
HttpServer.cs          → Receives HTTP request, parses JSON
  ↓
CommandRouter.cs       → Routes to handler based on "action" field
  ↓
Handler method         → Executes FrooxEngine operations on engine thread
  ↓
SlotTracker.cs         → Manages name→Slot references
```

## Adding a New Command

### Step 1: Add to the switch statement in CommandRouter.cs
Location: `ExecuteCommand()` method, inside the `action switch` block (~line 79-93).

```csharp
result = action switch
{
    "ping" => Ok(id, new JObject { ["message"] = "pong" }),
    "createslot" => HandleCreateSlot(id, p),
    // ... existing commands ...
    "yournewcommand" => HandleYourNewCommand(id, p),  // ADD HERE
    "cleartracker" => HandleClearTracker(id),
    _ => Error(id, $"Unknown action: {action}")
};
```

**IMPORTANT**: Action names are lowercased before matching (`action.ToLowerInvariant()`).
Always use all-lowercase in the switch.

### Step 2: Create the handler method
Add a new private method following the existing pattern:

```csharp
private JObject HandleYourNewCommand(string id, JObject p)
{
    // 1. Extract parameters
    string slotName = p["slot"]?.ToString();
    
    // 2. Resolve slot
    var slot = _tracker.Get(slotName);
    if (slot == null)
        return Error(id, $"Slot '{slotName}' not found");
    
    // 3. Execute FrooxEngine operations
    // (this code runs on the engine thread via RunSynchronously)
    slot.SomeOperation();
    
    // 4. Return result
    return Ok(id, new JObject
    {
        ["slot"] = slotName,
        ["result"] = "value"
    });
}
```

### Step 3: Build and deploy
```powershell
# Close Resonite first!
dotnet build -c Release
Copy-Item "bin\Release\AntigravityBridge.dll" "E:\...\rml_mods\AntigravityBridge.dll" -Force
# Relaunch Resonite
```

## Adding New Component Types

### Short Name Lookup
Add to the `ComponentTypes` dictionary at the top of CommandRouter.cs:

```csharp
private static readonly Dictionary<string, Type> ComponentTypes = new(StringComparer.OrdinalIgnoreCase)
{
    // Existing entries...
    ["Canvas"] = typeof(Canvas),
    ["Image"] = typeof(Image),
    
    // Add new entries:
    ["GridLayout"] = typeof(GridLayout),
    ["Hyperlink"] = typeof(Hyperlink),
    ["TextEditor"] = typeof(TextEditor),
};
```

### Reflection Fallback
If you don't add to the dictionary, the mod will try to resolve via reflection:
1. `FrooxEngine.{typeName}`
2. `FrooxEngine.UIX.{typeName}`

This works for most components but is slower. Add to the dictionary for performance.

## Adding New Field Types

### Current Supported Types (in SetFieldValue / ReadFieldValue)
- `Sync<string>` → string value
- `Sync<bool>` → boolean value
- `Sync<int>` → integer value
- `Sync<float>` → float value
- `Sync<float2>` → `[x, y]` array
- `Sync<float3>` → `[x, y, z]` array
- `Sync<float4>` → `[x, y, z, w]` array
- `Sync<floatQ>` → `[x, y, z, w]` array or `[euler_x, euler_y, euler_z]` (write supports both; read returns quaternion)
- `Sync<colorX>` → `[r, g, b, a]` array or `"#RRGGBB"` hex string
- `Sync<Uri>` → `"https://..."` string (for asset/URL references)
- **All enum types** → string value (auto-detected via reflection, case-insensitive)

### Enum Fields (Auto-Handled)
Enum-typed fields like `SizeFit`, `Alignment`, `LayoutAxis` are handled automatically.
No need to add explicit switch cases for them.

**Writing**: Pass the enum value name as a string (case-insensitive):
```json
{"action": "setField", "params": {
  "slot": "MySlot", "component": "ContentSizeFitter",
  "field": "HorizontalFit", "value": "PreferredSize"
}}
```

**Reading**: Returns the enum value name as a string:
```json
{"action": "getComponentField", "params": {
  "slot": "MySlot", "component": "ContentSizeFitter",
  "field": "HorizontalFit"
}}
// Response: {"value": "PreferredSize", "fieldType": "Sync`1"}
```

**How it works**: The `default` case in both `SetFieldValue` and `ReadFieldValue` uses reflection
to detect `Sync<T>` where `T.IsEnum`, then uses `Enum.Parse()` / `.ToString()` to convert.

### Adding a New Non-Enum Field Type
Add a new case to the switch in `SetFieldValue()` (and `ReadFieldValue()` for reads):

```csharp
// In SetFieldValue:
case Sync<YourType> yt:
    // Parse from JToken
    yt.Value = /* parse logic */;
    break;

// In ReadFieldValue:
case Sync<YourType> yt:
    return /* convert to JToken */;
```

### Common Types You Might Still Need to Add
| FrooxEngine Type | JSON Format | Notes |
|---|---|---|
| `Sync<int2>` | `[x, y]` | Integer 2D vector |
| `Sync<double>` | number | Double precision float |
| `Sync<long>` | number | 64-bit integer |
| `SyncRef<T>` | RefID string | Reference to another component/slot |

## Thread Safety

### The Golden Rule
ALL FrooxEngine operations MUST run on the engine thread. The bridge does this automatically
via `Engine.Current.WorldManager.FocusedWorld.RunSynchronously()`.

Never access FrooxEngine objects outside this callback. The HttpListener runs on a background
thread — the `RunSynchronously` call marshals to the engine thread and blocks until complete.

### Timeout
Commands have a 10-second timeout. If the engine thread is blocked (loading, etc.),
the command will return a timeout error.

### SlotTracker Thread Safety
`SlotTracker` uses `ConcurrentDictionary` — safe to read/write from any thread.
However, the Slot objects it contains must only be accessed on the engine thread.

## Error Handling Pattern
```csharp
// Always validate inputs first
if (string.IsNullOrEmpty(requiredParam))
    return Error(id, "Missing required parameter 'paramName'");

// Resolve slots with null check
var slot = _tracker.Get(slotName);
if (slot == null)
    return Error(id, $"Slot '{slotName}' not found");

// Wrap FrooxEngine calls in try-catch (already done at the router level)
// but you can add specific error messages:
try
{
    slot.SomeOperation();
}
catch (Exception ex)
{
    return Error(id, $"Failed to perform operation: {ex.Message}");
}
```

## Testing New Commands
```powershell
# Single command test
$body = '{"id":"test","action":"yournewcommand","params":{"slot":"TestSlot"}}'
Invoke-RestMethod -Uri "http://localhost:9090/cmd" -Method POST -Body $body -ContentType "application/json" | ConvertTo-Json

# Error case test
$body = '{"id":"err","action":"yournewcommand","params":{"slot":"NonExistent"}}'
Invoke-RestMethod -Uri "http://localhost:9090/cmd" -Method POST -Body $body -ContentType "application/json" | ConvertTo-Json
```
