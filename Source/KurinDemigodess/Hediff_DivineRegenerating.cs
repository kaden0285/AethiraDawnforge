using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Handles sequential body part regrowth.
    /// Based on Immortal Regenerant's Hediff_Regenerating.
    /// Each missing part gets a timer. When the timer expires,
    /// the part is restored and child parts are re-added as missing
    /// (segment by segment regrowth).
    /// </summary>
    public class Hediff_DivineRegenerating : HediffWithComps
    {
        public List<RegenPartState> activeRegens = new List<RegenPartState>();
        private int emptyTicks; // delay before self-removal to prevent flickering

        private static readonly Dictionary<string, float> RegenHours = new Dictionary<string, float>
        {
            { "Finger", 4f },
            { "Toe", 4f },
            { "Tongue", 6f },
            { "Eye", 18f },
            { "Ear", 18f },
            { "Nose", 18f },
            { "Hand", 12f },
            { "Foot", 12f },
            { "Arm", 24f },
            { "Leg", 24f },
            { "Jaw", 24f },
            { "Heart", 24f },
            { "Lung", 24f },
            { "Kidney", 24f },
            { "Liver", 24f },
            { "Stomach", 24f },
            { "Head", 16f },
            { "Neck", 16f },
            { "Spine", 16f },
            { "Brain", 24f },
            // Kurin parts
            { "Kurin_Tail", 6f },
            { "Kurin_Ear", 6f },
        };

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activeRegens, "activeRegens", LookMode.Deep);
            Scribe_Values.Look(ref emptyTicks, "emptyTicks", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && activeRegens == null)
            {
                activeRegens = new List<RegenPartState>();
            }
        }

        public override bool Visible
        {
            get { return activeRegens != null && activeRegens.Count > 0; }
        }

        public static Hediff_DivineRegenerating GetOrCreate(Pawn pawn)
        {
            if (pawn == null) return null;
            var def = Kurin_DefOf.DG_DivineRegenerating;
            if (def == null) return null;
            var existing = pawn.health.hediffSet.hediffs
                .OfType<Hediff_DivineRegenerating>()
                .FirstOrDefault(h => h.def == def);
            if (existing != null) return existing;

            var hediff = (Hediff_DivineRegenerating)HediffMaker.MakeHediff(def, pawn);
            pawn.health.AddHediff(hediff);
            return hediff;
        }

        public void StartRegeneration(BodyPartRecord part)
        {
            if (pawn == null || part == null) return;
            if (!pawn.health.hediffSet.PartIsMissing(part)) return;
            if (activeRegens.Any(r => r.part == part)) return;

            int ticks = GetRegenerationTicks(part);

            // Double regen time for non-Demigodess pawns (allies get slower regen)
            if (!Gene_Demigodess.HasDemigodessGene(pawn))
            {
                ticks *= 2;
            }

            activeRegens.Add(new RegenPartState
            {
                part = part,
                ticksLeft = ticks,
                initialTicks = ticks
            });
        }

        public static int GetRegenerationTicks(BodyPartRecord part)
        {
            string defName = part.def.defName;
            foreach (var kvp in RegenHours)
            {
                if (defName.Contains(kvp.Key))
                {
                    return (int)(kvp.Value * 2500f);
                }
            }
            // Default: 24 hours for unknown parts
            return (int)(24f * 2500f);
        }

        public override void Tick()
        {
            base.Tick();
            if (pawn == null) return;
            Advance(1);
        }

        public void Advance(int ticks)
        {
            if (pawn == null || activeRegens == null || activeRegens.Count == 0) return;

            var completed = new List<RegenPartState>();
            var toRemove = new List<RegenPartState>();

            foreach (var state in activeRegens)
            {
                if (state.part == null || !pawn.health.hediffSet.PartIsMissing(state.part))
                {
                    toRemove.Add(state);
                    continue;
                }

                state.ticksLeft -= ticks;
                if (state.ticksLeft <= 0)
                {
                    completed.Add(state);
                }
            }

            foreach (var state in completed)
            {
                RegenerateBodyPart(state);
                toRemove.Add(state);
            }

            foreach (var state in toRemove.Distinct())
            {
                activeRegens.Remove(state);
            }

            // Don't remove self the instant activeRegens hits 0.
            // Wait 300 ticks (5s) in case new child parts are about to be queued
            // by Gene_Demigodess.ApplyLivingRegen on its next tick. Prevents flicker.
            if (activeRegens.Count == 0)
            {
                emptyTicks += ticks;
                if (emptyTicks >= 300 && pawn.health != null)
                {
                    pawn.health.RemoveHediff(this);
                }
            }
            else
            {
                emptyTicks = 0;
            }
        }

        private void RegenerateBodyPart(RegenPartState state)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null) return;
            if (state == null || state.part == null) return;

            var part = state.part;

            // Restore the part
            pawn.health.RestorePart(part);

            Messages.Message(
                string.Format("Aethira has regrown her {0}!", part.def.label),
                pawn, MessageTypeDefOf.PositiveEvent, false);

            // Add a wound to the restored part (it just regrew, it's tender)
            float maxHealth = part.def.GetMaxHealth(pawn);
            if (maxHealth > 1f)
            {
                var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
                injury.Severity = maxHealth * 0.5f;
                pawn.health.AddHediff(injury);
            }

            // Re-add child parts as missing (segment by segment regrowth)
            if (part.parts != null)
            {
                foreach (var childPart in part.parts)
                {
                    if (!pawn.health.hediffSet.PartIsMissing(childPart))
                    {
                        var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(
                            HediffDefOf.MissingBodyPart, pawn, childPart);
                        missing.lastInjury = HediffDefOf.Cut;
                        pawn.health.AddHediff(missing, childPart);
                    }
                }
            }
        }

        public override string LabelInBrackets
        {
            get
            {
                if (activeRegens == null || activeRegens.Count == 0)
                    return "no active regeneration";

                var parts = activeRegens
                    .Where(r => r.part != null && r.ticksLeft > 0)
                    .OrderBy(r => r.ticksLeft)
                    .Take(4);

                var sb = new StringBuilder();
                bool first = true;
                foreach (var r in parts)
                {
                    if (!first) sb.Append("; ");
                    first = false;
                    int hours = Math.Max(0, r.ticksLeft) / 2500;
                    int days = hours / 24;
                    int remainHours = hours % 24;
                    if (days > 0)
                        sb.Append(string.Format("{0}: ~{1}d {2}h", r.part.def.label, days, remainHours));
                    else
                        sb.Append(string.Format("{0}: ~{1}h", r.part.def.label, hours));
                }
                return sb.ToString();
            }
        }

        public override string TipStringExtra
        {
            get
            {
                if (activeRegens == null || activeRegens.Count == 0)
                    return "No parts currently regenerating.";

                var sb = new StringBuilder();
                foreach (var r in activeRegens.Where(r => r.part != null).OrderBy(r => r.ticksLeft))
                {
                    int hours = Math.Max(0, r.ticksLeft) / 2500;
                    int days = hours / 24;
                    int remainHours = hours % 24;
                    if (days > 0)
                        sb.AppendLine(string.Format("{0}: ~{1}d {2}h until restored", r.part.def.label, days, remainHours));
                    else
                        sb.AppendLine(string.Format("{0}: ~{1}h until restored", r.part.def.label, hours));
                }
                return sb.ToString().TrimEnd();
            }
        }

        public class RegenPartState : IExposable
        {
            public BodyPartRecord part;
            public int ticksLeft;
            public int initialTicks;

            public void ExposeData()
            {
                Scribe_BodyParts.Look(ref part, "part");
                Scribe_Values.Look(ref ticksLeft, "ticksLeft", 0);
                Scribe_Values.Look(ref initialTicks, "initialTicks", 0);
            }
        }
    }
}
