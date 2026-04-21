using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Aethira ignores pathing-cell surcharges from terrain (mud, marsh, shallow
    /// water, blood, snow, ash, sand) AND from cell-blocking things like plants
    /// and filth. Each cell costs her her normal cardinal/diagonal tick count,
    /// with no slowdown added on top.
    ///
    /// In RimWorld 1.6, Pawn_PathFollower.CostToMoveIntoCell has two overloads:
    ///   (Pawn, IntVec3)             - public wrapper, computes base cost then
    ///                                 delegates to the 3-arg version.
    ///   (Pawn, IntVec3, int base)   - internal, adds terrain + plant + thing
    ///                                 path cost onto the supplied base.
    ///
    /// We postfix BOTH so the cap applies no matter which overload the caller
    /// hits. The 3-arg postfix is authoritative (caps at the supplied baseCost,
    /// which is exactly the pre-surcharge value). The 2-arg postfix re-derives
    /// the base from the pawn's ticks-per-move properties as a safety net in
    /// case some caller invokes the public wrapper without the internal one.
    ///
    /// Gated by HasDemigodessGene so only Aethira benefits - regular Kurins
    /// still slow on bad terrain like everyone else.
    /// </summary>
    public static class TerrainSpeedImmunity_Patch
    {
        private static readonly string[] CandidateTypeNames = new[]
        {
            "Verse.AI.Pawn_PathFollower",
            "Verse.PathFinder",
            "Verse.AI.PathFinder",
            "Verse.GenPath",
            "Verse.AI.GenPath",
            "Verse.GenGrid",
        };

        private static bool _firedOnceForAethira;

        public static void ApplyPatch(Harmony harmony)
        {
            var targets = FindCostMethods();
            if (targets.Count == 0)
            {
                Log.Warning("[KurinDemigodess] TerrainSpeedImmunity: CostToMoveIntoCell not found in any candidate type; terrain immunity disabled.");
                return;
            }

            int patched = 0;
            foreach (var target in targets)
            {
                var paramCount = target.GetParameters().Length;
                HarmonyMethod postfix;
                if (paramCount == 3)
                {
                    postfix = new HarmonyMethod(typeof(TerrainSpeedImmunity_Patch), nameof(Postfix_ThreeArg));
                }
                else
                {
                    postfix = new HarmonyMethod(typeof(TerrainSpeedImmunity_Patch), nameof(Postfix_TwoArg));
                }

                try
                {
                    harmony.Patch(target, null, postfix);
                    Log.Message("[KurinDemigodess] TerrainSpeedImmunity patch applied on " + target.DeclaringType.FullName + "." + target.Name + " (" + paramCount + "-arg)");
                    patched++;
                }
                catch (Exception ex)
                {
                    Log.Warning("[KurinDemigodess] TerrainSpeedImmunity: failed to patch " + target.DeclaringType.FullName + "." + target.Name + ": " + ex.Message);
                }
            }

            if (patched == 0)
            {
                throw new Exception("TerrainSpeedImmunity: found candidate methods but none could be patched.");
            }
        }

        private static List<MethodInfo> FindCostMethods()
        {
            var found = new List<MethodInfo>();
            var seen = new HashSet<MethodInfo>();

            // Prefer the known candidates for both signatures.
            for (int i = 0; i < CandidateTypeNames.Length; i++)
            {
                var t = AccessTools.TypeByName(CandidateTypeNames[i]);
                if (t == null) continue;

                TryAddMethod(t, new[] { typeof(Pawn), typeof(IntVec3) }, found, seen);
                TryAddMethod(t, new[] { typeof(Pawn), typeof(IntVec3), typeof(int) }, found, seen);
            }

            // Fallback: brute-force scan for both signatures if we got nothing from the candidates.
            if (found.Count == 0)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    catch { continue; }

                    for (int j = 0; j < types.Length; j++)
                    {
                        var t = types[j];
                        if (t == null) continue;
                        TryAddMethod(t, new[] { typeof(Pawn), typeof(IntVec3) }, found, seen);
                        TryAddMethod(t, new[] { typeof(Pawn), typeof(IntVec3), typeof(int) }, found, seen);
                    }
                }
            }

            return found;
        }

        private static void TryAddMethod(Type t, Type[] paramTypes, List<MethodInfo> found, HashSet<MethodInfo> seen)
        {
            try
            {
                var m = t.GetMethod("CostToMoveIntoCell",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
                    null,
                    paramTypes,
                    null);
                if (m != null && seen.Add(m))
                {
                    found.Add(m);
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix on the 3-arg overload: cap __result to the supplied baseCost
        /// (passed as __2). baseCost is the pawn's pre-surcharge tick cost for
        /// this step, so this strips every additive slowdown (terrain, plant,
        /// thing). Uses positional __0/__1/__2 so param-name mismatches with
        /// the game's source don't break the binding.
        /// </summary>
        public static void Postfix_ThreeArg(Pawn __0, IntVec3 __1, int __2, ref int __result)
        {
            var pawn = __0;
            var baseCost = __2;
            if (pawn == null) return;
            if (!Gene_Demigodess.HasDemigodessGene(pawn)) return;

            if (__result > baseCost)
            {
                if (!_firedOnceForAethira)
                {
                    _firedOnceForAethira = true;
                    Log.Message("[KurinDemigodess] TerrainSpeedImmunity: first cap fired for " + pawn.LabelShort + " (3-arg, was " + __result + ", capped to " + baseCost + ").");
                }
                __result = baseCost;
            }
        }

        /// <summary>
        /// Postfix on the 2-arg overload: re-derive base from the pawn's
        /// ticks-per-move properties. Safety net for callers that don't go
        /// through the 3-arg overload.
        /// </summary>
        public static void Postfix_TwoArg(Pawn __0, IntVec3 __1, ref int __result)
        {
            var pawn = __0;
            var c = __1;
            if (pawn == null) return;
            if (!Gene_Demigodess.HasDemigodessGene(pawn)) return;

            bool cardinal = c.x == pawn.Position.x || c.z == pawn.Position.z;
            int baseCost = (int)(cardinal ? pawn.TicksPerMoveCardinal : pawn.TicksPerMoveDiagonal);

            if (__result > baseCost)
            {
                if (!_firedOnceForAethira)
                {
                    _firedOnceForAethira = true;
                    Log.Message("[KurinDemigodess] TerrainSpeedImmunity: first cap fired for " + pawn.LabelShort + " (2-arg, was " + __result + ", capped to " + baseCost + ").");
                }
                __result = baseCost;
            }
        }
    }
}
