using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // KiriHLODManager.SetupKiriTiles caches each KiriTile's world bounds and builds
    // the QuadTree from those snapshots — but SetupKiriTiles runs inside CreateDungeon,
    // BEFORE AttachDungeonToGround moves tiles to their final terrain-surface positions.
    //
    // SetupGlobalTileAndData (called much later, after attachment) queries that same
    // QuadTree to assign each KiriHLODGroup its visibilityBitMask:
    //
    //   foreach (KiriTile t in tileQuadTree.GetIntersections(new Bounds(pos, Vector3.one)))
    //       num |= 1UL << (int)t.tileBit;
    //
    // Because the QuadTree's internal node bounds AND each KiriTile's cachedMainBounds
    // reflect pre-attachment positions, GetIntersections returns empty for buildings
    // whose post-attachment Y landed outside the stale tile extents. Those buildings get
    // visibilityBitMask = 0 — the HLOD system never shows them, but their physics
    // colliders remain active, producing "invisible buildings with collision."
    //
    // Fix: re-cache every KiriTile's bounds and rebuild the QuadTree immediately before
    // SetupGlobalTileAndData reads from it.
    [HarmonyPatch(typeof(KiriHLODManager), nameof(KiriHLODManager.SetupGlobalTileAndData))]
    internal static class KiriTileBoundsRefreshPatch
    {
        static void Prefix(KiriHLODManager __instance)
        {
            if (!MapScalerConfig.EnableKiriTileBoundsRefresh.Value) return;
            if (__instance.tileList == null || __instance.tileList.Length == 0) return;

            foreach (KiriTile tile in __instance.tileList)
            {
                if (tile != null)
                    tile.CacheQuadTreeCheckBounds();
            }

            __instance.tileQuadTree = new QuadTree<KiriTile>(__instance.tileList);
            __instance.tileQuadTree.CacheQuadTreeBounds();

            Plugin.Log.LogDebug(
                $"[MapScaler] KiriTile bounds refreshed ({__instance.tileList.Length} tiles) " +
                "after AttachDungeonToGround.");
        }
    }

    // When TilePlacementBounds is scaled, tiles spread over a larger XZ area and some
    // buildings land outside every tile's 1×1×1 query box inside RebuildNativeArray,
    // leaving their visibilityBitMask = 0. Those buildings are invisible to the HLOD
    // renderer but keep their physics colliders active.
    //
    // Fix: after SetupGlobalTileAndData runs, find every KiriHLODGroup with
    // visibilityBitMask = 0, snap it to its nearest tile, and update both the
    // component field and the corresponding NativeArray entry so the runtime job
    // uses the correct bit.
    [HarmonyPatch(typeof(KiriHLODManager), nameof(KiriHLODManager.SetupGlobalTileAndData))]
    internal static class HLODOrphanBuildingFixPatch
    {
        static void Postfix(KiriHLODManager __instance)
        {
            if (__instance.tileList == null || __instance.tileList.Length == 0) return;
            if (__instance.globalData == null) return;
            if (!__instance.globalData.groupNativeArray.IsCreated) return;

            QuadTree<KiriTile> qt = __instance.tileQuadTree;
            if (qt == null) return;

            KiriHLODGroup[] groups = Object.FindObjectsOfType<KiriHLODGroup>(true);
            int fixed_count = 0;

            foreach (KiriHLODGroup group in groups)
            {
                // outOfBounds groups use a special sentinel bit — leave them alone.
                if (group.visibilityBitMask != 0 || group.outOfBounds) continue;

                Vector3 pos = group.transform.localToWorldMatrix.MultiplyPoint3x4(group.offset);
                KiriTile nearest = qt.GetClosetItemByEdge(pos, float.MaxValue);
                if (nearest == null) continue;

                ulong bit = 1UL << (int)nearest.tileBit;
                group.visibilityBitMask = bit;

                // Also update the NativeArray struct the runtime job reads.
                int idx = group.index;
                if (idx >= 0 && idx < __instance.globalData.groupNativeArray.Length)
                {
                    KiriHLODGroupStruct s = __instance.globalData.groupNativeArray[idx];
                    s.visiblityBitMask = bit;
                    __instance.globalData.groupNativeArray[idx] = s;
                }

                fixed_count++;
            }

            if (fixed_count > 0)
                Plugin.Log.LogInfo(
                    $"[MapScaler] HLOD orphan fix: assigned {fixed_count} building(s) " +
                    "with no tile to their nearest tile.");
        }
    }
}
