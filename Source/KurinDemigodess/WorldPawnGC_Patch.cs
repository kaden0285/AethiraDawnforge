using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Prevents the WorldPawn garbage collector from discarding the Demigodess.
    /// Patches AccumulatePawnGCDataImmediate to always mark her as "kept".
    /// </summary>
    [HarmonyPatch(typeof(WorldPawnGC), nameof(WorldPawnGC.AccumulatePawnGCDataImmediate))]
    public static class WorldPawnGC_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Dictionary<Pawn, string> __result)
        {
            if (__result == null) return;

            // Find all Demigodess pawns in the world and make sure they're kept
            foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
            {
                if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn) && !__result.ContainsKey(pawn))
                {
                    __result[pawn] = "Kurin Demigodess (immortal, never discard)";
                }
            }
        }
    }

    /// <summary>
    /// Prevent PassToWorld with Discard mode from removing her.
    /// </summary>
    [HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.PassToWorld))]
    public static class WorldPawns_PassToWorld_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, PawnDiscardDecideMode discardMode)
        {
            if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
            {
                if (discardMode == PawnDiscardDecideMode.Discard)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Prevent RemovePawn from removing the Demigodess from world pawns
    /// unless she's being spawned back on a map.
    /// </summary>
    /// <summary>
    /// Block Pawn.Discard from permanently removing the Demigodess.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Discard))]
    public static class PawnDiscard_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Thing __instance)
        {
            var pawn = __instance as Pawn;
            if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
            {
                return false; // Never discard
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.RemovePawn))]
    public static class WorldPawns_Remove_Patch
    {
        public static bool allowRemoval = false;

        [HarmonyPrefix]
        public static bool Prefix(Pawn p)
        {
            if (p != null && Gene_Demigodess.HasDemigodessGene(p))
            {
                if (allowRemoval) return true;
                return false;
            }
            return true;
        }
    }
}
