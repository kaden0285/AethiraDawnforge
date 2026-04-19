using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Blocks any attempt to add one of Aethira's divine genes to a pawn other
    /// than the "real" Aethira (the one tracked by WorldComponent_DemigodessTracker).
    /// Catches Gene Siphon, Gene Assembler implants, dev-mode gene adds, and any
    /// other path that goes through Pawn_GeneTracker.AddGene.
    ///
    /// Allows the add-gene call if:
    ///   - The gene is not a Demigodess gene
    ///   - No Aethira is currently tracked (e.g., during PawnGenerator flow for
    ///     a new colony or emergency respawn fallback)
    ///   - The registered Aethira reference is destroyed (failsafe regeneration path)
    ///   - The pawn receiving the gene IS the registered Aethira
    /// Blocks in every other case.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.AddGene), new Type[] { typeof(GeneDef), typeof(bool) })]
    public static class AntiGeneTransfer_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_GeneTracker __instance, GeneDef geneDef)
        {
            if (geneDef == null) return true;
            if (!IsDemigodessGene(geneDef)) return true;

            var pawn = __instance?.pawn;
            if (pawn == null) return true;

            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            if (tracker == null) return true; // world not ready, allow

            var registered = tracker.SavedDemigodess;

            // No Aethira registered yet, or the registered one is destroyed:
            // allow the gene add (this pawn is becoming the new Aethira).
            if (registered == null || registered.Destroyed) return true;

            // This pawn IS the registered Aethira: allow.
            if (registered == pawn) return true;

            // Different pawn trying to get Demigodess genes: REJECT.
            Log.Message(string.Format(
                "[KurinDemigodess] Blocked Demigodess gene transfer: {0} tried to gain {1}. Only Aethira can carry her genes.",
                pawn.LabelShort, geneDef.defName));

            Messages.Message(
                string.Format("Aethira's divine essence rejects {0}. Her genes cannot be transferred.", pawn.LabelShort),
                pawn, MessageTypeDefOf.RejectInput, false);

            return false;
        }

        private static bool IsDemigodessGene(GeneDef def)
        {
            if (def == null || def.defName == null) return false;
            switch (def.defName)
            {
                case "DG_DivineConstitution":
                case "DG_DivineVitality":
                case "DG_DivineGrace":
                case "DG_DivinePresence":
                case "DG_HairSnowWhite":
                    return true;
                default:
                    return false;
            }
        }
    }
}
