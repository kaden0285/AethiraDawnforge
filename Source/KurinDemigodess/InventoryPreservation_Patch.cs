using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Nothing rots or deteriorates while stored in Aethira's inventory.
    /// Her divine pocket-space freezes time on anything she's carrying.
    ///
    /// Two prefixes:
    ///   1. CompRottable.CompTick / CompTickRare - skip ticking if the rotting thing
    ///      is inside a Demigodess pawn's inventory tracker. Freezes rot progress.
    ///   2. SteadyEnvironmentEffects.FinalDeteriorationRate - returns 0 if the thing
    ///      is held by Aethira. Items in inventory don't normally deteriorate, but
    ///      some mods expose inventory items to environmental effects.
    /// </summary>
    public static class InventoryPreservation_Patch
    {
        /// <summary>
        /// Walks up the ParentHolder chain to find the owning pawn, if any.
        /// Returns null if the thing isn't in any pawn's inventory/equipment/apparel.
        /// </summary>
        public static Pawn GetHoldingPawn(Thing thing)
        {
            if (thing == null) return null;

            IThingHolder holder = thing.ParentHolder;
            int safety = 0;
            while (holder != null && safety++ < 8)
            {
                if (holder is Pawn_InventoryTracker inv) return inv.pawn;
                if (holder is Pawn_EquipmentTracker eq) return eq.pawn;
                if (holder is Pawn_ApparelTracker ap) return ap.pawn;
                if (holder is Pawn p) return p;

                holder = holder.ParentHolder;
            }
            return null;
        }

        public static bool IsHeldByDemigodess(Thing thing)
        {
            var pawn = GetHoldingPawn(thing);
            return pawn != null && Gene_Demigodess.HasDemigodessGene(pawn);
        }
    }

    /// <summary>
    /// Skip rot ticking when the item is in a Demigodess's inventory.
    /// </summary>
    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.CompTick))]
    public static class InventoryPreservation_Rot_CompTick_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(CompRottable __instance)
        {
            return !InventoryPreservation_Patch.IsHeldByDemigodess(__instance.parent);
        }
    }

    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.CompTickRare))]
    public static class InventoryPreservation_Rot_CompTickRare_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(CompRottable __instance)
        {
            return !InventoryPreservation_Patch.IsHeldByDemigodess(__instance.parent);
        }
    }

    /// <summary>
    /// Zero deterioration rate for things held by a Demigodess.
    /// Covers mods that tick env effects on inventory contents.
    /// </summary>
    [HarmonyPatch(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.FinalDeteriorationRate),
        new[] { typeof(Thing), typeof(bool), typeof(TerrainDef) })]
    public static class InventoryPreservation_Deterioration_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing t, ref float __result)
        {
            if (__result <= 0f) return;
            if (InventoryPreservation_Patch.IsHeldByDemigodess(t))
            {
                __result = 0f;
            }
        }
    }
}
