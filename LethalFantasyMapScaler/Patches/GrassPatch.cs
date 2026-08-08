using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DunGen;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // GrassRenderer.ProcessGrass has two performance problems:
    //
    //  1. Physics.SyncTransforms() at the start forces a full physics transform
    //     sync once per tile. By the time grass is placed (after dungeon generation
    //     is complete and terrain is set up), transforms are stable — the sync is
    //     a no-op in practice but still costs a physics engine round-trip.
    //     Fix: remove the call via Transpiler.
    //
    //  2. The inner loop is O(N²) in tile size with spawnCountPerMeter as the
    //     density parameter: a 200×200 tile at 2/m = 160 000 iterations, each
    //     calling GetFloorColor up to 5 times. Reducing density cuts iterations
    //     quadratically: 0.5 density = 4× fewer iterations.
    //     Fix: Prefix scales spawnCountPerMeter by GrassDensity; Postfix restores it.
    [HarmonyPatch(typeof(GrassRenderer), nameof(GrassRenderer.ProcessGrass))]
    internal static class GrassPatch
    {
        static void Prefix(GrassRenderer __instance, out float __state)
        {
            __state = __instance.spawnCountPerMeter;
            if (!MapScalerConfig.EnableOptimizations.Value) return;
            float density = MapScalerConfig.GrassDensity.Value;
            __instance.spawnCountPerMeter *= density;
            if (density < 1f)
                Plugin.Log.LogDebug(
                    $"[MapScaler] Grass: spawnCountPerMeter {__state:F2} → {__instance.spawnCountPerMeter:F2} (×{density:F2})");
        }

        static void Postfix(GrassRenderer __instance, float __state)
        {
            __instance.spawnCountPerMeter = __state;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!MapScalerConfig.EnableOptimizations.Value)
            {
                foreach (var instr in instructions) yield return instr;
                yield break;
            }

            MethodInfo syncTransforms = typeof(UnityEngine.Physics)
                .GetMethod("SyncTransforms", BindingFlags.Public | BindingFlags.Static);

            bool patched = false;
            foreach (var instr in instructions)
            {
                if (!patched
                    && instr.opcode == OpCodes.Call
                    && instr.operand is MethodInfo mi
                    && mi == syncTransforms)
                {
                    // Replace the call with a no-op — transforms are stable at
                    // this point (dungeon + terrain already committed).
                    yield return new CodeInstruction(OpCodes.Nop);
                    patched = true;
                    Plugin.Log.LogDebug("[MapScaler] GrassRenderer.ProcessGrass: Physics.SyncTransforms() removed.");
                    continue;
                }
                yield return instr;
            }

            if (!patched)
                Plugin.Log.LogWarning(
                    "[MapScaler] GrassRenderer.ProcessGrass: Physics.SyncTransforms() not found — not removed.");
        }
    }
}
