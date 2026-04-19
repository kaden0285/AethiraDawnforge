using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// When a map is about to be destroyed and the Demigodess is on it,
    /// teleport her home BEFORE the map deinits. She's always rescued first.
    /// The map still gets destroyed normally after she's safe.
    /// </summary>
    [HarmonyPatch(typeof(MapDeiniter), nameof(MapDeiniter.Deinit))]
    public static class MapProtection_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Map map)
        {
            if (map == null) return;
            if (map.IsPlayerHome) return; // Don't mess with home map destruction

            // Check for living Demigodess on this map
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (Gene_Demigodess.HasDemigodessGene(pawn))
                {
                    RescueDemigodess(pawn, map);
                }
            }

            // Check for Demigodess corpse on this map
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList())
            {
                var corpse = thing as Corpse;
                if (corpse != null && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
                {
                    RescueCorpse(corpse.InnerPawn, corpse, map);
                }
            }
        }

        private static void RescueDemigodess(Pawn pawn, Map dyingMap)
        {
            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome && m != dyingMap);
            if (homeMap == null) return;

            if (pawn.Spawned) pawn.DeSpawn();

            IntVec3 spawnPos = FindShrineOrEdge(homeMap);
            GenSpawn.Spawn(pawn, spawnPos, homeMap);

            // No coma here. The map was destroyed around her, she didn't expend energy to teleport.
            // She keeps whatever state she was in (wounded, downed, etc.)

            Messages.Message(
                "The Demigodess was rescued from a collapsing map. Divine power brought her home.",
                pawn, MessageTypeDefOf.NeutralEvent, false);
        }

        private static void RescueCorpse(Pawn pawn, Corpse corpse, Map dyingMap)
        {
            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome && m != dyingMap);
            if (homeMap == null)
            {
                // Last resort: WorldComponent ascension
                CorpseProtection_DeSpawn_Patch.allowMapDeinit = true;
                var tracker = Find.World.GetComponent<WorldComponent_DemigodessTracker>();
                if (tracker != null) tracker.BeginAscension(pawn);
                CorpseProtection_DeSpawn_Patch.allowMapDeinit = false;
                return;
            }

            // Allow despawn for map deinit teleport
            CorpseProtection_DeSpawn_Patch.allowMapDeinit = true;
            if (corpse.Spawned) corpse.DeSpawn();
            CorpseProtection_DeSpawn_Patch.allowMapDeinit = false;

            IntVec3 spawnPos = FindShrineOrEdge(homeMap);
            GenSpawn.Spawn(corpse, spawnPos, homeMap);

            Messages.Message(
                "The Demigodess's body was rescued from a collapsing map. It continues regenerating at the colony.",
                MessageTypeDefOf.NeutralEvent, false);
        }

        private static IntVec3 FindShrineOrEdge(Map map)
        {
            if (Kurin_DefOf.DG_DivineShrine != null)
            {
                var shrines = map.listerThings.ThingsOfDef(Kurin_DefOf.DG_DivineShrine);
                if (shrines != null && shrines.Count > 0)
                {
                    foreach (var cell in GenAdj.CellsAdjacent8Way(shrines.First()))
                    {
                        if (cell.InBounds(map) && cell.Standable(map))
                            return cell;
                    }
                }
            }
            return CellFinder.RandomEdgeCell(map);
        }
    }
}
