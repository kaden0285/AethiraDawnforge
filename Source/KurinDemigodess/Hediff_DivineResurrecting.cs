using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Applied to the Demigodess's corpse after death.
    /// Ticks on the dead pawn, healing injuries and triggering part regrowth.
    /// Once brain + head are restored and no lethal conditions remain,
    /// auto-resurrects via ResurrectionUtility.TryResurrect.
    /// Based on Immortal Regenerant's Hediff_Resurrection.
    /// </summary>
    public class Hediff_DivineResurrecting : HediffWithComps
    {
        private int ticksSinceDeath;
        private string currentActivity = "waiting";
        private int resurrectionDelay = -1;
        public bool applyComaAfterResurrect;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksSinceDeath, "ticksSinceDeath", 0);
            Scribe_Values.Look(ref currentActivity, "currentActivity", "waiting");
            Scribe_Values.Look(ref resurrectionDelay, "resurrectionDelay", -1);
            Scribe_Values.Look(ref applyComaAfterResurrect, "applyComaAfterResurrect", false);
        }

        public override void Tick()
        {
            base.Tick();
            // Dead pawns don't get health ticks. ManualTick is called from WorldComponent.
            // This only fires if she's somehow alive with this hediff (shouldn't happen).
            if (pawn != null && !pawn.Dead)
            {
                pawn.health.RemoveHediff(this);
            }
        }

        /// <summary>
        /// Called from WorldComponent every 250 ticks since dead pawns don't get health ticks.
        /// </summary>
        public void ManualTick(int ticks)
        {
            if (pawn == null) return;

            // If pawn isn't dead anymore (something else resurrected them), remove self
            if (!pawn.Dead)
            {
                pawn.health.RemoveHediff(this);
                return;
            }

            // Initialize resurrection delay (3-6 hours after vital organs restored)
            if (resurrectionDelay == -1)
            {
                resurrectionDelay = Rand.Range(7500, 15000);
            }

            ticksSinceDeath += ticks;
            // Prevent corpse from rotting
            PreserveCorpse();

            // First 200 ticks: preparation phase
            if (ticksSinceDeath < 200)
            {
                currentActivity = "divine energy gathering";
                return;
            }

            // Check for missing parts and start regen
            CheckMissingParts();

            // Heal injuries and lethal conditions every tick batch
            HealConditions();

            // Check if ready to resurrect
            if (IsReadyToResurrect())
            {
                currentActivity = "resurrection";
                DoResurrect();
                return;
            }

            // Update status label
            UpdateStatusLabel();

            // Daily status update message
            if (ticksSinceDeath % 60000 < ticks)
            {
                SendDailyUpdate();
            }
        }

        private void CheckMissingParts()
        {
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors().ToList();
            if (missingParts.Count == 0) return;

            var regen = Hediff_DivineRegenerating.GetOrCreate(pawn);
            if (regen == null) return;

            foreach (var missing in missingParts)
            {
                if (missing.Part != null)
                {
                    regen.StartRegeneration(missing.Part);
                }
            }
        }

        private void HealConditions()
        {
            foreach (var hediff in pawn.health.hediffSet.hediffs.ToList())
            {
                // Heal all injuries at 0.05 per second
                var injury = hediff as Hediff_Injury;
                if (injury != null && injury.Severity > 0)
                {
                    injury.Heal(0.05f);
                    continue;
                }

                // Remove malnutrition
                if (hediff.def == HediffDefOf.Malnutrition)
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Remove catatonic breakdown
                if (hediff.def.defName == "CatatonicBreakdown")
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Reduce blood loss
                if (hediff.def == HediffDefOf.BloodLoss)
                {
                    if (hediff.Severity > 0f)
                    {
                        hediff.Severity -= 0.02f;
                        if (hediff.Severity < 0f) hediff.Severity = 0f;
                    }
                    continue;
                }

                // Reduce any lethal hediff
                if (hediff.def.lethalSeverity > 0f && hediff.Severity > 0f)
                {
                    hediff.Severity -= 0.01f;
                    if (hediff.Severity < 0f) hediff.Severity = 0f;
                }
            }
        }

        private void PreserveCorpse()
        {
            if (pawn == null || !pawn.Dead) return;
            var corpse = pawn.Corpse;
            if (corpse == null) return;
            var comp = corpse.TryGetComp<CompRottable>();
            if (comp != null)
            {
                comp.RotProgress = 0f;
            }
        }

        private bool IsReadyToResurrect()
        {
            if (ticksSinceDeath < resurrectionDelay) return false;

            // Need head
            var head = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName == "Head");
            if (head != null && pawn.health.hediffSet.PartIsMissing(head)) return false;

            // Need brain
            var brain = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName == "Brain");
            if (brain != null && pawn.health.hediffSet.PartIsMissing(brain)) return false;

            // Remove catatonic breakdown if present (it blocks resurrection)
            var catatonic = pawn.health.hediffSet.hediffs
                .FirstOrDefault(h => h.def.defName == "CatatonicBreakdown");
            if (catatonic != null)
            {
                pawn.health.RemoveHediff(catatonic);
            }

            // Remove malnutrition if present
            var malnutrition = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition);
            if (malnutrition != null)
            {
                pawn.health.RemoveHediff(malnutrition);
            }

            // No lethal hediffs remaining
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def.lethalSeverity > 0f && hediff.Severity >= hediff.def.lethalSeverity)
                    return false;
            }

            return true;
        }

        private void DoResurrect()
        {
            try
            {
                // Remember non-vital missing parts (will continue regrowing after resurrection)
                var nonVitalMissing = pawn.health.hediffSet.GetMissingPartsCommonAncestors()
                    .Where(m => m.Part != null && !IsVitalPart(m.Part))
                    .Select(m => m.Part)
                    .Distinct()
                    .ToList();

                // Remember current injuries
                var existingInjuries = new List<Tuple<BodyPartRecord, HediffDef, float>>();
                foreach (var injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>())
                {
                    existingInjuries.Add(new Tuple<BodyPartRecord, HediffDef, float>(
                        injury.Part, injury.def, injury.Severity));
                }

                // Allow corpse despawn/destroy AND world pawn removal during resurrection
                CorpseProtection_DeSpawn_Patch.allowResurrection = true;
                CorpseProtection_Destroy_Patch.allowResurrection = true;
                WorldPawns_Remove_Patch.allowRemoval = true;

                // RESURRECT
                ResurrectionUtility.TryResurrect(pawn);

                CorpseProtection_DeSpawn_Patch.allowResurrection = false;
                CorpseProtection_Destroy_Patch.allowResurrection = false;
                WorldPawns_Remove_Patch.allowRemoval = false;

                if (pawn.Dead)
                {
                    Log.Warning("[KurinDemigodess] ResurrectionUtility.TryResurrect failed for Aethira!");
                    return;
                }

                // Re-apply injuries that existed before resurrection
                foreach (var injuryData in existingInjuries)
                {
                    if (injuryData.Item1 == null || !pawn.health.hediffSet.PartIsMissing(injuryData.Item1))
                    {
                        var newInjury = (Hediff_Injury)HediffMaker.MakeHediff(injuryData.Item2, pawn, injuryData.Item1);
                        newInjury.Severity = injuryData.Item3;
                        pawn.health.AddHediff(newInjury);
                    }
                }

                // Re-add non-vital missing parts and continue their regen
                if (nonVitalMissing.Count > 0)
                {
                    var regen = Hediff_DivineRegenerating.GetOrCreate(pawn);
                    foreach (var part in nonVitalMissing)
                    {
                        if (part != null)
                        {
                            if (!pawn.health.hediffSet.PartIsMissing(part))
                            {
                                var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(
                                    HediffDefOf.MissingBodyPart, pawn, part);
                                missing.lastInjury = HediffDefOf.Cut;
                                pawn.health.AddHediff(missing);
                            }
                            regen.StartRegeneration(part);
                        }
                    }
                }

                // Remove resurrection sickness/psychosis
                foreach (var hediff in pawn.health.hediffSet.hediffs
                    .Where(h => h.def.defName.Contains("Coma") ||
                                h.def.defName.Contains("Sickness") ||
                                h.def.defName == "ResurrectionPsychosis" ||
                                h.def.defName == "ResurrectionSickness" ||
                                h.def.defName == "DeathlessRegeneration")
                    .ToList())
                {
                    pawn.health.RemoveHediff(hediff);
                }

                // Remove corpse preservation hediff
                if (Kurin_DefOf.DG_CorpsePreservation != null)
                {
                    var preserve = pawn.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_CorpsePreservation);
                    if (preserve != null) pawn.health.RemoveHediff(preserve);
                }

                // Remove this resurrection hediff
                pawn.health.RemoveHediff(this);

                // Re-apply divine presence
                if (Kurin_DefOf.DG_DemigodessPresence != null && !pawn.health.hediffSet.HasHediff(Kurin_DefOf.DG_DemigodessPresence))
                {
                    pawn.health.AddHediff(Kurin_DefOf.DG_DemigodessPresence);
                }

                // Max her needs - she just reformed, she shouldn't wake up starving/exhausted
                WorldComponent_DemigodessTracker.MaxAllNeeds(pawn);

                // ResurrectionUtility.TryResurrect can reset work priorities, policies, and
                // player settings. Re-apply the settings-only portion of the disk snapshot
                // so the player doesn't lose their configured work assignments, medical care
                // level, outfit/drug/food policies, etc. on every death.
                try
                {
                    var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
                    if (tracker != null && !string.IsNullOrEmpty(tracker.WorldUuid))
                    {
                        DemigodessSnapshot.LoadAndApply(pawn, tracker.WorldUuid, skipIdentityData: true);
                    }
                }
                catch (Exception snapEx)
                {
                    Log.Warning("[KurinDemigodess] Post-resurrect settings restore failed: " + snapEx.Message);
                }

                // Colony notification
                if (Kurin_DefOf.DG_DemigodessReturns != null && pawn.Map != null)
                {
                    foreach (var colonist in pawn.Map.mapPawns.FreeColonistsSpawned)
                    {
                        if (colonist != pawn && colonist.needs != null && colonist.needs.mood != null)
                        {
                            colonist.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_DemigodessReturns);
                        }
                    }
                }

                // Apply 7-day recovery coma if she died away from home
                if (applyComaAfterResurrect)
                {
                    if (Kurin_DefOf.DG_DivineRecoveryComa != null && !pawn.health.hediffSet.HasHediff(Kurin_DefOf.DG_DivineRecoveryComa))
                    {
                        pawn.health.AddHediff(Kurin_DefOf.DG_DivineRecoveryComa);
                    }

                    Messages.Message(
                        "The Demigodess has resurrected, but lies in a deep recovery coma. She will awaken in 7 days.",
                        pawn, MessageTypeDefOf.NeutralEvent, false);

                    Find.LetterStack.ReceiveLetter(
                        "The Demigodess Rises, But Rests",
                        "Aethira Dawnforge has resurrected after being brought home through divine power. She is alive, but lies in a deep recovery coma. The ordeal of dying far from home has taken its toll. She will awaken in 7 days.",
                        LetterDefOf.NeutralEvent, pawn);
                }
                else
                {
                    Messages.Message(
                        "The Demigodess has resurrected! Aethira Dawnforge rises once more!",
                        pawn, MessageTypeDefOf.PositiveEvent, false);

                    Find.LetterStack.ReceiveLetter(
                        "The Demigodess Rises",
                        "Aethira Dawnforge's body has finished regenerating. Her eyes open, divine light fills the air. She is alive once more.",
                        LetterDefOf.PositiveEvent, pawn);
                }
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("[KurinDemigodess] Error during resurrection: {0}", ex));
            }
        }

        private bool IsVitalPart(BodyPartRecord part)
        {
            if (part == null) return false;
            string defName = part.def.defName;
            return defName == "Head" || defName == "Neck" || defName == "Torso" ||
                   defName == "Heart" || defName == "Liver" || defName == "Stomach" ||
                   defName == "LeftLung" || defName == "RightLung";
        }

        private void UpdateStatusLabel()
        {
            if (ticksSinceDeath < resurrectionDelay)
            {
                currentActivity = "regenerating";
                return;
            }

            var head = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName == "Head");
            var brain = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName == "Brain");

            if (head != null && pawn.health.hediffSet.PartIsMissing(head))
            {
                currentActivity = "regrowing head";
                return;
            }
            if (brain != null && pawn.health.hediffSet.PartIsMissing(brain))
            {
                currentActivity = "restoring brain";
                return;
            }

            var lethal = pawn.health.hediffSet.hediffs
                .FirstOrDefault(h => h.def.lethalSeverity > 0f && h.Severity >= h.def.lethalSeverity);
            if (lethal != null)
            {
                currentActivity = string.Format("healing: {0}", lethal.Label);
                return;
            }

            if (pawn.health.hediffSet.GetMissingPartsCommonAncestors().Any())
            {
                currentActivity = "regenerating tissues";
                return;
            }

            currentActivity = "healing injuries";
        }

        private void SendDailyUpdate()
        {
            var missingCount = pawn.health.hediffSet.GetMissingPartsCommonAncestors().Count();
            var injuryCount = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count(h => h.Severity > 0);

            string msg;
            if (missingCount > 0)
                msg = string.Format("Aethira's body continues to regenerate. {0} parts regrowing, {1} injuries healing. Status: {2}",
                    missingCount, injuryCount, currentActivity);
            else if (injuryCount > 0)
                msg = string.Format("Aethira is nearly restored. {0} injuries healing. Resurrection imminent.", injuryCount);
            else
                msg = "Aethira's regeneration is nearly complete. She will rise soon.";

            Messages.Message(msg, pawn, MessageTypeDefOf.NeutralEvent, false);
        }

        public override string LabelInBrackets
        {
            get
            {
                if (pawn != null && pawn.Dead && resurrectionDelay > 0)
                {
                    int remaining = Math.Max(0, resurrectionDelay - ticksSinceDeath);
                    if (remaining > 0)
                    {
                        int hours = remaining / 2500;
                        int mins = (remaining % 2500) * 60 / 2500;
                        if (hours > 0)
                            return string.Format("{0} (~{1}h {2}m)", currentActivity, hours, mins);
                        return string.Format("{0} (~{1}m)", currentActivity, mins);
                    }
                }
                return currentActivity;
            }
        }
    }
}
