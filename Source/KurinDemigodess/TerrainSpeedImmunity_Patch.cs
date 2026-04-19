using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Aethira ignores terrain-induced pathing slowdown. Mud, marsh, shallow water,
    /// blood, snow, ash, sand, etc. cost her the same as plain dirt.
    ///
    /// The host class for CostToMoveIntoCell moved between RimWorld versions, so
    /// this patch resolves the target method at runtime via reflection and applies
    /// a manual postfix. We cap the returned cost at the move's base (cardinal or
    /// diagonal) tick cost, stripping any terrain surcharge.
    ///
    /// Gated by HasDemigodessGene so only Aethira benefits - regular Kurins still
    /// slow on bad terrain like everyone else.
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

        public static void ApplyPatch(Harmony harmony)
        {
            MethodInfo target = FindCostMethod();
            if (target == null)
            {
                Log.Warning("[KurinDemigodess] TerrainSpeedImmunity: CostToMoveIntoCell(Pawn, IntVec3) not found in any candidate type; terrain immunity disabled.");
                return;
            }

            var postfix = new HarmonyMethod(typeof(TerrainSpeedImmunity_Patch), nameof(Postfix));
            harmony.Patch(target, null, postfix);
            Log.Message("[KurinDemigodess] TerrainSpeedImmunity patch applied on " + target.DeclaringType.FullName + "." + target.Name);
        }

        private static MethodInfo FindCostMethod()
        {
            // Try the expected signature on each candidate type first.
            for (int i = 0; i < CandidateTypeNames.Length; i++)
            {
                var t = AccessTools.TypeByName(CandidateTypeNames[i]);
                if (t == null) continue;

                var m = AccessTools.Method(t, "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) });
                if (m != null) return m;
            }

            // Fallback: brute-force scan all loaded types for a static method with the right signature.
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
                    try
                    {
                        var m = t.GetMethod("CostToMoveIntoCell",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                            null,
                            new[] { typeof(Pawn), typeof(IntVec3) },
                            null);
                        if (m != null) return m;
                    }
                    catch { }
                }
            }
            return null;
        }

        public static void Postfix(Pawn pawn, IntVec3 c, ref int __result)
        {
            if (pawn == null) return;
            if (!Gene_Demigodess.HasDemigodessGene(pawn)) return;

            // Determine whether the step is cardinal or diagonal so we cap at the
            // correct base cost (cardinal < diagonal).
            bool cardinal = c.x == pawn.Position.x || c.z == pawn.Position.z;
            int baseCost = (int)(cardinal ? pawn.TicksPerMoveCardinal : pawn.TicksPerMoveDiagonal);

            if (__result > baseCost)
            {
                __result = baseCost;
            }
        }
    }
}
