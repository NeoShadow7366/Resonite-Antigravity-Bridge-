# Resonite Mod Deploy Workflow

## Overview
The AntigravityBridge mod is compiled from C# source, then the resulting DLL is copied
into Resonite's `rml_mods` directory. Resonite loads mods at startup — there is no hot-reload.

## Build Command
```powershell
dotnet build -c Release
# Output: g:\Resonite\AntigravityBridgeMod\bin\Release\AntigravityBridge.dll
```

## Deploy Command
```powershell
Copy-Item "G:\Resonite\AntigravityBridgeMod\bin\Release\AntigravityBridge.dll" `
  "E:\SteamLibrary\steamapps\common\Resonite\rml_mods\AntigravityBridge.dll" -Force
```

## CRITICAL: DLL Lock Behavior
- **Resonite locks the DLL** while running — `Copy-Item` will fail with an IOException
- **You MUST close Resonite before deploying** a new version of the mod
- There is no workaround — .NET runtime locks loaded assemblies

## Full Deploy Cycle
```
1. Close Resonite
2. dotnet build -c Release
3. Copy-Item ... -Force
4. Launch Resonite
5. Wait for mod to load (check /ping endpoint)
6. Execute commands
```

## One-liner (Resonite must be closed)
```powershell
dotnet build -c Release -o "E:\SteamLibrary\steamapps\common\Resonite\rml_mods\" 2>&1 | Select-Object -Last 5
```
NOTE: This outputs ALL build artifacts to rml_mods — only use if no other build outputs conflict.

## Verification After Launch
```powershell
# Wait a few seconds after Resonite starts, then:
Invoke-RestMethod -Uri "http://localhost:9090/ping" -Method GET | ConvertTo-Json
# Expected: {"status":"ok","mod":"AntigravityBridge","version":"1.0.0","trackedSlots":0}
```

## Project Structure
```
g:\Resonite\AntigravityBridgeMod\
├── AntigravityBridge.csproj    # .NET 10, references FrooxEngine + RML
├── AntigravityBridge.cs        # RML mod entry point (OnEngineInit)
├── HttpServer.cs               # HttpListener on localhost:9090
├── SlotTracker.cs              # ConcurrentDictionary<string, Slot>
└── CommandRouter.cs            # JSON command → FrooxEngine API dispatch
```

## CSProj Key Settings
- Target: `net10.0`
- RML NuGet: `ResoniteModLoader` 5.0.1
- Assembly references: FrooxEngine.dll, Elements.Core.dll, SkyFrost.Base.dll etc.
- `NoWarn` includes `MSB3277` (assembly version conflicts)
- All Resonite DLLs referenced from `E:\SteamLibrary\steamapps\common\Resonite\`

## Common Build Errors
| Error | Fix |
|---|---|
| `MSB3277` assembly conflicts | Already suppressed via `NoWarn` |
| Missing `Renderite.Shared` | Add reference from Resonite install dir |
| `net462` incompatible | Resonite requires `net10.0` — do NOT use .NET Framework |
| DLL locked IOException | Close Resonite first, then copy |
