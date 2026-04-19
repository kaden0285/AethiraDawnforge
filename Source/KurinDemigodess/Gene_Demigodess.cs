using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Gene class used by DG_DivineVitality (the healing/immortality gene).
    /// Applies the DG_DemigodessPresence hediff which contains all aura comps + regen.
    /// Forces and maintains the Demigodess identity (name, age, backstories).
    /// Also provides the static HasDemigodessGene check used by all Harmony patches.
    ///
    /// Tick scheduling (Tier 3 #8 - staggered):
    ///  - every 60 ticks:              HealLivingInjuries
    ///  - every normalEnforceInterval: purge status, remove psylink, tend self, enforce food/regen, downed-check, scar removal
    ///  - every heavyEnforceInterval:  enforce identity/genes/traits/passions
    /// Defaults: normal=600, heavy=7500 (both user-configurable via mod settings).
    /// </summary>
    public class Gene_Demigodess : Gene
    {
        private const long BioAgeTicks = 18L * 3600000L;
        private const long ChronoAgeTicks = 541L * 3600000L;
        private int normalTickCounter;
        private int heavyTickCounter;
        private bool backstorySkillsApplied;

        // All 5 Demigodess gene defNames (used by HasDemigodessGene static check)
        private static readonly string[] DemigodessGeneDefNames = new string[]
        {
            "DG_DivineConstitution",
            "DG_DivineVitality",
            "DG_DivineGrace",
            "DG_DivinePresence"
        };

        // All genes that should be on the Demigodess (iterated in EnforceGenes)
        private static readonly string[] RequiredGeneDefNames = new string[]
        {
            "DG_HairSnowWhite",
            "Body_Standard",
            "Hands_Human",
            "Voice_Human",
            "Beard_NoBeardOnly",
            "Beauty_Beautiful",
            "Skin_Melanin2",
            "DG_DivineConstitution",
            "DG_DivineVitality",
            "DG_DivineGrace",
            "DG_DivinePresence"
        };

        // Allowed trait defNames (anything else gets removed)
        private static readonly string[] AllowedTraitDefNames = new string[]
        {
            "Nerves",              // Iron-Willed (degree 2)
            "Bisexual",            // Kurin racial trait
            "DG_DemigodessBeauty", // Custom divine beauty
            "DG_EternalWisdom",    // Custom eternal wisdom
        };

        public override void PostAdd()
        {
            base.PostAdd();
            ApplyPresenceHediff();
            ApplyLivingRegen();
            // Run all heavy enforcement once on gene add (Tier 3 #9: moved from per-tick)
            EnforceIdentity();
            EnforceGenes();
            EnforceTraits();
            EnforcePassions();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            RemovePresenceHediff();
        }

        public override void Tick()
        {
            base.Tick();
            var settings = KurinDemigodessMod.Settings;

            normalTickCounter++;
            heavyTickCounter++;

            // Heal injuries while alive (0.1 per second, every 60 ticks)
            if (!pawn.Dead && normalTickCounter % 60 == 0)
            {
                HealLivingInjuries();
            }

            // Normal enforcement cycle (Tier 3 #8)
            if (normalTickCounter >= settings.normalEnforceInterval)
            {
                normalTickCounter = 0;
                DemigodessHealing.PurgeHarmfulStatusEffects(pawn);
                DemigodessHealing.RemovePsylink(pawn);
                if (!pawn.Dead)
                {
                    EnforceMinFood();
                    ApplyLivingRegen();
                    AutoTendSelf();
                    CheckDownedAwayFromHome();
                    RemoveOneScar();
                }
            }

            // Heavy enforcement cycle (Tier 3 #8)
            if (heavyTickCounter >= settings.heavyEnforceInterval)
            {
                heavyTickCounter = 0;
                EnforceIdentity();
                EnforceGenes();
                EnforceTraits();
                EnforcePassions();
            }
        }

        private void EnforceTraits()
        {
            if (pawn.story == null || pawn.story.traits == null) return;

            // Remove any trait that isn't in our allowed list or a tech proficiency trait
            var traits = pawn.story.traits.allTraits.ToList();
            foreach (var trait in traits)
            {
                // Allow PE_ tech proficiency traits (progression mod)
                if (trait.def.defName.StartsWith("PE_")) continue;

                bool allowed = false;
                for (int i = 0; i < AllowedTraitDefNames.Length; i++)
                {
                    if (trait.def.defName == AllowedTraitDefNames[i])
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    pawn.story.traits.RemoveTrait(trait);
                }
            }

            // Make sure she HAS the required traits
            EnsureTrait("Nerves", 2);                                // Iron-Willed
            EnsureTrait("Bisexual", 0);                              // Bisexual
            EnsureTraitDef(Kurin_DefOf.DG_DemigodessBeauty, 0);      // Demigodess Beauty
            EnsureTraitDef(Kurin_DefOf.DG_EternalWisdom, 0);         // Eternal Wisdom
        }

        private void EnforcePassions()
        {
            if (pawn.skills == null || pawn.Dead) return;

            // Major passions
            EnsurePassion("Social", Passion.Major);
            EnsurePassion("Intellectual", Passion.Major);
            EnsurePassion("Medicine", Passion.Major);
            EnsurePassion("Cooking", Passion.Major);
            EnsurePassion("Plants", Passion.Major);

            // Minor passions
            EnsurePassion("Melee", Passion.Minor);
            EnsurePassion("Construction", Passion.Minor);
            EnsurePassion("Animals", Passion.Minor);

            // No passion
            RemovePassion("Shooting");
            RemovePassion("Mining");
            RemovePassion("Crafting");
            RemovePassion("Artistic");
        }

        private void EnsurePassion(string skillDefName, Passion passion)
        {
            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            if (skillDef == null) return;
            var skill = pawn.skills.GetSkill(skillDef);
            if (skill != null && skill.passion != passion)
            {
                skill.passion = passion;
            }
        }

        private void RemovePassion(string skillDefName)
        {
            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            if (skillDef == null) return;
            var skill = pawn.skills.GetSkill(skillDef);
            if (skill != null && skill.passion != Passion.None)
            {
                skill.passion = Passion.None;
            }
        }

        private void EnsureMinSkill(string skillDefName, int minLevel)
        {
            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            if (skillDef == null) return;
            var skill = pawn.skills.GetSkill(skillDef);
            if (skill != null && skill.Level < minLevel)
            {
                skill.Level = minLevel;
            }
        }

        private void EnsureTrait(string defName, int degree)
        {
            var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            if (traitDef == null) return;
            EnsureTraitDef(traitDef, degree);
        }

        private void EnsureTraitDef(TraitDef traitDef, int degree)
        {
            if (traitDef == null) return;

            if (!pawn.story.traits.HasTrait(traitDef, degree))
            {
                // Remove any existing degree of this trait first
                var existing = pawn.story.traits.GetTrait(traitDef);
                if (existing != null)
                {
                    pawn.story.traits.RemoveTrait(existing);
                }
                pawn.story.traits.GainTrait(new Trait(traitDef, degree));
            }
        }

        private void EnforceGenes()
        {
            if (pawn.genes == null) return;

            // Ensure Skin_Melanin2 endogene exists (her skin color)
            var skinDef = DefDatabase<GeneDef>.GetNamedSilentFail("Skin_Melanin2");
            if (skinDef != null)
            {
                bool hasSkin = false;
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (gene.def.defName == "Skin_Melanin2") { hasSkin = true; break; }
                }
                if (!hasSkin)
                {
                    pawn.genes.AddGene(skinDef, false); // false = endogene
                }
            }

            // Remove DG_DivineWrath if it still exists from old saves
            var wrathDef = DefDatabase<GeneDef>.GetNamedSilentFail("DG_DivineWrath");
            if (wrathDef != null)
            {
                foreach (var gene in pawn.genes.Xenogenes.ToList())
                {
                    if (gene.def == wrathDef)
                    {
                        pawn.genes.RemoveGene(gene);
                        break;
                    }
                }
            }

            // Remove any xenogenes that aren't in our required list
            var xenogenes = pawn.genes.Xenogenes.ToList();
            foreach (var gene in xenogenes)
            {
                bool isValid = false;
                for (int i = 0; i < RequiredGeneDefNames.Length; i++)
                {
                    if (gene.def.defName == RequiredGeneDefNames[i])
                    {
                        isValid = true;
                        break;
                    }
                }
                if (!isValid)
                {
                    pawn.genes.RemoveGene(gene);
                }
            }

            // Re-add any missing required genes
            for (int i = 0; i < RequiredGeneDefNames.Length; i++)
            {
                var geneDef = DefDatabase<GeneDef>.GetNamedSilentFail(RequiredGeneDefNames[i]);
                if (geneDef == null) continue;

                bool hasGene = false;
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (gene.def.defName == RequiredGeneDefNames[i])
                    {
                        hasGene = true;
                        break;
                    }
                }

                if (!hasGene)
                {
                    pawn.genes.AddGene(geneDef, true); // true = xenogene
                }
            }
        }

        private void EnforceMinFood()
        {
            if (pawn.Dead) return;
            if (pawn.needs == null) return;
            var food = pawn.needs.food;
            if (food != null && food.CurLevelPercentage < 0.1f)
            {
                food.CurLevelPercentage = 0.1f;
            }
        }

        private void AutoTendSelf()
        {
            var untended = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.Severity > 0 && !h.IsTended())
                .FirstOrDefault();

            if (untended != null)
            {
                untended.Tended(0.5f, 0.5f);
            }
        }

        private void RemoveOneScar()
        {
            var scar = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.IsPermanent())
                .FirstOrDefault();
            if (scar != null)
                pawn.health.RemoveHediff(scar);
        }

        private void CheckDownedAwayFromHome()
        {
            if (pawn.Dead) return;
            if (!pawn.Downed) return;
            if (pawn.Map == null) return;
            if (pawn.Map.IsPlayerHome) return;

            // She's downed on a non-home map - teleport her home in a coma
            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (homeMap == null) return;

            // Despawn from current map
            pawn.DeSpawn();

            // Find spawn position (prefer shrine)
            IntVec3 spawnPos = CellFinder.RandomEdgeCell(homeMap);
            if (Kurin_DefOf.DG_DivineShrine != null)
            {
                var shrines = homeMap.listerThings.ThingsOfDef(Kurin_DefOf.DG_DivineShrine);
                if (shrines != null && shrines.Count > 0)
                {
                    foreach (var cell in GenAdj.CellsAdjacent8Way(shrines.First()))
                    {
                        if (cell.InBounds(homeMap) && cell.Standable(homeMap))
                        {
                            spawnPos = cell;
                            break;
                        }
                    }
                }
            }

            GenSpawn.Spawn(pawn, spawnPos, homeMap);

            // Apply 7-day divine recovery coma - she keeps all her wounds
            if (Kurin_DefOf.DG_DivineRecoveryComa != null && !pawn.health.hediffSet.HasHediff(Kurin_DefOf.DG_DivineRecoveryComa))
            {
                pawn.health.AddHediff(Kurin_DefOf.DG_DivineRecoveryComa);
            }

            Messages.Message(
                "The Demigodess was downed in enemy territory. Divine power has brought her home, but she lies in a deep recovery coma.",
                pawn, MessageTypeDefOf.NegativeEvent, false);

            Find.LetterStack.ReceiveLetter(
                "The Demigodess Returns Wounded",
                "Aethira Dawnforge was struck down far from home. Her body has been transported back to the colony through divine will, but she carries all her wounds. She will awaken from her recovery coma in 7 days.",
                LetterDefOf.NegativeEvent, pawn);
        }

        private void HealLivingInjuries()
        {
            // Heal all non-permanent injuries at 0.1 per tick-batch
            var injuries = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.Severity > 0 && !h.IsPermanent())
                .ToList();
            foreach (var injury in injuries)
            {
                injury.Heal(0.1f);
            }

            // Remove blood loss
            var bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                bloodLoss.Severity -= 0.02f;
                if (bloodLoss.Severity <= 0f)
                    pawn.health.RemoveHediff(bloodLoss);
            }
        }

        private void ApplyLivingRegen()
        {
            // Apply the regenerating hediff while alive - handles injury healing,
            // part regrowth, scar removal, blood loss reduction
            if (pawn.Dead) return;

            // Check for missing parts and start regen
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (missingParts.Any())
            {
                var regen = Hediff_DivineRegenerating.GetOrCreate(pawn);
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

        /// <summary>
        /// Called from WorldComponent while dead to keep enforcement running.
        /// </summary>
        public void ForceEnforceWhileDead()
        {
            EnforceIdentity();
            EnforceGenes();
            EnforceTraits();
            DemigodessHealing.PurgeHarmfulStatusEffects(pawn);
            DemigodessHealing.RemovePsylink(pawn);
        }

        /// <summary>
        /// Forces name, age, and backstories. Called on add and periodically.
        /// </summary>
        private void EnforceIdentity()
        {
            // Force biological age to 18 - she does not age
            if (pawn.ageTracker != null)
            {
                if (pawn.ageTracker.AgeBiologicalTicks != BioAgeTicks)
                {
                    pawn.ageTracker.AgeBiologicalTicks = BioAgeTicks;
                }
                // Chronological age should only go UP, never down
                if (pawn.ageTracker.AgeChronologicalTicks < ChronoAgeTicks)
                {
                    pawn.ageTracker.AgeChronologicalTicks = ChronoAgeTicks;
                }
            }

            // Force name - she is always Aethira Dawnforge
            var correctName = new NameTriple("Aethira", "Aethira", "Dawnforge");
            if (pawn.Name == null || pawn.Name.ToStringFull != correctName.ToStringFull)
            {
                pawn.Name = correctName;
            }

            // Force appearance
            if (pawn.story != null)
            {
                // Hair style
                var hairDef = DefDatabase<HairDef>.GetNamedSilentFail("DRNTF_Hair_01");
                if (hairDef != null && pawn.story.hairDef != hairDef)
                {
                    pawn.story.hairDef = hairDef;
                }

                // Force snow white hair color
                var snowWhite = new UnityEngine.Color(1.0f, 1.0f, 1.0f, 1f);
                if (pawn.story.HairColor != snowWhite)
                {
                    pawn.story.HairColor = snowWhite;
                }

                // Force favorite color to white
                var whiteColor = DefDatabase<ColorDef>.GetNamedSilentFail("Structure_White");
                if (whiteColor != null && pawn.story.favoriteColor != whiteColor)
                {
                    pawn.story.favoriteColor = whiteColor;
                }

                // Head type
                var headType = DefDatabase<HeadTypeDef>.GetNamedSilentFail("Female_AveragePointy");
                if (headType != null && pawn.story.headType != headType)
                {
                    pawn.story.headType = headType;
                }

                // Body type
                var bodyType = DefDatabase<BodyTypeDef>.GetNamedSilentFail("Female");
                if (bodyType != null && pawn.story.bodyType != bodyType)
                {
                    pawn.story.bodyType = bodyType;
                }
            }

            // Force straight ears (HAR addonVariants) and jade green eyes (Facial Animation)
            AppearanceEnforcer.Enforce(pawn);

            // Force no beard (Kurins are all-female, no facial hair ever)
            if (pawn.style != null && BeardDefOf.NoBeard != null && pawn.style.beardDef != BeardDefOf.NoBeard)
            {
                pawn.style.beardDef = BeardDefOf.NoBeard;
            }

            // Force tattoo
            if (pawn.style != null)
            {
                var bodyTattoo = DefDatabase<TattooDef>.GetNamedSilentFail("STechTat_Body_BlackCircuit");
                if (bodyTattoo != null && pawn.style.BodyTattoo != bodyTattoo)
                {
                    pawn.style.BodyTattoo = bodyTattoo;
                }

                var faceTattoo = DefDatabase<TattooDef>.GetNamedSilentFail("NoTattoo_Face");
                if (faceTattoo != null && pawn.style.FaceTattoo != faceTattoo)
                {
                    pawn.style.FaceTattoo = faceTattoo;
                }
            }

            // Force backstories (using Kurin_DefOf for compile-time safety)
            if (pawn.story != null)
            {
                bool childChanged = false;
                if (Kurin_DefOf.DG_BackstoryChild_DivineFoxKit != null && pawn.story.Childhood != Kurin_DefOf.DG_BackstoryChild_DivineFoxKit)
                {
                    pawn.story.Childhood = Kurin_DefOf.DG_BackstoryChild_DivineFoxKit;
                    childChanged = true;
                }

                bool adultChanged = false;
                if (Kurin_DefOf.DG_BackstoryAdult_KurinDemigodess != null && pawn.story.Adulthood != Kurin_DefOf.DG_BackstoryAdult_KurinDemigodess)
                {
                    pawn.story.Adulthood = Kurin_DefOf.DG_BackstoryAdult_KurinDemigodess;
                    adultChanged = true;
                }

                // Apply backstory skill gains once when backstories are swapped
                if ((childChanged || adultChanged || !backstorySkillsApplied) && pawn.skills != null)
                {
                    backstorySkillsApplied = true;
                    // Combined minimum from both backstories
                    EnsureMinSkill("Melee", 9);
                    EnsureMinSkill("Social", 9);
                    EnsureMinSkill("Intellectual", 5);
                    EnsureMinSkill("Medicine", 2);
                    EnsureMinSkill("Construction", 2);
                }
            }
        }

        private void ApplyPresenceHediff()
        {
            if (Kurin_DefOf.DG_DemigodessPresence != null && !pawn.health.hediffSet.HasHediff(Kurin_DefOf.DG_DemigodessPresence))
            {
                pawn.health.AddHediff(Kurin_DefOf.DG_DemigodessPresence);
            }
        }

        private void RemovePresenceHediff()
        {
            if (Kurin_DefOf.DG_DemigodessPresence != null)
            {
                var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_DemigodessPresence);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        /// <summary>
        /// Returns true if the pawn has ANY of the 5 Demigodess genes active.
        /// Used by all Harmony patches (damage cap, deathless, disease immunity, anti-kidnap).
        /// </summary>
        public static bool HasDemigodessGene(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null) return false;
            foreach (var gene in pawn.genes.GenesListForReading)
            {
                if (!gene.Active) continue;
                for (int i = 0; i < DemigodessGeneDefNames.Length; i++)
                {
                    if (gene.def.defName == DemigodessGeneDefNames[i])
                        return true;
                }
            }
            return false;
        }
    }
}
