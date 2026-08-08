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

        // When true, enables performance optimizations: O(N²) pathfinding on a
        // background thread, loot deferral, grass density scaling, and OOB grid
        // scaling. WARNING: enabling this has been observed to conflict with HLOD
        // rendering and cause dungeon tiles to be invisible at ground level.
        // Leave disabled unless the loading time is unacceptable.
        public static ConfigEntry<bool> EnableOptimizations { get; private set; }

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
                    "0.25 = quarter density, ~16× faster. Lower values reduce visual coverage.",
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

            EnableOptimizations = cfg.Bind(
                "Performance",
                "EnableOptimizations",
                false,
                "Enable performance optimizations: O(N²) pathfinding on a background thread, " +
                "deferred loot spawning, grass density scaling, and OOB ground-collider grid scaling. " +
                "WARNING: enabling this may conflict with HLOD rendering and cause dungeon tiles " +
                "to be invisible at ground level. Leave false for stable rendering."
            );
        }
    }
}
