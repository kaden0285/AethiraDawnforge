using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace KurinDemigodess
{
    /// <summary>
    /// Pilgrimage event: a small group of Kurin from a friendly faction arrives
    /// to pay their respects to Aethira Dawnforge. They walk around the colony
    /// for a while then leave. Only fires while Aethira is alive on a home map.
    /// </summary>
    public class IncidentWorker_KurinPilgrimage : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms)) return false;
            if (!KurinDemigodessMod.Settings.pilgrimageEnabled) return false;

            var map = parms.target as Map;
            if (map == null || !map.IsPlayerHome) return false;

            if (!AethiraIsPresentOn(map)) return false;
            if (FindSourceFaction() == null) return false;

            return true;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var map = parms.target as Map;
            if (map == null) return false;

            var sourceFaction = FindSourceFaction();
            if (sourceFaction == null) return false;

            // Pick a Kurin pawn kind if one exists, otherwise fall back to Villager
            PawnKindDef kindDef = PickKurinOrFallback();
            if (kindDef == null) return false;

            int count = Rand.Range(2, 5);
            var pawns = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kindDef,
                    sourceFaction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true));
                pawns.Add(pawn);
            }

            IntVec3 entry;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out entry, map, CellFinder.EdgeRoadChance_Neutral))
            {
                return false;
            }

            foreach (var pawn in pawns)
            {
                var cell = CellFinder.RandomClosewalkCellNear(entry, map, 8);
                GenSpawn.Spawn(pawn, cell, map);
            }

            // Pilgrim offerings: drop a small gift cache at the entry
            SpawnOfferings(entry, map);

            // Vanilla visitor lord: they'll wander to a point, socialize, then leave
            LordMaker.MakeNewLord(sourceFaction, new LordJob_VisitColony(sourceFaction, entry), map, pawns);

            // Favor source #2: pilgrims bring divine favor on arrival
            if (KurinDemigodessMod.Settings.divineFavorEnabled)
            {
                var favor = Current.Game?.GetComponent<GameComponent_DivineFavor>();
                favor?.Add(20);
            }

            SendStandardLetter(
                new TaggedString("Kurin Pilgrimage"),
                new TaggedString(string.Format(
                    "A small group of {0} {1} arrives at the colony. They carry offerings and whispered prayers, " +
                    "hoping for a glimpse of Aethira Dawnforge. They will stay briefly before continuing their journey.",
                    count, sourceFaction.Name)),
                LetterDefOf.PositiveEvent,
                parms,
                new LookTargets(pawns));

            return true;
        }

        private static bool AethiraIsPresentOn(Map map)
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                    return true;
            }
            return false;
        }

        private static Faction FindSourceFaction()
        {
            // Prefer a non-hostile humanlike faction that isn't the player
            foreach (var f in Find.FactionManager.AllFactions)
            {
                if (f == null || f.IsPlayer) continue;
                if (f.defeated) continue;
                if (f.HostileTo(Faction.OfPlayer)) continue;
                if (f.def == null || !f.def.humanlikeFaction) continue;
                return f;
            }
            return null;
        }

        private static void SpawnOfferings(IntVec3 entry, Map map)
        {
            try
            {
                // Silver (always)
                TrySpawnOffering(ThingDefOf.Silver, Rand.Range(50, 150), entry, map);

                // Herbal medicine (if def exists)
                var herbal = DefDatabase<ThingDef>.GetNamedSilentFail("MedicineHerbal");
                if (herbal != null)
                {
                    TrySpawnOffering(herbal, Rand.Range(2, 6), entry, map);
                }

                // Wood (if def exists)
                var wood = DefDatabase<ThingDef>.GetNamedSilentFail("WoodLog");
                if (wood != null)
                {
                    TrySpawnOffering(wood, Rand.Range(40, 100), entry, map);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[KurinDemigodess] Pilgrim offerings failed: " + ex.Message);
            }
        }

        private static void TrySpawnOffering(ThingDef def, int stack, IntVec3 center, Map map)
        {
            if (def == null || stack <= 0) return;
            var thing = ThingMaker.MakeThing(def);
            int cap = def.stackLimit > 0 ? def.stackLimit : stack;
            thing.stackCount = System.Math.Min(stack, cap);
            var cell = CellFinder.RandomClosewalkCellNear(center, map, 5);
            GenSpawn.Spawn(thing, cell, map);
        }

        private static PawnKindDef PickKurinOrFallback()
        {
            // Find any PawnKindDef whose defName contains "Kurin" but NOT "Demigodess"
            var kurin = DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k != null && k.defName != null
                         && k.defName.IndexOf("Kurin", System.StringComparison.OrdinalIgnoreCase) >= 0
                         && k.defName.IndexOf("Demigodess", System.StringComparison.OrdinalIgnoreCase) < 0)
                .FirstOrDefault();

            if (kurin != null) return kurin;

            // Fallback to vanilla villager
            return PawnKindDefOf.Villager;
        }
    }
}
