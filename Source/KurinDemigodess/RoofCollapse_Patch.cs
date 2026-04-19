using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// When a roof/mountain collapses on the Demigodess (alive or corpse),
    /// teleport her to a safe spot on the same map so she doesn't get
    /// buried under impassable rubble.
    /// </summary>
    [HarmonyPatch(typeof(RoofCollapserImmediate), "DropRoofInCellPhaseOne")]
    public static class RoofCollapse_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(IntVec3 c, Map map)
        {
            if (map == null) return;

            var things = c.GetThingList(map).ToList();
            foreach (var thing in things)
            {
                // Check living Demigodess
                var pawn = thing as Pawn;
                if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
                {
                    TeleportToSafety(pawn, map);
                    continue;
                }

                // Check Demigodess corpse
                var corpse = thing as Corpse;
                if (corpse != null && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
                {
                    TeleportCorpseToSafety(corpse, map);
                }
            }
        }

        private static void TeleportToSafety(Pawn pawn, Map map)
        {
            // Find a safe outdoor spot nearby
            IntVec3 safeSpot;
            if (CellFinder.TryFindRandomCellNear(pawn.Position, map, 20,
                (IntVec3 cell) => cell.InBounds(map) && cell.Standable(map) && !cell.Roofed(map), out safeSpot))
            {
                pawn.Position = safeSpot;
                pawn.Notify_Teleported(true, false);
                Messages.Message(
                    "The Demigodess narrowly escaped the collapsing roof through divine reflexes.",
                    pawn, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (CellFinder.TryFindRandomCellNear(pawn.Position, map, 50,
                (IntVec3 cell) => cell.InBounds(map) && cell.Standable(map), out safeSpot))
            {
                pawn.Position = safeSpot;
                pawn.Notify_Teleported(true, false);
            }
        }

        private static void TeleportCorpseToSafety(Corpse corpse, Map map)
        {
            IntVec3 safeSpot;
            if (CellFinder.TryFindRandomCellNear(corpse.Position, map, 20,
                (IntVec3 cell) => cell.InBounds(map) && cell.Standable(map) && !cell.Roofed(map), out safeSpot))
            {
                corpse.Position = safeSpot;
            }
            else if (CellFinder.TryFindRandomCellNear(corpse.Position, map, 50,
                (IntVec3 cell) => cell.InBounds(map) && cell.Standable(map), out safeSpot))
            {
                corpse.Position = safeSpot;
            }
        }
    }
}
