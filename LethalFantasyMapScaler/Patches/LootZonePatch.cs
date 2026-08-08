using System.Collections;
using System.Collections.Generic;
using DunGen;
using HarmonyLib;

namespace LethalFantasyMapScaler.Patches
{
    // LootZoneManager.SetupZones is called synchronously from the PlaceLootZones
    // coroutine. Inside it, every non-selected NodeTag fires SpawnPrefab(), which
    // instantiates a prefab and calls DungeonGenerator.ProcessLocalProps on it.
    // On large maps (many LootZone/LootZoneNoMain nodes) this is many Instantiate
    // + ProcessLocalProps calls all in one frame.
    //
    // Fix: Prefix arms a "defer" flag before SetupZones runs so SpawnPrefab calls
    // enqueue their work instead of executing immediately. The Postfix disarms the
    // flag and starts a coroutine that drains the queue one SetupStopwatch frame
    // at a time, spreading the Instantiate work across multiple frames.
    internal static class LootZonePatch
    {
        static readonly Queue<(NodeTags node, RandomStream stream)> s_pending = new();
        static bool s_deferring;

        [HarmonyPatch(typeof(LootZoneManager), nameof(LootZoneManager.SetupZones))]
        internal static class SetupZonesPatch
        {
            static void Prefix()
            {
                if (!MapScalerConfig.EnableOptimizations.Value) return;
                s_pending.Clear();
                s_deferring = true;
            }

            static void Postfix()
            {
                s_deferring = false;
                if (s_pending.Count == 0) return;

                Plugin.Log.LogInfo(
                    $"[MapScaler] LootZone: deferring {s_pending.Count} SpawnPrefab calls across frames.");
                Plugin.Instance.StartCoroutine(DrainQueue());
            }
        }

        // Intercept SpawnPrefab and queue it while SetupZones is running.
        [HarmonyPatch(typeof(NodeTags), nameof(NodeTags.SpawnPrefab))]
        internal static class SpawnPrefabPatch
        {
            static bool Prefix(NodeTags __instance, RandomStream randomStream)
            {
                if (!s_deferring) return true; // run normally outside of SetupZones
                s_pending.Enqueue((__instance, randomStream));
                return false; // skip original — will be called from DrainQueue
            }
        }

        static IEnumerator DrainQueue()
        {
            var sw = new SetupStopwatch();
            while (s_pending.Count > 0)
            {
                var (node, stream) = s_pending.Dequeue();
                if (node != null)
                    node.SpawnPrefab(stream); // s_deferring is false, runs normally

                if (sw.ElapsedFrame()) { yield return null; sw.Restart(); }
            }
            Plugin.Log.LogInfo("[MapScaler] LootZone: all deferred SpawnPrefab calls complete.");
        }
    }
}
