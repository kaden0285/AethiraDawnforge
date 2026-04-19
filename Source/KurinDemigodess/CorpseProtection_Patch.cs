using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Makes the Demigodess's corpse completely indestructible.
    /// Nothing can destroy it - fire, collapse, acid, explosions, nothing.
    /// The resurrection hediff will eventually bring her back.
    ///
    /// Also patches Corpse.DeSpawn as a safety net.
    /// If somehow the corpse IS about to be lost, trigger 7-day ascension.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    public static class CorpseProtection_Destroy_Patch
    {
        public static bool allowResurrection = false;

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance)
        {
            var corpse = __instance as Corpse;
            if (corpse == null) return true;

            var pawn = corpse.InnerPawn;
            if (pawn == null) return true;
            if (!Gene_Demigodess.HasDemigodessGene(pawn)) return true;

            // Allow during resurrection
            if (allowResurrection) return true;

            // BLOCK the destruction entirely. Her corpse is indestructible.
            return false;
        }
    }

    /// <summary>
    /// Also make the corpse take no damage (so fire/explosions can't damage it to 0 HP).
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class CorpseProtection_Damage_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing __instance)
        {
            var corpse = __instance as Corpse;
            if (corpse == null) return true;

            var pawn = corpse.InnerPawn;
            if (pawn == null) return true;
            if (!Gene_Demigodess.HasDemigodessGene(pawn)) return true;

            // Block ALL damage to the corpse. It cannot be destroyed.
            return false;
        }
    }

    /// <summary>
    /// Block animals/pawns from eating the corpse.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
    public static class CorpseProtection_Prey_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, Thing prey)
        {
            var corpse = prey as Corpse;
            if (corpse != null && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Block the corpse from being selected as food.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap))]
    public static class CorpseProtection_Food_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Thing __result)
        {
            if (__result is Corpse corpse && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
            {
                __result = null;
            }
        }
    }

    /// <summary>
    /// Make the corpse unable to be hauled to graves/crematoriums.
    /// </summary>
    [HarmonyPatch(typeof(Verse.AI.HaulAIUtility), nameof(Verse.AI.HaulAIUtility.PawnCanAutomaticallyHaulFast))]
    public static class CorpseProtection_Haul_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, Thing t)
        {
            var corpse = t as Corpse;
            if (corpse != null && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Block corpse from being despawned by mods or forced cleanup.
    /// Only allow despawn if MapDeiniter is running (we handle that separately).
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class CorpseProtection_DeSpawn_Patch
    {
        public static bool allowMapDeinit = false;
        public static bool allowResurrection = false;

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance)
        {
            var corpse = __instance as Corpse;
            if (corpse == null) return true;

            var pawn = corpse.InnerPawn;
            if (pawn == null) return true;
            if (!Gene_Demigodess.HasDemigodessGene(pawn)) return true;

            // Allow despawn during map deinit
            if (allowMapDeinit) return true;

            // Allow despawn if pawn is being resurrected (no longer dead)
            if (!pawn.Dead) return true;

            // Allow despawn if ResurrectionUtility is running
            if (allowResurrection) return true;

            // Block all other despawns
            return false;
        }
    }

    /// <summary>
    /// Prevents the Demigodess from dropping her weapons, apparel, and inventory
    /// when downed. Vanilla RimWorld calls DropAndForbidEverything when a pawn goes
    /// down, which scatters their gear on the ground. This prefix skips that for
    /// Aethira so she keeps fighting-ready the moment she gets back up.
    /// Manual weapon swaps and "drop" commands go through TryDropEquipment which is
    /// a different path, so those still work.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DropAndForbidEverything))]
    public static class AntiWeaponDrop_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance)
        {
            if (Gene_Demigodess.HasDemigodessGene(__instance))
            {
                return false;
            }
            return true;
        }
    }
}
