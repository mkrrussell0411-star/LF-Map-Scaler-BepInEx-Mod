using BepInEx.Configuration;

namespace LethalFantasyMapScaler
{
    internal static class MapScalerConfig
    {
        // Scales DungeonGenerator.LengthMultiplier — controls main path room count.
        // 1.0 = default (5-10 rooms), 2.0 = 10-20 rooms, 0.5 = 3-5 rooms.
        public static ConfigEntry<float> MapSizeMultiplier { get; private set; }

        // Scales BranchingDepth and BranchCount on every DungeonArchetype.
        // 1.0 = default, 2.0 = twice as many/deeper branches.
        public static ConfigEntry<float> BranchMultiplier { get; private set; }

        // When true, also scales the out-of-bounds terrain boundary so it
        // stays large enough to wrap the bigger dungeon.
        public static ConfigEntry<bool> ScaleWorldBoundary { get; private set; }

        // Milliseconds each setup coroutine (terrain, visibility, buildings) is
        // allowed to run per frame. Default 33 = 30fps during loading.
        // Raise to 100-500 to load faster; the game will hitch more during the
        // loading screen but finish sooner.
        public static ConfigEntry<int> LoadingFrameBudgetMs { get; private set; }

        // Maximum number of room-placement retries before the dungeon generator
        // gives up. Larger maps may need more attempts to place rooms without
        // collision. Default is the game's own default of 20.
        public static ConfigEntry<int> MaxGenerationAttempts { get; private set; }

        // Scales GrassRenderer.spawnCountPerMeter before the grass loop runs.
        // Cuts iterations quadratically: 0.5 = 4× faster, 0.25 = 16× faster.
        // Visual impact is roughly proportional; 0.5 is a good balance.
        public static ConfigEntry<float> GrassDensity { get; private set; }

        // Tiles farther apart than this (in Unity units) are skipped in the
        // minimap visibility check. Tightening this cuts O(N²) cost dramatically
        // on large maps. Set to 0 to disable the optimisation (vanilla behaviour).
        public static ConfigEntry<float> VisibilityMaxDistance { get; private set; }

        // ── Dungeon generation toggles (default on) ─────────────────────────────

        // Apply MapSizeMultiplier to DungeonGenerator.LengthMultiplier (main path length).
        public static ConfigEntry<bool> EnableLengthScaling { get; private set; }

        // Apply BranchMultiplier to each archetype's BranchingDepth and BranchCount.
        public static ConfigEntry<bool> EnableBranchScaling { get; private set; }

        // Scale TilePlacementBounds by MapSizeMultiplier so the larger dungeon fits inside
        // the giantess HLOD XZ region. Disabling this breaks ground-level tile rendering.
        public static ConfigEntry<bool> EnableTilePlacementBoundsScaling { get; private set; }

        // Override DungeonGenerator.MaxAttemptCount with MaxGenerationAttempts config value.
        public static ConfigEntry<bool> EnableMaxAttemptsOverride { get; private set; }

        // Override SetupStopwatch.MINIMUM_FRAME_MILLISECONDS with LoadingFrameBudgetMs.
        public static ConfigEntry<bool> EnableFrameBudgetOverride { get; private set; }

        // ── Optimization toggles (default off — may affect rendering) ──────────

        // Replace O(N³) giantess pathfinding LINQ loop with O(N²) + background thread.
        public static ConfigEntry<bool> EnableGiantessPathfinding { get; private set; }

        // Defer LootZone SpawnPrefab calls out of SetupZones to spread load across frames.
        public static ConfigEntry<bool> EnableLootZoneDefer { get; private set; }

        // Scale GrassRenderer.spawnCountPerMeter by GrassDensity before the grass loop.
        public static ConfigEntry<bool> EnableGrassReduction { get; private set; }

        // Scale EmptyGridFinder OOB ground-collider cell size with the map multiplier.
        public static ConfigEntry<bool> EnableOOBGridScaling { get; private set; }

        // ── Patch toggles (default on — bug fixes / correctness) ────────────────

        // Run NavMesh bake asynchronously instead of blocking the main thread.
        public static ConfigEntry<bool> EnableNavMeshAsync { get; private set; }

