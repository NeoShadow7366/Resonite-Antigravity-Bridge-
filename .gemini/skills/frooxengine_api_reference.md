# FrooxEngine API Reference (Inspected from DLL)

> [!NOTE]
> Generated via MetadataLoadContext reflection on `FrooxEngine.dll` (version 2026.5.27.1300)

## Slot — Find Methods

| Method | Description |
|---|---|
| `Slot FindChild(string name)` | Find immediate child by exact name |
| `Slot FindChild(Predicate<Slot> filter, int maxDepth = -1)` | Find child matching predicate, with depth limit (-1 = unlimited) |
| `Slot FindChild(string name, bool matchSubstring, bool ignoreCase, int maxDepth = -1)` | Flexible name search with options |
| `Slot FindChildInHierarchy(string name)` | Recursive search by exact name |
| `Slot FindChildOrAdd(string name, bool persistent = true)` | Find or create child |
| `Slot FindParent(string name, bool matchSubstring, bool ignoreCase, int maxDepth = -1)` | Search parents by name |
| `List<Slot> GetChildrenWithTag(string tag)` | Get all children with a specific tag |
| `List<Slot> GetAllChildren(bool includeSelf = false)` | Get flat list of all descendants |

## Slot — Duplicate

| Method | Description |
|---|---|
| `Slot Duplicate(Slot duplicateRoot = null, bool keepGlobalTransform = true, DuplicationSettings settings = null, bool duplicateAsLocal = false)` | Deep copy slot hierarchy. Returns the new root slot. |

## Slot — Hierarchy

| Method | Description |
|---|---|
| `void SetParent(Slot newParent, bool keepGlobalTransform = true)` | Move to new parent. **Default keeps global transform.** |
| `void ReparentChildren(Slot newParent)` | Move all children to a new parent |
| `int ChildIndex { get; set; }` | Get/set ordering index among siblings |

## Slot — Destruction

| Method | Description |
|---|---|
| `void Destroy()` | Destroy slot and children |
| `void Destroy(Slot moveChildren, bool sendDestroyingEvent = true)` | Destroy but reparent children first |
| `void DestroyChildren(bool preserveAssets = false, ...)` | Destroy children with filter options |

## ISyncRef Interface (Reference Fields)

| Member | Type | Description |
|---|---|---|
| `Target` | `IWorldElement` | get/set — the referenced object |
| `RawTarget` | `IWorldElement` | get — raw target (bypasses proxies) |
| `Value` | `RefID` | get — the RefID being referenced |
| `TargetType` | `Type` | get — the generic type constraint |
| `Clear()` | void | Clear the reference |
| `TrySet(IWorldElement target)` | bool | Set the target (returns false if type mismatch) |

## Asset System

### Key Relationship
`AssetRef<T>` extends `SyncRef<T>` — so `ISyncRef.TrySet()` works for all asset references.

### Texture Pipeline
```
StaticTexture2D (component)
  └── URL: Sync<Uri>  ← set this to load the texture
  └── Inherited from StaticAssetProvider<Texture2D, ...>

SpriteProvider (component)
  └── Texture: AssetRef<IAssetProvider<ITexture2D>>  ← wire to StaticTexture2D
  └── Borders, Scale, Rect, FixedSize

UIX.Image (component)
  └── Sprite: AssetRef<IAssetProvider<ISprite>>  ← wire to SpriteProvider
  └── Tint: Sync<colorX>

UIX.RawImage (component)
  └── Texture: AssetRef<IAssetProvider<ITexture2D>>  ← wire directly to StaticTexture2D
  └── Tint: Sync<colorX>, PreserveAspect, UVRect
```

### Material Pipeline
```
UnlitMaterial (component)
  └── Texture: AssetRef  ← wire to StaticTexture2D
  └── TintColor: Sync<colorX>

PBS_Metallic (component, extends PBS_Material)
  └── AlbedoTexture: AssetRef  ← wire to StaticTexture2D
  └── AlbedoColor: Sync<colorX>
  └── NormalMap: AssetRef
  └── EmissiveMap: AssetRef, EmissiveColor: Sync<colorX>
  └── MetallicMap: AssetRef, Metallic: Sync<float>, Smoothness: Sync<float>
```

### Mesh Types
```
StaticMesh (component)
  └── URL: Sync<Uri>  ← same pattern as StaticTexture2D

BoxMesh (component)
  └── Size: Sync<float3>

SphereMesh, CylinderMesh, ConeMesh (procedural)

QuadMesh (component)
  └── Size: Sync<float2>, DualSided: Sync<bool>
```

### MeshRenderer (Rendering Pipeline)
```
MeshRenderer (component)
  └── Mesh: AssetRef<Mesh>  ← wire to any mesh component via ISyncRef.TrySet()
  └── Materials: SyncAssetList<Material>  ← use Add(IAssetProvider<Material>) to wire
  └── ShadowCastMode: Sync<ShadowCastMode>
  └── SortingOrder: Sync<int>
```

**To create a visible 3D object, you need:**
1. A mesh component (BoxMesh, StaticMesh, etc.)
2. A MeshRenderer with Mesh wired to the mesh component
3. A material (PBS_Metallic, UnlitMaterial) added to Materials list

The `createPrimitive` bridge command does all of this in one call.

## Worker — SyncMember Iteration

| Member | Description |
|---|---|
| `int SyncMemberCount` | Number of sync members on this worker |
| `ISyncMember GetSyncMember(int index)` | Get sync member by index |
| `ISyncMember GetSyncMember(string name)` | Get sync member by name |
| `string GetSyncMemberName(int index)` | Get name of sync member at index |
| `IEnumerable<ISyncMember> SyncMembers` | Enumerate all sync members |

## Slot — Transform Properties

| Property | Type | Access |
|---|---|---|
| `LocalPosition` | `float3` | get/set |
| `LocalRotation` | `floatQ` | get/set |
| `LocalScale` | `float3` | get/set |
| `GlobalPosition` | `float3` | get/set |
| `GlobalRotation` | `floatQ` | get/set |
| `GlobalScale` | `float3` | get/set |
