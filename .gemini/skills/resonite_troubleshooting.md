# Resonite Development — Troubleshooting & Gotchas

## Build & Compilation Issues

### GOTCHA: .NET Framework vs .NET 10
**Problem**: Initial attempt used `net462` (classic .NET Framework). Resonite runs on `net10.0`.
**Symptom**: DLL loads but crashes with `TypeLoadException` or `MissingMethodException`.
**Fix**: Always target `net10.0` in `.csproj`:
```xml
<TargetFramework>net10.0</TargetFramework>
```

### GOTCHA: Assembly Version Conflicts (MSB3277)
**Problem**: Multiple Resonite DLLs reference different versions of the same dependency.
**Symptom**: Build warnings about conflicting assembly versions.
**Fix**: Suppress the warning — it's harmless for mod development:
```xml
<NoWarn>$(NoWarn);MSB3277</NoWarn>
```

### GOTCHA: Missing Renderite.Shared
**Problem**: `Renderite.Shared.dll` is required but easy to overlook.
**Symptom**: Build error about missing type from `Renderite.Shared`.
**Fix**: Add explicit reference:
```xml
<Reference Include="Renderite.Shared">
  <HintPath>E:\SteamLibrary\steamapps\common\Resonite\Renderite.Shared.dll</HintPath>
</Reference>
```

### GOTCHA: Resonite DLL Location
**Problem**: Resonite's DLLs are in the root install directory, NOT in a `Managed` subfolder.
**Fix**: Reference path is directly `E:\SteamLibrary\steamapps\common\Resonite\FrooxEngine.dll`
(not `...\Resonite\Managed\FrooxEngine.dll`).

## Deployment Issues

### GOTCHA: DLL Locked While Running
**Problem**: `Copy-Item` fails with `IOException: file in use by another process`.
**Cause**: .NET runtime locks loaded assemblies. Resonite holds the DLL open.
**Fix**: Close Resonite → Copy DLL → Relaunch Resonite.
**No workaround exists** — this is a .NET runtime limitation.

### GOTCHA: No Hot-Reload
**Problem**: Mod changes require full Resonite restart.
**Cause**: RML loads mods once at startup. No reload mechanism exists.
**Mitigation**: Design the mod to be data-driven (JSON commands) so logic changes
don't require mod rebuilds. Only add new commands when the bridge API is insufficient.

## Text & Rendering Issues

### GOTCHA: Emoji Cause Mojibake
**Problem**: Emoji characters (🌐🔍🏠 etc.) display as garbled multi-byte sequences.
**Cause**: Resonite's default font doesn't include emoji glyphs. The UTF-8 bytes
are rendered individually as Latin characters.
**Fix**: Use ASCII alternatives. See `resonite_uix_patterns.md` for the full replacement table.

### GOTCHA: Some Unicode Symbols Also Fail
**Problem**: Characters like ◀ (U+25C0), ▼ (U+25BC), ⭐ (U+2B50), ⚙ (U+2699) may not render.
**Fix**: Test each character in-engine before committing. Safe characters:
- All ASCII printable characters (0x20-0x7E)
- Common Latin-1 supplement characters
- Basic mathematical symbols

### GOTCHA: Text Content via Bridge Must Be UTF-8
**Problem**: PowerShell's `Invoke-RestMethod` may mangle Unicode.
**Fix**: Always encode the body as UTF-8 bytes:
```powershell
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
Invoke-RestMethod -Uri $url -Method POST -Body $bytes -ContentType "application/json"
```

## Bridge Protocol Issues

### GOTCHA: Extra JSON Fields Cause Parse Errors
**Problem**: Build scripts with `"description"` or `"note"` fields fail to parse.
**Cause**: The batch endpoint expects exactly `{"commands": [...]}`.
**Fix**: Strip extra fields before sending:
```powershell
$obj = $raw | ConvertFrom-Json
$batch = @{commands=$obj.commands} | ConvertTo-Json -Depth 10 -Compress
```

### GOTCHA: ConvertTo-Json Default Depth
**Problem**: `ConvertTo-Json` defaults to depth 2, truncating nested objects.
**Fix**: Always specify `-Depth 10`:
```powershell
ConvertTo-Json -Depth 10 -Compress
```

### GOTCHA: Slot Name Collisions
**Problem**: Two slots with the same name — second overwrites first in tracker.
**Symptom**: Commands target the wrong slot. Children appear under wrong parent.
**Fix**: Always use unique slot names. Prefix with context: `LabelText_Search` not `LabelText`.

### GOTCHA: Component Already Exists
**Problem**: `attachComponent` doesn't check if the component already exists.
**Result**: Duplicate components on the same slot. Usually harmless but wastes memory.
**Note**: Some components are auto-added (Canvas adds RectTransform). If you then
explicitly attach RectTransform, you'll have two. Usually fine, but be aware.

## FrooxEngine Runtime Issues

### GOTCHA: RunSynchronously Timeout
**Problem**: Command returns "timed out (10s) waiting for engine thread".
**Cause**: Engine thread is busy (loading world, heavy computation, etc.).
**Fix**: Retry after a few seconds. If persistent, Resonite may be frozen.

### GOTCHA: Slot References Go Stale
**Problem**: A tracked slot was destroyed externally (by user or another mod).
**Symptom**: Commands fail with NullReferenceException.
**Fix**: The bridge should (TODO) check `slot.IsRemoved` before operating on slots.

### GOTCHA: Canvas Scale Affects Touch Interaction
**Problem**: Very small canvas scales make touch targets tiny and hard to interact with.
**Fix**: Scale 0.0003 with 1920×1080 canvas gives ~58cm width — good for VR interaction.
Don't go below 0.0001 or interactions become frustrating.

## PowerShell Gotchas

### GOTCHA: Single vs Double Quotes in JSON
**Problem**: PowerShell interpolates variables inside double quotes.
**Fix**: Use single quotes for literal JSON:
```powershell
$body = '{"id":"test","action":"ping","params":{}}'
```
Or escape properly:
```powershell
$body = "{`"id`":`"test`",`"action`":`"ping`",`"params`":{}}"
```

### GOTCHA: Invoke-RestMethod Error Handling
**Problem**: HTTP errors throw exceptions instead of returning error objects.
**Fix**: Wrap in try-catch or use `-ErrorAction SilentlyContinue`:
```powershell
try {
    $result = Invoke-RestMethod -Uri $url -Method POST -Body $body -ContentType "application/json"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
```
