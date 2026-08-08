using System.Collections;
using System.Diagnostics;
using DunGen;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // ── DungeonGenerator generation phases ───────────────────────────────────
    // Logs every GenerationStatus transition with a running elapsed timer.
    internal static class DungeonGenerationLogger
    {
        private static readonly Stopwatch s_watch = new Stopwatch();

        internal static void OnStatusChanged(DungeonGenerator generator, GenerationStatus status)
        {
            if (!s_watch.IsRunning || status == GenerationStatus.NotStarted)
            {
                s_watch.Restart();
            }

            long ms = s_watch.ElapsedMilliseconds;

            // targetLength and retryCount are protected — use Traverse to read them.
            var t = Traverse.Create(generator);
            int targetLength = t.Field("targetLength").GetValue<int>();
            int retryCount   = t.Field("retryCount").GetValue<int>();

            switch (status)
            {
                case GenerationStatus.NotStarted:
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] Dungeon generation starting...");
                    break;
                case GenerationStatus.TileInjection:
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] Injecting tiles (target = {targetLength} rooms)...");
                    break;
                case GenerationStatus.MainPath:
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] Building main path ({targetLength} rooms)...");
                    break;
                case GenerationStatus.Branching:
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] Building branch paths...");
                    break;
                case GenerationStatus.PostProcessing:
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] Post-processing dungeon...");
                    break;
                case GenerationStatus.Complete:
                    int tileCount = generator.CurrentDungeon?.AllTiles?.Count ?? -1;
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] Dungeon generation COMPLETE — {tileCount} tiles placed.");
                    break;
                case GenerationStatus.Failed:
                    Plugin.Log.LogWarning($"[MapScaler] [{ms}ms] Dungeon generation FAILED (retry {retryCount}/{generator.MaxAttemptCount}).");
                    break;
            }
        }
    }

    // ── TerrainManager phases ─────────────────────────────────────────────────
    // These are IEnumerator methods — Prefix fires when the coroutine is created,
    // not when it finishes. End-to-end timing comes from the SetupManager monitor.
    [HarmonyPatch(typeof(TerrainManager), nameof(TerrainManager.UpdateMesh))]
    internal static class TerrainUpdateMeshLog
    {
        static void Prefix() =>
            Plugin.Log.LogInfo("[MapScaler] Terrain: OOB mesh coroutine launched...");
    }

    [HarmonyPatch(typeof(TerrainManager), nameof(TerrainManager.CreateVisuals))]
    internal static class TerrainCreateVisualsLog
    {
        static void Prefix() =>
            Plugin.Log.LogInfo("[MapScaler] Terrain: tree/visual placement coroutine launched...");
    }

    [HarmonyPatch(typeof(TerrainManager), nameof(TerrainManager.CreateColliders))]
    internal static class TerrainCreateCollidersLog
    {
        static void Prefix(UnityEngine.Bounds worldBounds) =>
            Plugin.Log.LogInfo(
                $"[MapScaler] Terrain: OOB collider coroutine launched " +
                $"(world envelope {worldBounds.size.x:F0}×{worldBounds.size.z:F0} units)...");
    }

    // ── MapVisibility phase ───────────────────────────────────────────────────
    [HarmonyPatch(typeof(MapVisibility), nameof(MapVisibility.CreateMap))]
    internal static class MapVisibilityCreateMapLog
    {
        private static readonly Stopwatch s_watch = new Stopwatch();

        static void Prefix()
        {
            s_watch.Restart();
            int tileCount = KiriHLODManager.Instance?.tileList?.Length ?? -1;
            long pairs = tileCount > 0 ? (long)tileCount * tileCount : -1;
            Plugin.Log.LogInfo(
                $"[MapScaler] Visibility: O(N²) CreateMap for {tileCount} tiles " +
                $"(up to {pairs:N0} pair-checks).");
        }

        // Postfix on an IEnumerator method fires when the coroutine object is
        // created, not when it finishes. Real completion is logged in
        // KiriHLODLog.Postfix via SetupGlobalTileAndData.
        static void Postfix() =>
            Plugin.Log.LogInfo("[MapScaler] Visibility: coroutine launched (running in background)...");

        internal static long ElapsedMs => s_watch.ElapsedMilliseconds;
    }

    // ── KiriHLOD phase ────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(KiriHLODManager), nameof(KiriHLODManager.SetupGlobalTileAndData))]
    internal static class KiriHLODLog
    {
        private static readonly Stopwatch s_watch = new Stopwatch();
        static void Prefix() { s_watch.Restart(); Plugin.Log.LogInfo("[MapScaler] HLOD: SetupGlobalTileAndData starting..."); }
        // Postfix is also used by MapVisibilityCullPatch — order within the same
        // [HarmonyPatch] class doesn't matter since they're in separate classes.
        static void Postfix() =>
            Plugin.Log.LogInfo(
                $"[MapScaler] HLOD: SetupGlobalTileAndData done ({s_watch.ElapsedMilliseconds}ms). " +
                $"Visibility calc total ~{MapVisibilityCreateMapLog.ElapsedMs}ms.");
    }

    // ── SetupManager state monitor ────────────────────────────────────────────
    // SetupManager.state is a plain public field so it can't be patched directly.
    // Instead the Plugin starts a polling coroutine that logs every state change.
    internal static class SetupManagerMonitor
    {
        private static SetupManager.State s_lastState = (SetupManager.State)(-1);
        private static readonly Stopwatch s_watch = new Stopwatch();

        internal static IEnumerator PollState()
        {
            // Wait until SetupManager exists.
            while (SetupManager.Instance == null)
                yield return null;

            s_watch.Restart();
            s_lastState = SetupManager.Instance.state;
            Plugin.Log.LogInfo("[MapScaler] SetupManager detected — monitoring state transitions.");

            while (true)
            {
                SetupManager.State current = SetupManager.Instance.state;
                if (current != s_lastState)
                {
                    long ms = s_watch.ElapsedMilliseconds;
                    Plugin.Log.LogInfo($"[MapScaler] [{ms}ms] SetupManager → {current}");
                    s_lastState = current;

                    if (current == SetupManager.State.Complete)
                    {
                        Plugin.Log.LogInfo($"[MapScaler] ✓ Map setup COMPLETE in {ms}ms total.");
                        yield break;
                    }
                }
                yield return null; // check every frame
            }
        }
    }
}
