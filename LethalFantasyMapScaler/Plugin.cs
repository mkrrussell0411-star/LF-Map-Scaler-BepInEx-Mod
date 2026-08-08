using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using DunGen;
using HarmonyLib;

namespace LethalFantasyMapScaler
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            MapScalerConfig.Initialize(Config);

            ApplyFrameBudget();

            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();

            // Restore archetype values when generation finishes or fails.
            DungeonGenerator.OnAnyDungeonGenerationStatusChanged += Patches.DungeonGeneratorPatch.OnStatusChanged;

            // Progress logging for dungeon generation phases.
            DungeonGenerator.OnAnyDungeonGenerationStatusChanged += Patches.DungeonGenerationLogger.OnStatusChanged;

            // Start the SetupManager state-transition monitor.
            StartCoroutine(Patches.SetupManagerMonitor.PollState());

            Log.LogInfo(
                $"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} loaded. " +
                $"Map ×{MapScalerConfig.MapSizeMultiplier.Value}, " +
                $"Branch ×{MapScalerConfig.BranchMultiplier.Value}, " +
                $"Frame budget {MapScalerConfig.LoadingFrameBudgetMs.Value}ms, " +
                $"Max attempts {MapScalerConfig.MaxGenerationAttempts.Value}");
        }

        private static void ApplyFrameBudget()
        {
            if (!MapScalerConfig.EnableFrameBudgetOverride.Value) return;

            int budget = MapScalerConfig.LoadingFrameBudgetMs.Value;
            if (budget == 0)
                budget = int.MaxValue; // 0 = no limit, run coroutines flat-out

            // SetupStopwatch.MINIMUM_FRAME_MILLISECONDS is static readonly;
            // reflection is the only way to override it at runtime.
            FieldInfo field = typeof(SetupStopwatch).GetField(
                "MINIMUM_FRAME_MILLISECONDS",
                BindingFlags.Public | BindingFlags.Static);

            if (field == null)
            {
                Log.LogWarning("[MapScaler] Could not find SetupStopwatch.MINIMUM_FRAME_MILLISECONDS — frame budget NOT applied.");
                return;
            }

            field.SetValue(null, budget);
            Log.LogDebug($"[MapScaler] Loading frame budget set to {budget}ms (was 33ms).");
        }
    }
}
