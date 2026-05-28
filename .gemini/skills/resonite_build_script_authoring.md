# AntigravityBridge Build Script Authoring

## Overview
Build scripts are JSON files containing batches of commands that the AntigravityBridge
mod executes sequentially to construct UIX hierarchies in Resonite.

## File Format
```json
{
  "description": "Human-readable description (stripped before sending)",
  "note": "Additional notes (also stripped)",
  "commands": [
    {"id": "unique_id", "action": "actionName", "params": {...}},
    ...
  ]
}
```

**IMPORTANT**: The `/batch` endpoint only accepts `{"commands": [...]}`. 
Extra fields like `description` and `note` cause parse errors. Always strip them:
```powershell
$raw = Get-Content "build_script.json" -Raw
$obj = $raw | ConvertFrom-Json
$batch = @{commands=$obj.commands} | ConvertTo-Json -Depth 10 -Compress
Invoke-RestMethod -Uri "http://localhost:9090/batch" -Method POST `
  -Body ([System.Text.Encoding]::UTF8.GetBytes($batch)) -ContentType "application/json"
```

## Slot Naming Rules

### Names MUST be unique
The SlotTracker uses a `ConcurrentDictionary<string, Slot>`. If two slots share a name,
the second registration **silently overwrites** the first reference. This means subsequent
commands targeting the original slot will hit the wrong one.

**Bad** — reusing "Icon" for multiple buttons:
```json
{"action": "createSlot", "params": {"name": "Icon", "parent": "NavBtn_Search"}},
{"action": "createSlot", "params": {"name": "Icon", "parent": "NavBtn_Main"}}
// Now "Icon" points to the SECOND slot only!
```

**Good** — unique names with prefixes:
```json
{"action": "createSlot", "params": {"name": "IconText", "parent": "NavBtn_Search"}},
{"action": "createSlot", "params": {"name": "MainIconText", "parent": "NavBtn_Main"}}
```

### Naming Conventions
- Use PascalCase for slot names: `SearchField`, `ContentArea`
- Prefix children with parent context: `LabelText_Search`, `LabelText_Main`
- Use suffixes for slot purpose: `_Scroll`, `_Mask`, `_Rect`, `_Content`
- Templates end with `Template`: `ResultItemTemplate`, `FavoriteItemTemplate`

## Command Ordering

Commands execute **strictly sequentially**. A child slot cannot be created before its parent.

**Correct order:**
1. Create parent slot
2. Attach components to parent
3. Create child slots
4. Attach components to children
5. Set field values (if not done inline via `fields`)
6. Set slot active/inactive states

**Pattern for a complete element:**
```json
{"id": "s1", "action": "createSlot", "params": {"name": "MyPanel", "parent": "Parent"}},
{"id": "s1_le", "action": "attachComponent", "params": {"slot": "MyPanel", "type": "LayoutElement", "fields": {"MinHeight": 50}}},
{"id": "s1_vl", "action": "attachComponent", "params": {"slot": "MyPanel", "type": "VerticalLayout", "fields": {"Spacing": 4}}},
{"id": "s1_img", "action": "attachComponent", "params": {"slot": "MyPanel", "type": "Image", "fields": {"Tint": [0.14, 0.14, 0.18, 1.0]}}}
```

## ID Conventions
- Use short, descriptive IDs: `"sb"` (sidebar), `"tb"` (top bar), `"av"` (article view)
- Suffix with component purpose: `"sb_le"` (sidebar layout element), `"sb_vl"` (sidebar vertical layout)
- Template IDs: `"tpl_ri"` (template result item), `"tpl_fav"` (template favorite)
- DynVar IDs: `"dv1"`, `"dv2"`, etc.

## Incremental Updates vs Full Rebuild

### Full Rebuild (clean slate)
```powershell
# 1. Destroy existing root
$body = '{"id":"cleanup","action":"destroySlot","params":{"slot":"WikiNavigator"}}'
Invoke-RestMethod -Uri "http://localhost:9090/cmd" -Method POST -Body $body -ContentType "application/json"

# 2. Clear tracker
$body = '{"id":"clear","action":"clearTracker","params":{}}'
Invoke-RestMethod -Uri "http://localhost:9090/cmd" -Method POST -Body $body -ContentType "application/json"

# 3. Execute full build script
# (as shown above)
```

### Incremental Updates (modify in-place)
```powershell
# Change a single text field
$body = '{"id":"fix","action":"setField","params":{"slot":"LangText","component":"Text","field":"Content","value":"EN v"}}'
Invoke-RestMethod -Uri "http://localhost:9090/cmd" -Method POST -Body $body -ContentType "application/json"

# Change scale
$body = '{"id":"scale","action":"setSlotTransform","params":{"slot":"Canvas","scale":[0.0003,0.0003,0.0003]}}'
Invoke-RestMethod -Uri "http://localhost:9090/cmd" -Method POST -Body $body -ContentType "application/json"
```

### Partial Rebuild (rebuild subtree)
```powershell
# Destroy children of a container, then rebuild just that section
$body = '{"id":"clear","action":"destroyChildren","params":{"slot":"Sidebar"}}'
# Then send new commands to rebuild sidebar contents
```

## Filtering Commands
To skip commands the bridge doesn't support yet:
```powershell
$filtered = $obj.commands | Where-Object { $_.action -ne "unsupportedAction" }
$batch = @{commands=$filtered} | ConvertTo-Json -Depth 10 -Compress
```

## Batch Size Limits
- No hard limit on command count (tested up to 310 successfully)
- Commands execute synchronously on the engine thread
- Very large batches (1000+) may cause frame hitches — consider splitting into chunks

## Build Script Location
All build scripts are stored in: `g:\Resonite\WikiFacet\bridge\`
- `build_wiki_navigator.json` — Full UIX shell (310 commands)
- Future scripts for modals, additional UI, etc.
