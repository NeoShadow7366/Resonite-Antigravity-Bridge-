---
name: add-bridge-command
description: Add a new command to the AntigravityBridge mod. Use when adding a new API command — handles the handler method, switch registration, RequiredParams, help endpoint, and documentation updates. Ensures all 5 registration points stay in sync.
---

# Add Bridge Command Skill

## When to Use
Use this skill whenever you need to add a new command to the AntigravityBridge mod. This ensures all registration points are updated consistently.

## The 5 Registration Points

Every command in the mod requires entries in **exactly 5 places** in `CommandRouter.cs`. Missing any one causes bugs:

### 1. Help Endpoint (~line 220-270)
In the `GetHelpJson()` method, add a `JObject` entry with `params` and `description`:
```csharp
["myCommand"] = new JObject { ["params"] = "requiredParam, optionalParam? (default)", ["description"] = "What it does" },
```

### 2. Switch Dispatch (~line 280-360)
In the `ExecuteOnEngineThread()` switch expression, add the lowercase action mapping:
```csharp
"mycommand" => HandleMyCommand(id, p),
```
**IMPORTANT**: The switch key must be **all lowercase**. The action name is lowercased before matching.

### 3. RequiredParams (~line 380-450)
Add required parameter validation:
```csharp
["myCommand"] = new[] { "requiredParam1", "requiredParam2" },
// or for no required params:
["myCommand"] = Array.Empty<string>(),
```

### 4. Handler Method
Add the handler method in the appropriate section of CommandRouter.cs. Follow this pattern:
```csharp
private JObject HandleMyCommand(string id, JObject p)
{
    // 1. Extract params
    string slot = p["slot"]?.ToString();
    bool flag = p["flag"]?.Value<bool>() ?? true;  // with default
    
    // 2. Validate/resolve
    var slot = _tracker.Get(slotName);
    if (slot == null)
        return Error(id, $"Slot '{slotName}' not found");
    
    // 3. Do the work
    // ...
    
    // 4. Return result
    return Ok(id, new JObject
    {
        ["key"] = "value",
        ["refId"] = component.ReferenceID.ToString()
    });
}
```

### 5. Documentation
Update both documentation files:
- `README.md` — Add to Commands table, update command count
- `.gemini/skills/resonite_bridge_operations.md` — Add full command reference with JSON examples

## Handler Patterns

### Slot-targeting command (most common)
```csharp
private JObject HandleXyz(string id, JObject p)
{
    string slotName = p["slot"]?.ToString();
    var slot = _tracker.Get(slotName);
    if (slot == null)
        return Error(id, $"Slot '{slotName}' not found");
    // ... work with slot ...
    return Ok(id, new JObject { ... });
}
```

### Component-targeting command
```csharp
private JObject HandleXyz(string id, JObject p)
{
    string slotName = p["slot"]?.ToString();
    string componentName = p["component"]?.ToString();
    int componentIndex = p["componentIndex"]?.Value<int>() ?? 0;
    
    var slot = _tracker.Get(slotName);
    if (slot == null) return Error(id, $"Slot '{slotName}' not found");
    
    var (comp, error) = ResolveComponent(slot, componentName, componentIndex, id);
    if (error != null) return error;
    // ... work with comp ...
}
```

### World-level command (no slot required)
```csharp
private JObject HandleXyz(string id, JObject p)
{
    var world = Engine.Current.WorldManager.FocusedWorld;
    if (world == null) return Error(id, "No focused world");
    // ... work with world ...
}
```

### Creator command (creates new slot)
```csharp
private JObject HandleXyz(string id, JObject p)
{
    string parentName = p["parent"]?.ToString();
    string trackAs = p["trackAs"]?.ToString() ?? "DefaultName";
    
    var world = Engine.Current.WorldManager.FocusedWorld;
    if (world == null) return Error(id, "No focused world");
    
    Slot parent = null;
    if (!string.IsNullOrEmpty(parentName))
        parent = _tracker.Get(parentName);
    parent ??= world.RootSlot;
    
    var newSlot = parent.AddSlot(trackAs);
    _tracker.Register(trackAs, newSlot);
    
    // ... attach components, configure ...
    
    return Ok(id, new JObject
    {
        ["slotName"] = trackAs,
        ["refId"] = newSlot.ReferenceID.ToString(),
        ["trackedAs"] = trackAs
    });
}
```

## Common Gotchas

1. **Forgetting lowercase in switch**: `"mycommand"` not `"myCommand"`
2. **Missing RequiredParams entry**: Command works but has no validation — bad params hit the handler
3. **Thread safety**: All handler code runs on the engine thread via `RunSynchronously`. Don't start background tasks.
4. **Component types from non-standard namespaces**: Use fully qualified names (`FrooxEngine.PhotonDust.ParticleSystem`) or add to `ResolveComponentType` namespace search
5. **Generic components**: Use `ResolveComponentType("ValueGradientDriver<float>")` — the generic parser handles `<>` syntax
6. **ISyncRef wiring**: Cast the component appropriately. `Materials.Add().Target` expects `IAssetProvider<Material>`, not `Component`

## Verification Checklist

After adding a command:
- [ ] `dotnet build -c Release` passes with 0 errors
- [ ] Command appears in `/help` response
- [ ] RequiredParams validation works (test with missing required param)
- [ ] Handler returns proper `Ok(id, ...)` or `Error(id, ...)` response
- [ ] README.md command count updated
- [ ] Skills doc has full JSON example
