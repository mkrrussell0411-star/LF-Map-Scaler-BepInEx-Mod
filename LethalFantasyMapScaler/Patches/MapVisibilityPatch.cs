using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // ── Patch 1: scale the PIP ray length ────────────────────────────────────
    // The vanilla CreateMap always runs; the BFS replacement was removed because
    // setting visibileTileBit = 0xFFFF for all tiles was suspected to be HLOD-
    // incompatible. The real root cause was RestrictDungeonToBounds = false
    // (fixed in DungeonGeneratorPatch), but leaving BFS disabled is still safer.
    //
    // Scales the 1000f point-in-polygon ray so it exits the visibility polygon
    // even on large maps.
    [HarmonyPatch]
    internal static class MapVisibilityRayPatch
    {
        static MethodBase TargetMethod()
        {
            Type stateMachine = typeof(MapVisibility)
                .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(t => t.Name.StartsWith("<CreateMap>d__", StringComparison.Ordinal));

            if (stateMachine == null)
            {
                Plugin.Log.LogWarning("[MapScaler] Could not find MapVisibility.CreateMap state machine — PIP ray patch skipped.");
                return null;
            }

            return stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static float GetScaledRayLength() =>
            1000f * MapScalerConfig.MapSizeMultiplier.Value * 1.2f;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Check config in the transpiler body — never return null from TargetMethod
            // to disable a patch; Harmony 2.x treats null as an error, not a skip.
            if (!MapScalerConfig.EnableMapVisibilityRayScale.Value)
            {
                foreach (var instr in instructions) yield return instr;
                yield break;
            }

            MethodInfo getter = AccessTools.Method(typeof(MapVisibilityRayPatch), nameof(GetScaledRayLength));
            bool patched = false;

            foreach (var instr in instructions)
            {
                if (!patched && instr.opcode == OpCodes.Ldc_R4 && instr.operand is float f && Math.Abs(f - 1000f) < 0.01f)
                {
                    yield return new CodeInstruction(OpCodes.Call, getter);
                    patched = true;
                    Plugin.Log.LogDebug("[MapScaler] MapVisibility PIP ray length patched.");
                    continue;
                }
                yield return instr;
            }

            if (!patched)
                Plugin.Log.LogWarning("[MapScaler] MapVisibility transpiler: 1000f not found — PIP ray NOT scaled.");
        }
    }

    // ── Patch 2: distance-cull visibility lists after CreateMap finishes ─────
    [HarmonyPatch(typeof(KiriHLODManager), nameof(KiriHLODManager.SetupGlobalTileAndData))]
    internal static class MapVisibilityCullPatch
    {
        static void Postfix()
        {
            float maxDist = MapScalerConfig.VisibilityMaxDistance.Value;
            if (maxDist <= 0f) return;

            KiriTile[] tiles = KiriHLODManager.Instance.tileList;
            if (tiles == null || tiles.Length == 0) return;

            float maxDistSq = maxDist * maxDist;
            int totalRemoved = 0;

            foreach (KiriTile tile in tiles)
            {
                if (tile.visibleTiles == null || tile.visibleTiles.Count == 0) continue;
                Vector3 center = tile.parentTile.Bounds.center;

                for (int i = tile.visibleTiles.Count - 1; i >= 0; i--)
                {
                    KiriTile other = tile.visibleTiles[i];
                    if (other == tile) continue;
                    if ((other.parentTile.Bounds.center - center).sqrMagnitude > maxDistSq)
                    {
                        tile.visibleTiles.RemoveAt(i);
                        totalRemoved++;
                    }
                }
            }

            Plugin.Log.LogInfo($"[MapScaler] Visibility cull: removed {totalRemoved} distant tile pairs (max {maxDist} units).");
        }
    }

    // ── Patch 3: scale OOB ground collider grid cell size ────────────────────
    // EmptyGridFinder places one ground collider per 60×60 unit cell outside the
    // dungeon. At 1.5× world size that's (1152/60)² ≈ 369 cells vs 164 vanilla.
    // Scaling cellSize linearly with the map multiplier keeps the cell COUNT
    // (and thus loading time) identical to vanilla at any multiplier.
    [HarmonyPatch(typeof(EmptyGridFinder), nameof(EmptyGridFinder.GetEmptyCellCenters))]
    internal static class OOBGridCellSizePatch
    {
        static void Prefix(ref float cellSize)
        {
            if (!MapScalerConfig.EnableOOBGridScaling.Value) return;
            float mult = MapScalerConfig.MapSizeMultiplier.Value;
            float scaled = cellSize * mult;
            Plugin.Log.LogDebug($"[MapScaler] OOB grid cellSize: {cellSize} → {scaled:F1} (×{mult})");
            cellSize = scaled;
        }
    }
}
