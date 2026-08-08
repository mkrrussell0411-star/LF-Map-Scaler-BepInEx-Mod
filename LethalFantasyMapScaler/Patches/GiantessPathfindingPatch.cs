using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GiantessGraphPathfinding;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // GiantessPathfinding.Setup starts a coroutine (CreateCoroutine) that calls
    // VisibilityPolygon.BreakIntersectionsSlow with an IEnumerable<Segment> backed by
    // a LINQ .Select() iterator. Inside BreakIntersectionsSlow:
    //   - segments.Count()     is called in the outer-loop condition  → O(N) per call, called N times   = O(N²)
    //   - segments.ElementAt(i) is called in the outer loop           → O(N) per call, called N times   = O(N²)
    //   - segments.Count()     is called in the inner-loop condition  → O(N) per call, called N² times  = O(N³)
    //   - segments.ElementAt(j) is called in the inner loop           → O(N) per call, called N² times  = O(N³)
    // Total: O(N³) where N = number of GraphPathMonoBehaviour segments in the dungeon.
    // The algorithm itself is O(N²); the overhead is entirely from LINQ re-enumeration.
    //
    // Fix 1 — O(N³) → O(N²): convert the iterator to a List before the loops.
    // Fix 2 — move BreakIntersections off the main thread: it touches only C# value types
    //          (Vector3 structs and VisibilityPolygon.Segment structs), so Task.Run is safe.
    // Fix 3 — add SetupStopwatch yield points inside the deduplication loop and connection
    //          loop that CreateCoroutine left without internal yields.
    [HarmonyPatch(typeof(GiantessPathfinding), nameof(GiantessPathfinding.Setup))]
    internal static class GiantessPathfindingPatch
    {
        static bool Prefix(GiantessPathfinding __instance, ref IEnumerator __result)
        {
            if (!MapScalerConfig.EnableGiantessPathfinding.Value) return true;
            __result = FastSetup(__instance);
            return false;
        }

        static readonly PropertyInfo s_instanceProp =
            typeof(GiantessPathfinding).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

        static IEnumerator FastSetup(GiantessPathfinding instance)
        {
            s_instanceProp.SetValue(null, instance);
            GraphPathMonoBehaviour[] comps = UnityEngine.Object.FindObjectsOfType<GraphPathMonoBehaviour>();
            yield return null;

            // Collect segments on the main thread (GetSegment may access Unity objects).
            List<VisibilityPolygon.Segment> segs = comps
                .Select(p => p.GetSegment)
                .ToList();

            Plugin.Log.LogInfo($"[MapScaler] GiantessPathfinding: {segs.Count} path segments; computing intersections on thread...");

            // Run the fixed O(N²) algorithm on a background thread.
            // All inputs are C# value types (Vector3 structs) — no Unity API calls.
            List<VisibilityPolygon.Segment> splitSegments = null;
            Task task = Task.Run(() => { splitSegments = BreakIntersectionsFixed(segs); });

            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
            {
                Plugin.Log.LogError("[MapScaler] GiantessPathfinding background task failed: " + task.Exception);
                yield break;
            }

            Plugin.Log.LogInfo($"[MapScaler] GiantessPathfinding: {splitSegments.Count} split segments, building graph...");

            GraphPathfinding.CalculatedResult = new GraphPathfinding();

            // Build one GraphNode per endpoint of every split segment.
            List<GraphNode> allNodes = splitSegments
                .SelectMany(s => s.points)
                .Select(v => new GraphNode(v))
                .ToList();
            var initialQuadTree = new QuadTree<GraphNode>(allNodes);
            yield return null;

            // Deduplicate nodes that are within 0.1 units of each other.
            // The original had no yield inside this loop — add SetupStopwatch checks.
            var sw = new SetupStopwatch();
            var seen = new HashSet<GraphNode>();
            var graphNodes = new List<GraphNode>();
            foreach (GraphNode node in allNodes)
            {
                if (!seen.Contains(node))
                {
                    graphNodes.Add(node);
                    foreach (GraphNode nearby in initialQuadTree.GetClosetItems(node.position, 0.1f))
                        seen.Add(nearby);
                }
                if (sw.ElapsedFrame()) { yield return null; sw.Restart(); }
            }

            var graphNodeQuadTree = new QuadTree<GraphNode>(graphNodes);
            List<GraphPath> graphPaths = splitSegments
                .Select(s => new GraphPath(
                    graphNodeQuadTree.GetClosetItemByCenter(s[0], 0.1f),
                    graphNodeQuadTree.GetClosetItemByCenter(s[1], 0.1f)))
                .Where(p => p.cost > 0.1f)
                .ToList();
            var graphPathQuadTree = new QuadTree<GraphPath>(graphPaths);
            yield return null;

            // Wire node connections. The original had no yield inside this loop either.
            sw.Restart();
            foreach (GraphPath path in graphPaths)
            {
                path.nodeA.AddConnection(path);
                path.nodeB.AddConnection(path);
                if (sw.ElapsedFrame()) { yield return null; sw.Restart(); }
            }
            yield return null;

            // Apply animation names to paths that have animations.
            foreach (GraphPathMonoBehaviour comp in comps)
            {
                if (comp.HasAnimation)
                {
                    System.ValueTuple<Vector3, Vector3> wPos = comp.GetWorldPositions;
                    Vector3 posA = wPos.Item1; posA.y = 0f;
                    Vector3 posB = wPos.Item2; posB.y = 0f;
                    GraphNode nodeA = graphNodeQuadTree.GetClosetItemByCenter(posA, 0.1f);
                    GraphNode nodeB = graphNodeQuadTree.GetClosetItemByCenter(posB, 0.1f);
                    System.ValueTuple<GraphNode, GraphPath> conn =
                        nodeA.connections.FirstOrDefault(c => c.Item1 == nodeB);
                    if (conn.Item2 != null)
                        conn.Item2.animationName = comp.animationName;
                }
            }

            GraphPathfinding.CalculatedResult.graphNodes = graphNodes;
            GraphPathfinding.CalculatedResult.graphPaths = graphPaths;
            GraphPathfinding.CalculatedResult.graphNodeQuadTree = graphNodeQuadTree;
            GraphPathfinding.CalculatedResult.graphPathQuadTree = graphPathQuadTree;
            instance.pathfinding = GraphPathfinding.CalculatedResult;

            Plugin.Log.LogInfo("[MapScaler] GiantessPathfinding: setup complete.");
        }

        // O(N²) reimplementation of VisibilityPolygon.BreakIntersectionsSlow.
        // Safe to call from a background thread: all inputs and outputs are C# value types.
        static List<VisibilityPolygon.Segment> BreakIntersectionsFixed(List<VisibilityPolygon.Segment> segs)
        {
            int n = segs.Count;
            var result = new List<VisibilityPolygon.Segment>();

            for (int i = 0; i < n; i++)
            {
                VisibilityPolygon.Segment seg = segs[i];
                var splits = new List<Vector3>();

                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    VisibilityPolygon.Segment seg2 = segs[j];
                    Vector3 a = seg[0], b = seg[1], c = seg2[0], d = seg2[1];
                    if (VisibilityPolygon.DoLineSegmentsIntersectSlow(a, b, c, d))
                    {
                        (bool ok, Vector3 pt) = VisibilityPolygon.IntersectLinesSlow(a, b, c, d);
                        if (ok && !VisibilityPolygon.Vector3Equal(pt, a) && !VisibilityPolygon.Vector3Equal(pt, b))
                            splits.Add(pt);
                    }
                }

                Vector3 cur = seg[0];
                while (splits.Count > 0)
                {
                    int best = 0;
                    float bestDist = VisibilityPolygon.SqrDistance(cur, splits[0]);
                    for (int k = 1; k < splits.Count; k++)
                    {
                        float d2 = VisibilityPolygon.SqrDistance(cur, splits[k]);
                        if (d2 < bestDist) { bestDist = d2; best = k; }
                    }
                    result.Add(new VisibilityPolygon.Segment(cur, splits[best]));
                    cur = splits[best];
                    splits.RemoveAt(best);
                }

                result.Add(new VisibilityPolygon.Segment(cur, seg[1]));
            }

            return result;
        }
    }
}
