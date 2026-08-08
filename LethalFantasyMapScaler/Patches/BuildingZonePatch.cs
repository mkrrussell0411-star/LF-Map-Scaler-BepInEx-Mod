// BuildingZonePatch is intentionally disabled.
//
// Deferring DungeonGenerator.ProcessLocalProps for buildings caused two visible bugs:
//   1. Buildings were invisible at close range because ProcessLocalProps selects which
//      RandomProp/LocalProp mesh variant is active. With the default state (all variants
//      disabled), buildings showed nothing up close but showed the pre-baked HLOD imposter
//      at distance — "buildings appear when you move further away."
//   2. The map camera only captures actively-rendered geometry. Buildings whose LOD0 mesh
//      was still disabled (drain loop hadn't run yet) did not appear on the map.
//
// The original SpawnBuildings coroutine already yields once per group, which is sufficient
// pacing. The per-building ProcessLocalProps runs synchronously inside each group, keeping
// buildings fully set up before the group yield.
namespace LethalFantasyMapScaler.Patches { }
