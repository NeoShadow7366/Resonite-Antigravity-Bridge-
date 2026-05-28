# Resonite UIX Development Patterns

## Font & Text Rendering

### CRITICAL: No Emoji Support
Resonite's default font does NOT render emoji characters. They display as mojibake (garbled multi-byte sequences).

**Never use these** (or any emoji):
`🌐 🔍 🏠 🎲 📰 📂 📄 ⭐ ◀ 🔎 📁 ⚙ 📎 ✕ ☆ ★`

**Use ASCII/Latin alternatives instead:**
| Intent | Use | Avoid |
|---|---|---|
| Globe/Web | `W` | 🌐 |
| Search | `>>` | 🔍 🔎 |
| Home | `#` | 🏠 |
| Random | `?` | 🎲 |
| News/Recent | `~` | 📰 |
| Folder/Category | `/` | 📂 📁 |
| Page/Document | `=` | 📄 |
| Favorite/Star | `*` | ⭐ ★ ☆ |
| Back arrow | `<` | ◀ ← |
| Forward arrow | `>` | ▶ → |
| Close | `x` | ✕ ✖ |
| Settings/Menu | `...` | ⚙ |
| Dropdown indicator | `v` | ▼ |
| Link/Reference | `--` or `>` | 📎 |

### Safe Unicode Characters
These basic Unicode characters ARE generally safe in Resonite:
- Standard Latin letters, digits, punctuation
- Common math symbols: `+ - * / = < > ( ) [ ] { }`
- Basic punctuation: `. , : ; ! ? ' " - _`

## Component Type Names (for AntigravityBridge)

### Built-in Type Map (case-insensitive)
| Short Name | FrooxEngine Type |
|---|---|
| `Canvas` | `FrooxEngine.UIX.Canvas` |
| `Image` | `FrooxEngine.UIX.Image` |
| `Text` | `FrooxEngine.UIX.Text` |
| `Button` | `FrooxEngine.UIX.Button` |
| `Mask` | `FrooxEngine.UIX.Mask` |
| `RawImage` | `FrooxEngine.UIX.RawImage` |
| `RectTransform` | `FrooxEngine.UIX.RectTransform` |
| `VerticalLayout` | `FrooxEngine.UIX.VerticalLayout` |
| `HorizontalLayout` | `FrooxEngine.UIX.HorizontalLayout` |
| `LayoutElement` | `FrooxEngine.UIX.LayoutElement` |
| `ContentSizeFitter` | `FrooxEngine.UIX.ContentSizeFitter` |
| `ScrollRect` | `FrooxEngine.UIX.ScrollRect` |
| `DynamicVariableSpace` | `FrooxEngine.DynamicVariableSpace` |
| `SmoothTransform` | `FrooxEngine.SmoothTransform` |

### Auto-added Components
- Attaching `Canvas` auto-adds `RectTransform` + `BoxCollider`
- Attaching `Image` auto-adds `RectTransform`
- Attaching `Text` auto-adds `RectTransform`

### Reflection Fallback
Any component not in the built-in map is resolved via reflection:
1. `FrooxEngine.{typeName}` (main namespace)
2. `FrooxEngine.UIX.{typeName}` (UIX namespace)

## Color Scheme (Dark Theme — Resonite Native)

| Element | RGBA | Hex | Usage |
|---|---|---|---|
| Background | `[0.07, 0.07, 0.09, 1.0]` | `#121217` | Main panel, content area |
| Sidebar bg | `[0.10, 0.10, 0.13, 1.0]` | `#1A1A21` | Sidebar, top bar, modals |
| Button idle | `[0.14, 0.14, 0.18, 1.0]` | `#24242E` | Button backgrounds, cards |
| Button idle (transparent) | `[0.14, 0.14, 0.18, 0.0]` | — | Nav buttons (show on hover) |
| Hover | `[0.20, 0.20, 0.26, 1.0]` | `#333342` | Button hover state |
| Active/Pressed | `[0.18, 0.40, 0.85, 1.0]` | `#2E66D9` | Active button, accent |
| Divider | `[0.22, 0.22, 0.28, 0.5]` | `#383847` | Thin separator lines |
| TOC bg | `[0.12, 0.12, 0.16, 1.0]` | `#1E1E29` | Table of contents panel |
| Warning banner | `[0.85, 0.65, 0.10, 0.15]` | — | Language fallback banner |
| Primary text | — | `#EBEBF5` | Main text, labels |
| Secondary text | — | `#8C8C9E` | Muted text, descriptions |
| Link text | — | `#338CFF` | Clickable links |
| Error text | — | `#D94040` | Errors, delete buttons |

