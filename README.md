# LF Map Scaler — BepInEx Mod

A [BepInEx 5](https://github.com/BepInEx/BepInEx) mod for **Lethal Fantasy Beta v0.1.3** that scales the procedurally generated dungeon to a configurable size multiplier, with supporting fixes to keep HLOD rendering, navmesh baking, pathfinding, terrain, and visibility working correctly at any scale.

---

## Requirements

| Requirement | Version |
|---|---|
| Lethal Fantasy | Beta v0.1.3 |
| BepInEx | 5.4.21 |
| .NET Runtime | .NET Standard 2.1 compatible |

---

## Installation

1. Install [BepInEx 5.4.21](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.21) into your Lethal Fantasy game folder if you haven't already.
2. Download `LethalFantasyMapScaler.dll` from the [Releases](../../releases) page.
3. Drop the DLL into `<Game Folder>\BepInEx\plugins\`.
4. Launch the game. A config file will be generated at `BepInEx\config\com.author.lethalfantasymapscaler.cfg` on the first run.

### Building from source

1. Clone the repository.
2. Copy `Directory.Build.props.example` to `Directory.Build.props` in the same folder.
3. Edit `Directory.Build.props` and set `GameDir` to your Lethal Fantasy installation path.
4. Run `dotnet build -c Release`. The DLL is automatically copied to `<GameDir>\BepInEx\plugins\` if that folder exists.

---

## Configuration

Edit `BepInEx\config\com.author.lethalfantasymapscaler.cfg` while the game is closed, or use a BepInEx config manager mod in-game.

| Key | Default | Range | Description |
|---|---|---|---|
| `MapSizeMultiplier` | `1.5` | 0.25 – 10 | Multiplier for the main dungeon path length. `1.0` = default game size. |
| `BranchMultiplier` | `1.0` | 0.25 – 5 | Multiplier for branch depth and branch count on each dungeon archetype. |
| `ScaleWorldBoundary` | `true` | — | Scale the out-of-bounds terrain boundary with the map. Disable if you see terrain issues. |
| `LoadingFrameBudgetMs` | `500` | 0 – 10000 | Milliseconds each setup coroutine may run per frame. Higher = faster load, more hitching. `0` = no limit. |
| `MaxGenerationAttempts` | `40` | 5 – 200 | Room placement retries before DunGen gives up. Larger maps may need more. |
| `GrassDensity` | `1.0` | 0.1 – 1.0 | Fraction of grass spawns per tile. `0.5` = 4× faster placement, `0.25` = 16× faster. |
| `VisibilityMaxDistance` | `0` | 0 – 5000 | Skip tile-pair visibility checks beyond this distance (Unity units). `0` = check all pairs. |
| `EnableOptimizations` | `false` | — | Enables optional load-time speed-ups (pathfinding, loot deferral, grass scaling, OOB grid). **WARNING: may conflict with HLOD rendering. Leave false for stable rendering.** |

---

## How It Works

The mod intercepts DunGen's dungeon generator just before each generation attempt and applies three scaling changes:

- **Main path length** — `DungeonGenerator.LengthMultiplier` is set to `MapSizeMultiplier`, giving proportionally more rooms on the main corridor.
- **Branch depth/count** — every `DungeonArchetype`'s `BranchingDepth` and `BranchCount` are multiplied by `BranchMultiplier`.
- **Tile placement bounds** — the spatial boundary that DunGen uses when placing rooms is scaled by `MapSizeMultiplier`, keeping the dungeon inside the area the giantess terrain covers (critical for HLOD rendering — see the technical section below).

Several supporting patches handle the knock-on effects of a larger dungeon: navmesh baking, terrain boundary, giantess pathfinding, visibility polygon ray length, terrain painter caching, grass density, and a DunGen stack-overflow bug that only manifests at runtime.

---

<details>
<summary><strong>In-Depth Technical Reference</strong> (click to expand)</summary>

<br>

### Table of Contents

1. [HLOD Architecture](#hlod-architecture)
2. [Why Dungeon Bounds Matter](#why-dungeon-bounds-matter)
3. [Patch Reference](#patch-reference)
   - [DungeonGeneratorPatch](#dungeongeneratorpatch)
   - [NavMeshAsyncPatch](#navmeshasyncpatch)
   - [GiantessPathfindingPatch](#gianteesspathfindingpatch)
   - [MapVisibilityPatch](#mapvisibilitypatch)
   - [WorldBoundaryPatch](#worldboundarypatch)
   - [GrassPatch](#grasspatch)
   - [LootZonePatch](#lootzonepatch)
   - [TerrainPainterPatch](#terrainpainterpatch)
   - [RetryLimitPatch](#retrylimitpatch)
   - [BuildingZonePatch (disabled)](#buildingzonepatch-disabled)
4. [SetupManager Sequence](#setupmanager-sequence)
5. [EnableOptimizations Flag](#enableoptimizations-flag)

---

### HLOD Architecture

Lethal Fantasy uses a Burst-compiled `VisibilityJob` inside `KiriHLODManager` to decide each frame which dungeon tiles to show. It sets a `tileBitMask` (one bit per tile, max 64 tiles) via two independent paths:

**Step 1 — Height check**

```
if (cameraPos.y >= items[i].height) → set bit i
```

`items[i].height` is computed as:

```
TerrainManager.innerTerrainZone.baseHeight  (≈ 400 units)
  + wallVisibilityOffset                    (8 units)
  + kiriTile.selfTerrainZone.baseHeight
```

This means tiles become visible when the camera is approximately **408+ units above sea level** — the altitude at which the giantess normally operates when looking down at the dungeon from above.

**Step 3 — XZ quad-tree query**

```
kiriTileLinearQuadTree.Query(cameraPos, list)
```

This finds tiles whose `KiriTileDOTS.boundsMin/Max` (the XZ footprint of that tile in world space) contains the camera's XZ position, then ORs their `visibileTileBit` into `tileBitMask`. This is the path that makes tiles visible at **ground level** — when the giantess is standing inside or next to the dungeon.

**Step 4 — Frustum cull**

Bits corresponding to tiles outside the camera frustum are cleared.

Each frame, for tile `i`:
- If `(tileBitMask >> i) & 1` → call `UpdateRenders(dist)` — terrain heightmap on, buildings shown via HLOD groups
- Otherwise → call `ClearRenders()` — terrain heightmap off (buildings are managed separately by `globalData`)

---

### Why Dungeon Bounds Matter

The original mod attempt set `DungeonGenerator.RestrictDungeonToBounds = false` to give DunGen room to place more tiles. This turned out to be the root cause of the main rendering bug ("buildings only appear when 400+ units above").

With `RestrictDungeonToBounds = false`, DunGen places dungeon tiles at **arbitrary XZ coordinates** anywhere in the world. The `KiriTileDOTS.boundsMin/Max` values for each tile are computed from that tile's actual world-space position after generation. If the dungeon lands at XZ = (2000, 3000) and the giantess terrain is centered at (0, 0), the camera's XZ position is never inside any tile's bounds. Step 3 never fires at ground level, so no tiles are shown. The only rendering that works is step 1 — the height check at 400+ altitude — which is why that appeared to work.

**The fix**: keep `RestrictDungeonToBounds = true` and instead scale `TilePlacementBounds` proportionally to `MapSizeMultiplier`. The dungeon stays inside a larger but still giantess-terrain-centred region, so the camera is always XZ-over the tiles it stands on.

```csharp
Bounds origBounds = s_originalBounds[__instance].bounds;
__instance.TilePlacementBounds = new Bounds(origBounds.center, origBounds.size * mult);
```

The original bounds (and the `RestrictDungeonToBounds` flag) are saved on the first call and restored when generation completes or fails, so reloading a level without the mod works correctly.

---

### Patch Reference

#### DungeonGeneratorPatch

**Target:** `DungeonGenerator.Generate` (Prefix)

Applies three changes before each generation attempt:

1. `LengthMultiplier = MapSizeMultiplier` — controls main-path room count.
2. `MaxAttemptCount = MaxGenerationAttempts` — more retries for larger maps.
3. `TilePlacementBounds.size *= MapSizeMultiplier` — scaled spatial budget (see above).
4. For each `DungeonArchetype`, `BranchingDepth` and `BranchCount` are multiplied by `BranchMultiplier`. Archetype originals are saved on first call and restored from there on every retry, so retries always scale from the same baseline.

All values are restored in `OnStatusChanged` (hooked to `DungeonGenerator.OnAnyDungeonGenerationStatusChanged`) when generation reaches `Complete` or `Failed`.

---

#### NavMeshAsyncPatch

**Target:** `NavMeshBakeOnDungeonFinish.DungeonGenerator_OnGenerationComplete` (Prefix, replaces method)

The vanilla handler calls `navMeshSurface.BuildNavMesh()` synchronously inside an event callback, freezing the main thread for the entire bake. This patch replaces it with a coroutine:

```
1. If navMeshData is null → create NavMeshData, set it on the surface, call AddData()
   (registers the empty data with the NavMesh so pathfinding has a valid object immediately)
2. Call surface.UpdateNavMesh(data) → AsyncOperation (runs on a worker thread)
3. Yield each frame until op.isDone
4. If UpdateNavMesh returns null or throws → fall back to BuildNavMesh()
```

All three NavMeshSurface members used (`navMeshData`, `AddData()`, `UpdateNavMesh()`) are confirmed public in the game's Unity AI Navigation DLL — no reflection is required.

---

#### GiantessPathfindingPatch

**Target:** `GiantessPathfinding.Setup` (Prefix, replaces the coroutine)

**Gated by `EnableOptimizations`.**

The vanilla `Setup` coroutine calls `GraphPathfinding.CreateCoroutine`, which calls `VisibilityPolygon.BreakIntersectionsSlow` with a LINQ iterator. The complexity is O(N³) because:

- The iterator is backed by `.Select().Where()` chains, so `Count()` is O(N) per call.
- `BreakIntersectionsSlow` calls `Count()` inside the inner loop condition — O(N) × O(N²) inner iterations = O(N³) total.

The replacement (`FastSetup`) does two things:

1. **O(N³) → O(N²):** Materialises the LINQ iterator into a `List<Segment>` before the loops, dropping all re-enumeration overhead. The underlying algorithm is O(N²) — the O(N³) was pure overhead.

2. **Background thread:** `BreakIntersectionsFixed` only touches `Vector3` and `VisibilityPolygon.Segment` value types (no Unity API). It runs on `Task.Run`, yielding each frame on the main thread until the task completes.

The replacement also adds `SetupStopwatch` yield points inside the deduplication and connection-wiring loops, which the original lacked entirely.

`GiantessPathfinding.Instance` has a `private set` — the replacement sets it via cached `PropertyInfo.SetValue(null, instance)` reflection.

---

#### MapVisibilityPatch

**Target:** `MapVisibility.CreateMap` state machine `MoveNext` (Transpiler) + `KiriHLODManager.SetupGlobalTileAndData` (Postfix) + `EmptyGridFinder.GetEmptyCellCenters` (Prefix)

Three patches in one file:

**Patch 1 — PIP ray length (always active)**

`CreateMap` tests whether a tile's centroid is inside a visibility polygon using a point-in-polygon ray cast: `vector4 + Vector3.forward * 1000f`. On a scaled map the polygon is larger and the ray can terminate before exiting it, causing tiles to appear mutually invisible. The Transpiler replaces the `ldc.r4 1000` constant with a call to:

```csharp
public static float GetScaledRayLength() => 1000f * MapSizeMultiplier * 1.2f;
```

The transpiler targets the `CreateMap` state machine's `MoveNext` directly because `CreateMap` is an iterator method — its body lives in a compiler-generated nested type.

**Patch 2 — Visibility distance cull (disabled by default)**

After `SetupGlobalTileAndData` runs, iterates every tile's `visibleTiles` list and removes pairs whose centres are farther apart than `VisibilityMaxDistance`. Cuts the O(N²) `CreateMap` cost on large maps if a reasonable distance is set (e.g. 300–600 units). Default is `0` (disabled, vanilla behaviour).

**Patch 3 — OOB grid cell size (gated by `EnableOptimizations`)**

`EmptyGridFinder.GetEmptyCellCenters` places one ground collider per 60×60 unit cell outside the dungeon. At 1.5× world scale the grid spans (1152/60)² ≈ 369 cells vs 164 vanilla. Scaling `cellSize` by `MapSizeMultiplier` keeps the cell count constant at any multiplier, so loading time stays the same as vanilla.

---

#### WorldBoundaryPatch

**Target:** `TerrainManager.CreateColliders` (Prefix)

The caller computes `worldBounds = Vector3.one * 512f * 1.5f = 768 units` and passes it to `CreateColliders`, which uses `EmptyGridFinder` to place out-of-bounds ground colliders. The patch scales `worldBounds.size` by `MapSizeMultiplier` before the coroutine captures it, ensuring the collider grid wraps the entire scaled dungeon.

`CreateVisuals` receives the same bounds but ignores it (tree placement uses an outline polygon), so no patch is needed there.

Controlled by the `ScaleWorldBoundary` config key.

---

#### GrassPatch

**Target:** `GrassRenderer.ProcessGrass` (Prefix + Postfix + Transpiler)

**Gated by `EnableOptimizations`.**

Two changes:

1. **Density scaling (Prefix/Postfix):** `spawnCountPerMeter` is multiplied by `GrassDensity` before the loop and restored after. Grass iteration is O(N²) in tile area, so halving density cuts iterations by 4×.

2. **Physics.SyncTransforms removal (Transpiler):** `ProcessGrass` calls `Physics.SyncTransforms()` once per tile at the start. By the time grass is placed (after dungeon generation and terrain setup), transforms are stable. The call is a no-op in practice but still incurs a full physics-engine round-trip. The Transpiler replaces it with a `nop`.

When `EnableOptimizations` is false, both the Transpiler and Prefix pass through unchanged.

---

#### LootZonePatch

**Target:** `LootZoneManager.SetupZones` (Prefix/Postfix) + `NodeTags.SpawnPrefab` (Prefix)

**Gated by `EnableOptimizations`.**

`SetupZones` is a synchronous method that fires `NodeTags.SpawnPrefab` for every non-selected loot node — on large maps this is many `Instantiate` + `ProcessLocalProps` calls in a single frame.

When enabled:
- The `SetupZones` Prefix sets a `s_deferring` flag and clears the queue.
- The `SpawnPrefab` Prefix enqueues the call and returns `false` (skip original) while `s_deferring` is true.
- The `SetupZones` Postfix clears the flag and starts a `DrainQueue` coroutine that processes one item per `SetupStopwatch` frame budget, spreading the work across multiple frames.

---

#### TerrainPainterPatch

**Target:** `TerrainPainterKiri.PaintTerrain` (Prefix) + `TerrainPainter.UpdateSplatMapDots` state machine (Transpiler)

`UpdateSplatMapDots` calls `Object.FindObjectsOfType<PathPaint>()` once per terrain tile. `FindObjectsOfType` scans the entire scene hierarchy every call. On a large map with many tiles this is O(N_tiles × N_scene_objects).

The `PathPaintCache` static class caches the `PathPaint[]` for the lifetime of a single `PaintTerrain` session. The Transpiler replaces `FindObjectsOfType<PathPaint>()` in the state machine with a call to `PathPaintCache.Get()`. The cache is invalidated at the start of each `PaintTerrain` call via the Prefix.

---

#### RetryLimitPatch

**Target:** `DungeonGenerator.InnerGenerate` state machine + `GenerateBranchPaths` state machine (Transpiler)

DunGen has a retry-limit guard inside `InnerGenerate`:

```csharp
if (retryCount >= MaxAttemptCount && Application.isEditor) { /* fail */ }
```

The `&& Application.isEditor` condition means this guard **never fires in a real build**. When a tile cannot be placed and exceeds the retry limit, DunGen recurses through `InnerGenerate → GenerateMainPath → InnerGenerate → ...` until the call stack overflows — a `StackOverflowException` that silently kills the process. This bug is latent at the game's default map size but reliably triggered on larger maps.

The Transpiler replaces the `call Application::get_isEditor()` instruction with `ldc.i4.1` (constant `true`), making the retry limit fire correctly in all builds.

---

#### BuildingZonePatch (disabled)

This patch file is a stub. An earlier version deferred `DungeonGenerator.ProcessLocalProps` for buildings to spread `Instantiate` work across frames. It was removed because `ProcessLocalProps` selects which `RandomProp`/`LocalProp` mesh variant is active. While deferred, buildings were invisible at close range (all mesh variants disabled) but showed their pre-baked HLOD imposters at distance — the opposite of what the HLOD system intends.

The vanilla `SpawnBuildings` coroutine already yields once per building group, which is sufficient.

---

### SetupManager Sequence

Understanding the ordering is important for diagnosing timing-sensitive bugs:

```
CreateDungeon
  └─ DungeonGenerator.Generate
       └─ OnGenerationComplete → NavMeshAsyncPatch (async bake starts)
SetupKiriTiles          ← KiriTile.cachedMainBounds computed here
PlaceLootZones          ← LootZonePatch defers SpawnPrefab if enabled
PlaceBuildings
CalculateVisibility     ← MapVisibility.CreateMap runs (O(N²) ray casting)
SettingUpGlobalHLOD     ← KiriHLODManager.SetupGlobalTileAndData
                            builds NativeArrays + LinearQuadTree from cachedMainBounds
setup = true
BakeGiantessPathfinding ← GiantessPathfindingPatch replaces this if enabled
Player spawned
```

The HLOD quad-tree (`kiriTileLinearQuadTree`) is built from `KiriTileDOTS.boundsMin/Max`, which come from `KiriTile.cachedMainBounds` (set in `SetupKiriTiles`). These are **world-space bounds computed after DunGen places the tiles**. If tiles are placed outside the giantess terrain area (the old `RestrictDungeonToBounds = false` bug), the quad-tree contains bounds that never overlap the camera position at ground level.

---

### EnableOptimizations Flag

Four patches are gated behind `EnableOptimizations` (default `false`) because they trade some risk for faster loading:

| Patch | What it speeds up | Risk |
|---|---|---|
| `GiantessPathfindingPatch` | O(N²) path intersection on background thread | Behavioural changes to pathfinding graph |
| `LootZonePatch` | Deferred loot spawning across frames | Loot visible slightly later; ordering differences |
| `GrassPatch` | Grass density + SyncTransforms removal | Visual density reduction; SyncTransforms changes physics timing |
| `OOBGridCellSizePatch` | Constant OOB collider cell count at any scale | Fewer, larger collider cells outside dungeon |

The HLOD rendering fix (`TilePlacementBounds` scaling) is always active and does not depend on this flag. The navmesh async patch is also always active.

</details>
