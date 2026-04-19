using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Death system:
    /// - On home map: body stays, resurrection hediff ticks, she gets up
    /// - Away from home: body teleported to home base with all wounds, resurrection hediff applied,
    ///   7-day recovery coma applied AFTER she resurrects
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class DeathlessDemigodess_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (!Gene_Demigodess.HasDemigodessGene(__instance))
                return true;

            bool isOnHomeMap = __instance.Map != null && __instance.Map.IsPlayerHome;

            if (isOnHomeMap)
            {
                // On home map: let death proceed, Postfix handles resurrection
                return true;
            }
            else
            {
                // Away from home: prevent death, teleport home as corpse with 7-day coma
                TriggerHomeReturn(__instance);
                return false;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            // Favor source #6: kills of hostile pawns while Aethira is on the map grant favor
            if (__instance != null && __instance.Dead
                && !Gene_Demigodess.HasDemigodessGene(__instance))
            {
                TryGrantKillFavor(__instance);
            }

            if (!Gene_Demigodess.HasDemigodessGene(__instance))
                return;

            if (!__instance.Dead) return;

            // Apply resurrection hediff to the corpse
            if (Kurin_DefOf.DG_DivineResurrecting != null && !__instance.health.hediffSet.HasHediff(Kurin_DefOf.DG_DivineResurrecting))
            {
                __instance.health.AddHediff(Kurin_DefOf.DG_DivineResurrecting);
            }

            // Apply corpse preservation
            if (Kurin_DefOf.DG_CorpsePreservation != null && !__instance.health.hediffSet.HasHediff(Kurin_DefOf.DG_CorpsePreservation))
            {
                __instance.health.AddHediff(Kurin_DefOf.DG_CorpsePreservation);
            }

            // Death-recoil AoE pulse - divine essence lashes out at her killers
            if (KurinDemigodessMod.Settings.deathRecoilEnabled)
            {
                TriggerDeathRecoil(__instance);
            }

            Messages.Message(
                "The Demigodess has fallen, but her body glows with divine light. She will rise again.",
                __instance, MessageTypeDefOf.NeutralEvent, false);

            Find.LetterStack.ReceiveLetter(
                "The Demigodess Will Return",
                "Aethira Dawnforge has been struck down. But her people know she always comes back. Divine energy courses through her body, regrowing flesh and bone. There is no grief, only patience.",
                LetterDefOf.NeutralEvent, __instance);
        }

        /// <summary>
        /// Grants +1 Divine Favor (up to the daily kill cap) when a hostile pawn
        /// is killed while Aethira is present on the same map.
        /// </summary>
        private static void TryGrantKillFavor(Pawn dead)
        {
            try
            {
                if (dead == null || dead.Faction == null) return;
                if (!dead.HostileTo(Faction.OfPlayer)) return;
                if (!KurinDemigodessMod.Settings.divineFavorEnabled) return;

                var map = dead.MapHeld;
                if (map == null && dead.Corpse != null) map = dead.Corpse.MapHeld;
                if (map == null) return;

                bool aethiraHere = false;
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p != null && !p.Dead && Gene_Demigodess.HasDemigodessGene(p))
                    {
                        aethiraHere = true;
                        break;
                    }
                }
                if (!aethiraHere) return;

                Current.Game?.GetComponent<GameComponent_DivineFavor>()?.TryAddKillFavor(1);
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Kill favor grant failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Divine-essence shockwave that damages and stuns hostile pawns within
        /// DeathRecoilRadius tiles of the fallen Demigodess. Allies and neutrals
        /// are untouched. Lore: her divine power lashes out uncontrollably at
        /// whoever struck her down.
        /// Internal so debug actions can call it for testing.
        /// </summary>
        internal static void TriggerDeathRecoil(Pawn demigodess)
        {
            try
            {
                Map map = demigodess.Corpse != null ? demigodess.Corpse.MapHeld : demigodess.MapHeld;
                if (map == null) return;

                IntVec3 center = demigodess.Corpse != null ? demigodess.Corpse.PositionHeld : demigodess.PositionHeld;
                if (!center.IsValid) return;

                float radius = KurinDemigodessMod.Settings.deathRecoilRadius;
                if (radius < 1f) return;

                // Visual: big flash at the center
                FleckMaker.Static(center.ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, radius);

                // Gather hostile pawns in range
                var hostiles = new List<Pawn>();
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p == null || p.Dead) continue;
                    if (p == demigodess) continue;
                    if (!p.HostileTo(Faction.OfPlayer)) continue;
                    if ((float)p.Position.DistanceToSquared(center) > radius * radius) continue;
                    hostiles.Add(p);
                }

                foreach (var target in hostiles)
                {
                    float distance = target.Position.DistanceTo(center);
                    float falloff = Mathf.Clamp01(1f - (distance / radius));
                    float damage = Mathf.Max(8f, 30f * falloff);

                    target.TakeDamage(new DamageInfo(DamageDefOf.Burn, damage, 0.5f, -1f, demigodess));

                    if (target.stances != null && target.stances.stunner != null)
                    {
                        target.stances.stunner.StunFor(Rand.Range(60, 180), demigodess, false, false);
                    }

                    // Visual at each hit
                    FleckMaker.Static(target.Position.ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, 1.5f);
                }

                if (hostiles.Count > 0)
                {
                    Messages.Message(
                        string.Format("Aethira's divine essence lashes out at {0} {1} as she falls.",
                            hostiles.Count, hostiles.Count == 1 ? "enemy" : "enemies"),
                        new TargetInfo(center, map),
                        MessageTypeDefOf.ThreatBig, false);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Death-recoil failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Dies away from home: teleport body to home base, apply resurrection hediff,
        /// mark for 7-day coma after resurrection.
        /// </summary>
        private static void TriggerHomeReturn(Pawn pawn)
        {
            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (homeMap == null)
            {
                // No home map. This shouldn't happen but just in case, let death proceed
                // and the postfix will handle on-map resurrection
                return;
            }

            // Remove from current location
            if (pawn.Spawned) pawn.DeSpawn();
            if (pawn.IsCaravanMember())
            {
                var caravan = pawn.GetCaravan();
                if (caravan != null) caravan.RemovePawn(pawn);
            }

            // Find spawn position at home
            IntVec3 spawnPos = FindShrinePosition(homeMap);
            if (!spawnPos.IsValid)
            {
                spawnPos = CellFinder.RandomEdgeCell(homeMap);
            }

            // Kill the pawn (she needs to actually die so the resurrection system works)
            // But first spawn her at home so she dies on the home map
            GenSpawn.Spawn(pawn, spawnPos, homeMap);

            // Now let her die on the home map - the Postfix will apply resurrection hediff
            // We return false from the prefix so Kill doesn't run on the off-map,
            // but we need to manually kill her on the home map
            pawn.Kill(null);

            // Mark for 7-day coma after resurrection
            // The resurrection hediff will check for this flag and apply the coma
            if (Kurin_DefOf.DG_DivineResurrecting != null)
            {
                var resHediff = pawn.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_DivineResurrecting) as Hediff_DivineResurrecting;
                if (resHediff != null)
                {
                    resHediff.applyComaAfterResurrect = true;
                }
            }

            Messages.Message(
                "The Demigodess was struck down far from home. Divine power has brought her body back to the colony.",
                pawn, MessageTypeDefOf.NegativeEvent, false);

            Find.LetterStack.ReceiveLetter(
                "The Demigodess Falls Far From Home",
                "Aethira Dawnforge has been struck down far from home. Her body has been transported back through divine will. She will regenerate and rise again, but will need time to recover from the ordeal.",
                LetterDefOf.NegativeEvent, pawn);
        }

        private static IntVec3 FindShrinePosition(Map map)
        {
            if (Kurin_DefOf.DG_DivineShrine == null) return IntVec3.Invalid;
            var shrines = map.listerThings.ThingsOfDef(Kurin_DefOf.DG_DivineShrine);
            if (shrines == null || shrines.Count == 0) return IntVec3.Invalid;
            var shrine = shrines.First();
            foreach (var cell in GenAdj.CellsAdjacent8Way(shrine))
            {
                if (cell.InBounds(map) && cell.Standable(map))
                    return cell;
            }
            return shrine.Position;
        }

        private static void ApplyThoughtToColony(Pawn demigodess, string thoughtDefName)
        {
            var thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            if (thoughtDef == null || !thoughtDef.IsMemory) return;

            var map = demigodess.Map ?? Find.CurrentMap;
            if (map == null) return;

            foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist != demigodess && colonist.needs != null && colonist.needs.mood != null)
                {
                    colonist.needs.mood.thoughts.memories.TryGainMemory(thoughtDef);
                }
            }
        }
    }
}