## Layout Patterns

### Navigation Button Pattern
```json
[
  {"action": "createSlot", "params": {"name": "NavBtn_X", "parent": "Sidebar"}},
  {"action": "attachComponent", "params": {"slot": "NavBtn_X", "type": "LayoutElement", "fields": {"MinHeight": 48, "PreferredHeight": 48}}},
  {"action": "attachComponent", "params": {"slot": "NavBtn_X", "type": "HorizontalLayout", "fields": {"PaddingLeft": 12, "Spacing": 10}}},
  {"action": "attachComponent", "params": {"slot": "NavBtn_X", "type": "Image", "fields": {"Tint": [0.14, 0.14, 0.18, 0.0]}}},
  {"action": "attachComponent", "params": {"slot": "NavBtn_X", "type": "Button"}},
  // Icon child:
  {"action": "createSlot", "params": {"name": "Icon_X", "parent": "NavBtn_X"}},
  {"action": "attachComponent", "params": {"slot": "Icon_X", "type": "LayoutElement", "fields": {"MinWidth": 30}}},
  {"action": "attachComponent", "params": {"slot": "Icon_X", "type": "Text", "fields": {"Content": ">", "Size": 22}}},
  // Label child:
  {"action": "createSlot", "params": {"name": "Label_X", "parent": "NavBtn_X"}},
  {"action": "attachComponent", "params": {"slot": "Label_X", "type": "LayoutElement", "fields": {"FlexibleWidth": 1}}},
  {"action": "attachComponent", "params": {"slot": "Label_X", "type": "Text", "fields": {"Content": "Label", "Size": 26}}}
]
```

### Scroll Area Pattern
```json
[
  {"action": "createSlot", "params": {"name": "ScrollMask", "parent": "Parent"}},
  {"action": "attachComponent", "params": {"slot": "ScrollMask", "type": "LayoutElement", "fields": {"FlexibleHeight": 1}}},
  {"action": "attachComponent", "params": {"slot": "ScrollMask", "type": "Mask"}},
  {"action": "attachComponent", "params": {"slot": "ScrollMask", "type": "Image", "fields": {"Tint": [0, 0, 0, 0]}}},
  {"action": "createSlot", "params": {"name": "ScrollRect", "parent": "ScrollMask"}},
  {"action": "attachComponent", "params": {"slot": "ScrollRect", "type": "ScrollRect"}},
  {"action": "createSlot", "params": {"name": "ScrollContent", "parent": "ScrollRect"}},
  {"action": "attachComponent", "params": {"slot": "ScrollContent", "type": "VerticalLayout", "fields": {"Spacing": 2}}},
  {"action": "attachComponent", "params": {"slot": "ScrollContent", "type": "ContentSizeFitter"}}
]
```

### Divider Pattern
```json
[
  {"action": "createSlot", "params": {"name": "Divider", "parent": "Parent"}},
  {"action": "attachComponent", "params": {"slot": "Divider", "type": "LayoutElement", "fields": {"MinHeight": 1, "PreferredHeight": 1}}},
  {"action": "attachComponent", "params": {"slot": "Divider", "type": "Image", "fields": {"Tint": [0.22, 0.22, 0.28, 0.5]}}}
]
```

## Canvas Scaling

A Canvas with `Size = [1920, 1080]` at different scales:
| Scale | Physical Width | Use Case |
|---|---|---|
| `0.0001` | ~19cm | Small dashboard facet |
| `0.0003` | ~58cm | Standard facet (current Wiki Navigator) |
| `0.0005` | ~96cm | Large display |
| `0.001` | ~1.92m | Wall-sized panel |

## Common Field Names

### LayoutElement
`MinWidth`, `MinHeight`, `PreferredWidth`, `PreferredHeight`, `FlexibleWidth`, `FlexibleHeight`, `MaxWidth`, `MaxHeight`

### HorizontalLayout / VerticalLayout
`Spacing`, `PaddingTop`, `PaddingBottom`, `PaddingLeft`, `PaddingRight`, `ForceExpandWidth`, `ForceExpandHeight`

### Image
`Tint` (colorX)

### Text
`Content` (string), `Size` (float)

### Canvas
`Size` (float2)

### ScrollRect
`NormalizedPosition` (float2) — `[0, 1]` = top, `[0, 0]` = bottom