        // Fix DunGen InnerGenerate retry limit (Application.isEditor always false in builds)
        // that causes StackOverflow on large maps.
        public static ConfigEntry<bool> EnableRetryLimitFix { get; private set; }

        // Cache PathPaint FindObjectsOfType scan once per session instead of per tile.
        public static ConfigEntry<bool> EnablePathPaintCache { get; private set; }

        // Scale MapVisibility PIP ray from 1000 units to match the map multiplier.
        public static ConfigEntry<bool> EnableMapVisibilityRayScale { get; private set; }

        // Re-cache KiriTile world bounds after AttachDungeonToGround so the HLOD
        // building visibility lookup uses post-attachment positions.
        public static ConfigEntry<bool> EnableKiriTileBoundsRefresh { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
            MapSizeMultiplier = cfg.Bind(
                "Map",
                "MapSizeMultiplier",
                1.5f,
                new ConfigDescription(
                    "Multiplier for the main dungeon path length. " +
                    "1.0 = default game size. 2.0 = twice as many rooms on the main path.",
                    new AcceptableValueRange<float>(0.25f, 10f)
                )
            );

            BranchMultiplier = cfg.Bind(
                "Map",
                "BranchMultiplier",
                1.0f,
                new ConfigDescription(
                    "Multiplier for branch depth and branch count on each archetype. " +
                    "1.0 = default. 2.0 = twice as many/deeper side branches.",
                    new AcceptableValueRange<float>(0.25f, 5f)
                )
            );

            ScaleWorldBoundary = cfg.Bind(
                "Map",
                "ScaleWorldBoundary",
                true,
                "Scale the out-of-bounds terrain boundary along with the map. " +
                "Disable only if you see terrain/collision issues."
            );

            LoadingFrameBudgetMs = cfg.Bind(
                "Performance",
                "LoadingFrameBudgetMs",
                500,
                new ConfigDescription(
                    "How many milliseconds each setup coroutine may run per frame during map " +
                    "generation. Default game value is 33 (30fps). Higher = faster loading but " +
                    "the game freezes more during the loading screen. " +
                    "Set to 0 for no limit (fastest possible, hard freeze until done).",
                    new AcceptableValueRange<int>(0, 10000)
                )
            );

            MaxGenerationAttempts = cfg.Bind(
                "Performance",
                "MaxGenerationAttempts",
                40,
                new ConfigDescription(
                    "How many times the dungeon generator may retry room placement before " +
                    "giving up. Larger maps need more attempts. Game default is 20.",
                    new AcceptableValueRange<int>(5, 200)
                )
            );

            GrassDensity = cfg.Bind(
                "Performance",
                "GrassDensity",
                1.0f,
                new ConfigDescription(
                    "Fraction of the normal grass density to spawn per tile. " +
                    "1.0 = vanilla density. 0.5 = half density, ~4× faster grass placement. " +
                    "0.25 = quarter density, ~16× faster. Lower values reduce visual coverage. " +
                    "Only active when EnableGrassReduction = true.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)
                )
            );

            VisibilityMaxDistance = cfg.Bind(
                "Performance",
                "VisibilityMaxDistance",
                0f,
                new ConfigDescription(
                    "Tiles whose centres are farther apart than this (Unity units) are skipped " +
                    "in the minimap visibility calculation. Cuts the O(N²) cost on large maps. " +
                    "0 = disabled (vanilla behaviour, check all tile pairs). " +
                    "Try 300-600 for large maps: tiles that far apart are never visible anyway.",
                    new AcceptableValueRange<float>(0f, 5000f)
                )
            );

            // ── Dungeon generation toggles ──────────────────────────────────────

            EnableLengthScaling = cfg.Bind(
                "DungeonGen",
                "EnableLengthScaling",
                true,
                "Apply MapSizeMultiplier to DungeonGenerator.LengthMultiplier (main path room count). " +
                "Disable to leave LengthMultiplier at its default value."
            );

            EnableBranchScaling = cfg.Bind(
                "DungeonGen",
                "EnableBranchScaling",
                true,
                "Apply BranchMultiplier to each dungeon archetype's BranchingDepth and BranchCount. " +
                "Disable to leave branch settings at their default values."
            );

            EnableTilePlacementBoundsScaling = cfg.Bind(
                "DungeonGen",
                "EnableTilePlacementBoundsScaling",
                true,
                "Scale TilePlacementBounds by MapSizeMultiplier so the larger dungeon fits inside " +
                "the HLOD XZ region. Disabling this will break ground-level tile rendering at scale."
            );

            EnableMaxAttemptsOverride = cfg.Bind(
                "DungeonGen",
                "EnableMaxAttemptsOverride",
                true,
                "Override DungeonGenerator.MaxAttemptCount with the MaxGenerationAttempts config value. " +
                "Disable to use DunGen's built-in default (20)."
            );

            EnableFrameBudgetOverride = cfg.Bind(
                "DungeonGen",
                "EnableFrameBudgetOverride",
                true,
                "Override SetupStopwatch frame budget with LoadingFrameBudgetMs. " +
                "Disable to use the game's default 33ms budget (30fps during loading)."
            );

            // ── Optimization toggles ────────────────────────────────────────────

            EnableGiantessPathfinding = cfg.Bind(
                "Optimizations",
                "EnableGiantessPathfinding",
                false,
                "Replace the O(N³) giantess pathfinding LINQ loop with O(N²) + a background thread. " +
                "Safe to enable on large maps where pathfinding setup is slow. Requires restart."
            );

            EnableLootZoneDefer = cfg.Bind(
                "Optimizations",
                "EnableLootZoneDefer",
                false,
                "Spread LootZone SpawnPrefab calls across multiple frames instead of executing " +
                "all in one frame inside SetupZones. Reduces hitching during loot setup."
            );

            EnableGrassReduction = cfg.Bind(
                "Optimizations",
                "EnableGrassReduction",
                false,
                "Scale GrassRenderer.spawnCountPerMeter by GrassDensity before the grass loop runs. " +
                "Reduces grass spawn iterations quadratically. Visual impact scales with the reduction."
            );

            EnableOOBGridScaling = cfg.Bind(
                "Optimizations",
                "EnableOOBGridScaling",
                false,
                "Scale the EmptyGridFinder OOB ground-collider cell size by MapSizeMultiplier so " +
                "the number of OOB collider cells stays constant regardless of map scale."
            );

            // ── Patch toggles ───────────────────────────────────────────────────

            EnableNavMeshAsync = cfg.Bind(
                "Patches",
                "EnableNavMeshAsync",
                true,
                "Run the NavMesh bake asynchronously on a worker thread instead of blocking " +
                "the main thread. Disable to use the vanilla synchronous BuildNavMesh()."
            );

            EnableRetryLimitFix = cfg.Bind(
                "Patches",
                "EnableRetryLimitFix",
                true,
                "Fix DunGen InnerGenerate retry limit: the vanilla guard is ' && Application.isEditor' " +
                "which never fires in a build, causing StackOverflow on large maps. Replaces the " +
                "check with a constant true so the retry limit works correctly. Requires restart."
            );

            EnablePathPaintCache = cfg.Bind(
                "Patches",
                "EnablePathPaintCache",
                true,
                "Cache the PathPaint FindObjectsOfType scan once per PaintTerrain session instead " +
                "of repeating it for every tile. Eliminates O(N_scene × N_tiles) overhead. Requires restart."
            );

            EnableMapVisibilityRayScale = cfg.Bind(
                "Patches",
                "EnableMapVisibilityRayScale",
                false,
                "Scale the MapVisibility point-in-polygon ray from 1000 units to " +
                "1000 × MapSizeMultiplier × 1.2. WARNING: the 1000f constant is calibrated to " +
                "the doorway-segment polygon geometry. Scaling it causes the ray to exit and " +
                "re-enter concave sections of the visibility polygon, producing even crossing " +
                "counts that mark visible tiles as not visible, hiding their buildings. Only " +
                "enable for MapSizeMultiplier values large enough that 1000 units no longer " +
                "exits the polygon (typically > 3× or 4×)."
            );

            EnableKiriTileBoundsRefresh = cfg.Bind(
                "Patches",
                "EnableKiriTileBoundsRefresh",
                true,
                "Re-cache KiriTile world bounds after AttachDungeonToGround moves tiles to their " +
                "final terrain positions. Fixes buildings that are invisible but still have collision " +
                "because the HLOD quadtree used stale pre-attachment positions. Requires restart."
            );
        }
    }
}
