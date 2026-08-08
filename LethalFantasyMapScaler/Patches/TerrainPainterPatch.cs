using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // Cache PathPaint[] across tiles in a single PaintTerrain session.
    // TerrainPainter.UpdateSplatMapDots calls Object.FindObjectsOfType<PathPaint>()
    // once per tile — O(N_scene) per call. PathPaints don't change during painting,
    // so one scan per session is sufficient.
    internal static class PathPaintCache
    {
        static PathPaint[] s_cache;
        static bool s_valid;

        internal static void Invalidate() => s_valid = false;

        public static PathPaint[] Get()
        {
            if (!s_valid)
            {
                s_cache = UnityEngine.Object.FindObjectsOfType<PathPaint>();
                s_valid = true;
                Plugin.Log.LogDebug($"[MapScaler] PathPaint cache: {s_cache.Length} paints found.");
            }
            return s_cache;
        }
    }

    // Invalidate the cache each time a new PaintTerrain coroutine is created.
    [HarmonyPatch(typeof(TerrainPainterKiri), nameof(TerrainPainterKiri.PaintTerrain))]
    internal static class PathPaintCacheInvalidator
    {
        static void Prefix()
        {
            if (MapScalerConfig.EnablePathPaintCache.Value)
                PathPaintCache.Invalidate();
        }
    }

    // Transpile UpdateSplatMapDots state machine to call PathPaintCache.Get()
    // instead of FindObjectsOfType<PathPaint>() on every tile.
    [HarmonyPatch]
    internal static class PathPaintCacheTranspiler
    {
        static MethodBase TargetMethod()
        {
            Type sm = typeof(TerrainPainter)
                .GetNestedTypes(BindingFlags.NonPublic)
                .FirstOrDefault(t => t.Name.Contains("UpdateSplatMapDots"));

            if (sm == null)
            {
                Plugin.Log.LogWarning(
                    "[MapScaler] Could not find UpdateSplatMapDots state machine — " +
                    "PathPaint scan will NOT be cached.");
                return null;
            }

            return sm.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!MapScalerConfig.EnablePathPaintCache.Value)
            {
                foreach (var instr in instructions) yield return instr;
                yield break;
            }

            MethodInfo cacheGet = AccessTools.Method(typeof(PathPaintCache), nameof(PathPaintCache.Get));
            bool patched = false;

            foreach (var instr in instructions)
            {
                if (!patched
                    && instr.opcode == OpCodes.Call
                    && instr.operand is MethodInfo mi
                    && mi.Name == "FindObjectsOfType"
                    && mi.IsGenericMethod
                    && mi.GetGenericArguments().Length == 1
                    && mi.GetGenericArguments()[0] == typeof(PathPaint))
                {
                    yield return new CodeInstruction(OpCodes.Call, cacheGet);
                    patched = true;
                    Plugin.Log.LogDebug("[MapScaler] UpdateSplatMapDots: FindObjectsOfType<PathPaint> → PathPaintCache.");
                    continue;
                }
                yield return instr;
            }

            if (!patched)
                Plugin.Log.LogWarning(
                    "[MapScaler] UpdateSplatMapDots: FindObjectsOfType<PathPaint> not found — " +
                    "cache NOT applied.");
        }
    }
}
