using System;
using System.Collections.Generic;
using DunGen;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Generate))]
    internal static class DungeonGeneratorPatch
    {
        // Stores the unmodified IntRange values so we can always scale from
        // originals regardless of how many times Generate() is retried.
        private static readonly Dictionary<DungeonArchetype, ArchetypeSnapshot> s_originals = new();
        private static readonly Dictionary<DungeonGenerator, (bool restrict, Bounds bounds)> s_originalBounds = new();

        static void Prefix(DungeonGenerator __instance)
        {
            float mult = MapScalerConfig.MapSizeMultiplier.Value;
            __instance.LengthMultiplier = mult;
            __instance.MaxAttemptCount  = MapScalerConfig.MaxGenerationAttempts.Value;

            // Scale TilePlacementBounds proportionally to the dungeon size so the
            // larger dungeon fits within a proportionally larger spatial region.
            // Keeping RestrictDungeonToBounds = true (its original value) is critical:
            // the HLOD VisibilityJob (step 3) finds tiles by querying the LinearQuadTree
            // at the camera's XZ position. If the dungeon is placed at arbitrary XZ
            // locations (as happens when RestrictDungeonToBounds = false), the camera
            // is never XZ-over any tile and nothing renders at ground level.
            if (!s_originalBounds.ContainsKey(__instance))
                s_originalBounds[__instance] = (__instance.RestrictDungeonToBounds, __instance.TilePlacementBounds);

            Bounds origBounds = s_originalBounds[__instance].bounds;
            __instance.TilePlacementBounds = new Bounds(origBounds.center, origBounds.size * mult);

            Plugin.Log.LogDebug(
                $"[MapScaler] LengthMultiplier={__instance.LengthMultiplier}, " +
                $"MaxAttempts={__instance.MaxAttemptCount}, " +
                $"RestrictToBounds={__instance.RestrictDungeonToBounds}, " +
                $"PlacementBounds={__instance.TilePlacementBounds.size} (×{mult})");

            float branchMult = MapScalerConfig.BranchMultiplier.Value;
            if (__instance.DungeonFlow == null) return;

            DungeonArchetype[] archetypes = __instance.DungeonFlow.GetUsedArchetypes();
            foreach (DungeonArchetype arch in archetypes)
            {
                if (arch == null) continue;

                // Save originals on first encounter; restore from them on retries.
                if (!s_originals.TryGetValue(arch, out ArchetypeSnapshot snap))
                {
                    snap = new ArchetypeSnapshot(arch);
                    s_originals[arch] = snap;
                }
                else
                {
                    snap.RestoreTo(arch);
                }

                // Scale from the saved originals.
                arch.BranchingDepth.Min = Mathf.Max(1, Mathf.RoundToInt(snap.DepthMin * branchMult));
                arch.BranchingDepth.Max = Mathf.Max(1, Mathf.RoundToInt(snap.DepthMax * branchMult));
                arch.BranchCount.Min   = Mathf.RoundToInt(snap.CountMin * branchMult);
                arch.BranchCount.Max   = Mathf.RoundToInt(snap.CountMax * branchMult);

                Plugin.Log.LogDebug(
                    $"[MapScaler] {arch.name}: depth [{arch.BranchingDepth.Min}-{arch.BranchingDepth.Max}], " +
                    $"count [{arch.BranchCount.Min}-{arch.BranchCount.Max}]");
            }
        }

        // Called by Plugin.Awake via DungeonGenerator.OnAnyDungeonGenerationStatusChanged.
        // Restores archetype values to their originals when the dungeon finishes
        // (Complete or Failed) so re-loading a level without the mod works correctly.
        internal static void OnStatusChanged(DungeonGenerator generator, GenerationStatus status)
        {
            if (status != GenerationStatus.Complete && status != GenerationStatus.Failed) return;

            // Restore archetype branch values.
            if (generator.DungeonFlow != null)
            {
                foreach (DungeonArchetype arch in generator.DungeonFlow.GetUsedArchetypes())
                {
                    if (arch == null) continue;
                    if (s_originals.TryGetValue(arch, out ArchetypeSnapshot snap))
                        snap.RestoreTo(arch);
                }
            }

            // Restore RestrictDungeonToBounds and TilePlacementBounds.
            if (s_originalBounds.TryGetValue(generator, out var saved))
            {
                generator.RestrictDungeonToBounds = saved.restrict;
                generator.TilePlacementBounds     = saved.bounds;
            }
        }

        private readonly struct ArchetypeSnapshot
        {
            public readonly int DepthMin, DepthMax, CountMin, CountMax;

            public ArchetypeSnapshot(DungeonArchetype arch)
            {
                DepthMin = arch.BranchingDepth.Min;
                DepthMax = arch.BranchingDepth.Max;
                CountMin = arch.BranchCount.Min;
                CountMax = arch.BranchCount.Max;
            }

            public void RestoreTo(DungeonArchetype arch)
            {
                arch.BranchingDepth.Min = DepthMin;
                arch.BranchingDepth.Max = DepthMax;
                arch.BranchCount.Min    = CountMin;
                arch.BranchCount.Max    = CountMax;
            }
        }
    }
}
