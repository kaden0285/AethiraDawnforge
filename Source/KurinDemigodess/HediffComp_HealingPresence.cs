using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    public class HediffCompProperties_HealingPresence : HediffCompProperties
    {
        public float range = 50f;
        public float healAmount = 0.05f;
        public int tickInterval = 60; // every second

        public HediffCompProperties_HealingPresence()
        {
            compClass = typeof(HediffComp_HealingPresence);
        }
    }

    /// <summary>
    /// Healing aura that gives allies the same regen as the Demigodess herself:
    /// - 0.1 HP/sec on ALL injuries simultaneously
    /// - Reduces blood loss
    /// - Reduces blood loss
    /// - Regrows missing parts (one per hour)
    /// - Removes scars (one per 10 seconds)
    /// Does NOT resurrect the dead.
    /// </summary>
    public class HediffComp_HealingPresence : HediffComp
    {
        private int tickCounter;
        private int scarTickCounter;
        private int regenTickCounter;
        private int tendTickCounter;

        public HediffCompProperties_HealingPresence Props
        {
            get { return (HediffCompProperties_HealingPresence)props; }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            tickCounter++;
            scarTickCounter++;
            regenTickCounter++;
            tendTickCounter++;

            // Don't work while dead
            if (parent.pawn.Dead) return;

            // Every second: heal injuries + apply combat buff + stop bleeding
            if (tickCounter >= Props.tickInterval)
            {
                tickCounter = 0;
                HealAllies();
            }

            // Every 10 seconds: preserve corpses within range
            if (tendTickCounter % 600 == 0)
            {
                PreserveCorpses();
            }

            // Every 5 seconds: divinely close one wound per ally
            if (tendTickCounter >= 300)
            {
                tendTickCounter = 0;
                AutoTendAllies();
            }

            // Every 10 seconds: remove one scar per ally
            if (scarTickCounter >= 600)
            {
                scarTickCounter = 0;
                RemoveScars();
            }

            // Every hour: regrow one missing part per ally
            if (regenTickCounter >= 2500)
            {
                regenTickCounter = 0;
                RegrowParts();
            }
        }

        private void HealAllies()
        {
            if (!parent.pawn.Spawned) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;

            // Favor scales both range and heal amount (1x at 0 favor, 2x at max)
            float auraMult = GameComponent_DivineFavor.GetAuraMultiplier();
            float effectiveRange = Props.range * auraMult;

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var target = pawns[i];
                if (target == parent.pawn) continue;
                if (target.Dead || !target.IsColonist) continue;
                if (target.HostileTo(parent.pawn)) continue;
                if (target.Position.DistanceTo(pos) > effectiveRange) continue;

                // Apply or refresh divine inspiration combat buff (refresh timer to prevent flickering)
                if (Kurin_DefOf.DG_DivineInspirationHediff != null)
                {
                    var existingInspire = target.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_DivineInspirationHediff);
                    if (existingInspire != null)
                    {
                        var dc = existingInspire.TryGetComp<HediffComp_Disappears>();
                        if (dc != null) dc.ticksToDisappear = 600;
                    }
                    else
                    {
                        target.health.AddHediff(Kurin_DefOf.DG_DivineInspirationHediff);
                    }
                }

                // Reduce hunger (half food need while near Demigodess)
                if (target.needs != null && target.needs.food != null)
                {
                    // Add back a tiny bit of food to slow hunger rate by ~50%
                    target.needs.food.CurLevel += target.needs.food.FoodFallPerTick * 0.5f;
                }

                // Heal ALL injuries - scales with the same favor multiplier
                var injuries = target.health.hediffSet.hediffs
                    .OfType<Hediff_Injury>()
                    .Where(h => h.Severity > 0 && !h.IsPermanent())
                    .ToList();
                foreach (var injury in injuries)
                {
                    injury.Heal(Props.healAmount * auraMult);
                }

                // Reduce disease severity (help sick people heal faster)
                foreach (var hediff in target.health.hediffSet.hediffs.ToList())
                {
                    if (hediff.def.CompProps<HediffCompProperties_Immunizable>() != null && hediff.Severity > 0)
                    {
                        hediff.Severity -= 0.01f;
                        if (hediff.Severity < 0f) hediff.Severity = 0f;
                    }
                }

                // Reduce blood loss
                var bloodLoss = target.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
                if (bloodLoss != null)
                {
                    bloodLoss.Severity -= 0.02f;
                    if (bloodLoss.Severity <= 0f)
                    {
                        target.health.RemoveHediff(bloodLoss);
                    }
                }

            }
        }

        private void AutoTendAllies()
        {
            if (!parent.pawn.Spawned) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;
            float effectiveRange = Props.range * GameComponent_DivineFavor.GetAuraMultiplier();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var target = pawns[i];
                if (target == parent.pawn) continue;
                if (target.Dead || !target.IsColonist) continue;
                if (target.HostileTo(parent.pawn)) continue;
                if (target.Position.DistanceTo(pos) > effectiveRange) continue;

                // Find one untended injury and tend it at 50% quality
                var untended = target.health.hediffSet.hediffs
                    .OfType<Hediff_Injury>()
                    .Where(h => h.Severity > 0 && !h.IsTended())
                    .FirstOrDefault();

                if (untended != null)
                {
                    untended.Tended(0.5f, 0.5f);
                }
            }
        }

        private void PreserveCorpses()
        {
            if (!parent.pawn.Spawned) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;
            float effectiveRange = Props.range * GameComponent_DivineFavor.GetAuraMultiplier();

            // Find all corpses within range
            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i] as Corpse;
                if (corpse == null) continue;
                if (corpse.Position.DistanceTo(pos) > effectiveRange) continue;

                // Only preserve allied corpses (same faction or colonist)
                var innerPawn = corpse.InnerPawn;
                if (innerPawn == null) continue;
                if (innerPawn.Faction == null || !innerPawn.Faction.IsPlayer) continue;

                // Reset rot progress
                var compRottable = corpse.TryGetComp<CompRottable>();
                if (compRottable != null)
                {
                    compRottable.RotProgress = 0f;
                }
            }
        }

        private void RemoveScars()
        {
            if (!parent.pawn.Spawned) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;
            float effectiveRange = Props.range * GameComponent_DivineFavor.GetAuraMultiplier();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var target = pawns[i];
                if (target == parent.pawn) continue;
                if (target.Dead || !target.IsColonist) continue;
                if (target.HostileTo(parent.pawn)) continue;
                if (target.Position.DistanceTo(pos) > effectiveRange) continue;

                var scar = target.health.hediffSet.hediffs
                    .OfType<Hediff_Injury>()
                    .Where(h => h.IsPermanent())
                    .FirstOrDefault();
                if (scar != null)
                {
                    target.health.RemoveHediff(scar);
                }
            }
        }

        private void RegrowParts()
        {
            if (!parent.pawn.Spawned) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;
            float effectiveRange = Props.range * GameComponent_DivineFavor.GetAuraMultiplier();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var target = pawns[i];
                if (target == parent.pawn) continue;
                if (target.Dead || !target.IsColonist) continue;
                if (target.HostileTo(parent.pawn)) continue;
                if (target.Position.DistanceTo(pos) > effectiveRange) continue;

                // Use the same sequential regen system as the Demigodess but at double time
                var missingParts = target.health.hediffSet.GetMissingPartsCommonAncestors();
                if (missingParts.Any())
                {
                    var regen = Hediff_DivineRegenerating.GetOrCreate(target);
                    if (regen != null)
                    {
                        foreach (var missing in missingParts)
                        {
                            if (missing.Part != null)
                            {
                                regen.StartRegeneration(missing.Part);
                            }
                        }
                    }
                }
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref tickCounter, "DG_HealingTickCounter", 0);
            Scribe_Values.Look(ref scarTickCounter, "DG_ScarTickCounter", 0);
            Scribe_Values.Look(ref regenTickCounter, "DG_RegenTickCounter", 0);
            Scribe_Values.Look(ref tendTickCounter, "DG_TendTickCounter", 0);
        }
    }
}
