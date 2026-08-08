using System;
using System.Collections;
using System.Diagnostics;
using HarmonyLib;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace LethalFantasyMapScaler.Patches
{
    // NavMeshBakeOnDungeonFinish.DungeonGenerator_OnGenerationComplete calls
    // navMeshSurface.BuildNavMesh() synchronously on the main thread when dungeon
    // generation completes. This blocks the frame for the entire navmesh bake.
    //
    // Fix: replace the synchronous call with an async coroutine. All three
    // relevant NavMeshSurface members (navMeshData, AddData, UpdateNavMesh) are
    // public in the version shipped with this game — no reflection required.
    // If the async path fails for any reason, we fall back to BuildNavMesh().
    [HarmonyPatch(typeof(NavMeshBakeOnDungeonFinish), "DungeonGenerator_OnGenerationComplete")]
    internal static class NavMeshAsyncPatch
    {
        static bool Prefix(NavMeshBakeOnDungeonFinish __instance)
        {
            __instance.StartCoroutine(AsyncBake(__instance.navMeshSurface));
            return false; // skip the synchronous BuildNavMesh()
        }

        static IEnumerator AsyncBake(NavMeshSurface surface)
        {
            var sw = Stopwatch.StartNew();
            Plugin.Log.LogInfo("[MapScaler] NavMesh: async bake starting...");

            // Setup phase — no yield allowed inside try/catch in C#, so we
            // do the setup synchronously, capture the AsyncOperation, then poll.
            AsyncOperation op = null;
            try
            {
                // If no data exists yet, create it and register it with the
                // NavMesh before starting the async bake so pathfinding has a
                // valid (initially empty) data object from the first frame.
                if (surface.navMeshData == null)
                {
                    surface.navMeshData = new NavMeshData(surface.agentTypeID);
                    surface.AddData();
                }

                op = surface.UpdateNavMesh(surface.navMeshData);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MapScaler] NavMesh: async setup failed: {e.Message}");
            }

            if (op != null)
            {
                while (!op.isDone)
                    yield return null;

                Plugin.Log.LogInfo($"[MapScaler] NavMesh: async bake done ({sw.ElapsedMilliseconds}ms).");
                yield break;
            }

            // Fallback: sync bake.
            if (op == null)
                Plugin.Log.LogWarning("[MapScaler] NavMesh: UpdateNavMesh returned null — falling back to sync bake.");

            surface.BuildNavMesh();
            Plugin.Log.LogInfo($"[MapScaler] NavMesh: sync bake done ({sw.ElapsedMilliseconds}ms).");
        }
    }
}
