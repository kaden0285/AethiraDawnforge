using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Prevents the Demigodess from being removed from the colony:
    /// - Cannot be banished (manual patch to avoid ambiguous match)
    /// - Cannot change faction away from player
    /// - Cannot be sold to traders
    /// </summary>

    // Block banishment - manual patch to handle multiple Banish overloads
    public static class AntiBanish_Patch
    {
        public static void ApplyPatch(Harmony harmony)
        {
            var methods = typeof(PawnBanishUtility).GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (var m in methods)
            {
                if (m.Name != "Banish") continue;
                var parms = m.GetParameters();
                if (parms.Length >= 1 && parms[0].ParameterType == typeof(Pawn))
                {
                    var prefix = typeof(AntiBanish_Patch).GetMethod("Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(m, prefix: new HarmonyMethod(prefix));
                    break;
                }
            }
        }

        public static bool Prefix(Pawn pawn)
        {
            if (Gene_Demigodess.HasDemigodessGene(pawn))
            {
                Messages.Message(
                    "The Demigodess cannot be banished. She is bound to this colony by divine will.",
                    pawn, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return true;
        }
    }

    // Block faction change
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class AntiFactionChange_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, Faction newFaction)
        {
            if (!Gene_Demigodess.HasDemigodessGene(__instance)) return true;
            if (newFaction != null && newFaction.IsPlayer) return true;
            if (__instance.Faction != null && __instance.Faction.IsPlayer)
            {
                return false;
            }
            return true;
        }
    }

    // Block being sold to traders
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class AntiSell_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(TradeDeal __instance, ref bool __result)
        {
            foreach (var tradeable in __instance.AllTradeables)
            {
                if (tradeable.CountToTransfer > 0)
                {
                    var thing = tradeable.FirstThingTrader ?? tradeable.FirstThingColony;
                    if (thing is Pawn pawn && Gene_Demigodess.HasDemigodessGene(pawn))
                    {
                        Messages.Message(
                            "The Demigodess cannot be sold or traded. She belongs to no one.",
                            pawn, MessageTypeDefOf.RejectInput, false);
                        __result = false;
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
