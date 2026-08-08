using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // TerrainManager.CreateColliders receives the world-space Bounds that controls
    // where out-of-bounds ground colliders are placed (via EmptyGridFinder).
    // The caller builds this Bounds as  Vector3.one * 512f * 1.5f = 768 units.
    // We scale it before the parameter is captured into the coroutine state machine.
    //
    // TerrainManager.CreateVisuals receives the same Bounds but does NOT use it —
    // tree placement is driven by the outline polygon, so no patch is needed there.
    [HarmonyPatch(typeof(TerrainManager), nameof(TerrainManager.CreateColliders))]
    internal static class WorldBoundaryPatch
    {
        static void Prefix(ref Bounds worldBounds)
        {
            if (!MapScalerConfig.ScaleWorldBoundary.Value) return;

            float mult = MapScalerConfig.MapSizeMultiplier.Value;
            worldBounds = new Bounds(worldBounds.center, worldBounds.size * mult);

            Plugin.Log.LogDebug(
                $"[MapScaler] WorldBoundary scaled to {worldBounds.size.x:F0} units (×{mult})");
        }
    }
}
