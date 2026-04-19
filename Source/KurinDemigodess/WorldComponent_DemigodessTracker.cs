using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace KurinDemigodess
{
    /// <summary>
    /// Tracks Demigodess pawns during Divine Ascension (both brain+heart destroyed).
    /// Stores despawned pawn data and counts down 7 days for respawn.
    /// Runs the unified failsafe scan every 250 ticks: maintains the backup
    /// reference, repairs living pawns, ticks dead corpses, and triggers emergency
    /// respawn after N consecutive scans where Aethira is nowhere to be found.
    /// </summary>
    public class WorldComponent_DemigodessTracker : WorldComponent
    {
        private const int SnapshotIntervalTicks = 15000; // ~4 in-game minutes between disk writes
        private static readonly string LogFilePath = Path.Combine(GenFilePaths.ConfigFolderPath, "KurinDemigodess_FailsafeLog.txt");

        private List<AscensionData> ascendedDemigodesses = new List<AscensionData>();
        private Pawn savedDemigodess; // Backup reference to the Demigodess pawn
        private int consecutiveMissCount; // Tier 1 #1: anti-duplication - require N missed scans
        private bool firstScanLogged;
        private string worldUuid; // Per-save UUID for isolating the on-disk snapshot file (Layer 7)
        private int ticksSinceLastSnapshot; // Throttle for disk-snapshot writes

        public WorldComponent_DemigodessTracker(World world) : base(world)
        {
        }

        // ===== Debug / inspection accessors =====

        public int ConsecutiveMissCount
        {
            get { return consecutiveMissCount; }
            set { consecutiveMissCount = value; }
        }

        public Pawn SavedDemigodess
        {
            get { return savedDemigodess; }
        }

        public int AscensionCount
        {
            get { return ascendedDemigodesses.Count; }
        }

        /// <summary>
        /// Minimum ticksRemaining across all currently-ascending Demigodesses.
        /// Returns -1 if none are ascending. Used by Alert_DemigodessAscending
        /// to display a live countdown in the bottom-right alert list.
        /// </summary>
        public int MinAscensionTicksRemaining()
        {
            if (ascendedDemigodesses == null || ascendedDemigodesses.Count == 0) return -1;
            int min = int.MaxValue;
            foreach (var data in ascendedDemigodesses)
            {
                if (data == null) continue;
                if (data.ticksRemaining < min) min = data.ticksRemaining;
            }
            return min == int.MaxValue ? -1 : min;
        }

        /// <summary>
        /// Per-save UUID used to isolate the on-disk snapshot file.
        /// Read-only accessor for code that needs to call DemigodessSnapshot methods
        /// (e.g., Hediff_DivineResurrecting when corpse resurrection completes).
        /// </summary>
        public string WorldUuid
        {
            get { return worldUuid; }
        }

        public void ForceEmergencyRespawn()
        {
            EmergencyRespawn();
        }

        /// <summary>
        /// Sets every need (food, rest, joy, mood, etc.) to its maximum value.
        /// Called whenever Aethira reforms - she should never come back starving,
        /// exhausted, or miserable on arrival.
        /// </summary>
        public static void MaxAllNeeds(Pawn pawn)
        {
            if (pawn == null || pawn.needs == null) return;
            try
            {
                var all = pawn.needs.AllNeeds;
                if (all == null) return;
                foreach (var need in all)
                {
                    if (need == null) continue;
                    try { need.CurLevelPercentage = 1f; }
                    catch { /* some needs reject direct writes; skip */ }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] MaxAllNeeds failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Strips all apparel, equipment, and inventory from the pawn.
        /// Called when Aethira is rebuilt "from nothing" (ascension return or
        /// emergency respawn fallback). Narrative rule: if her body dissolved,
        /// her gear cannot teleport in with her.
        /// </summary>
        private static void StripAllGear(Pawn pawn, string reasonLabel)
        {
            if (pawn == null) return;
            try
            {
                int apparelCount = 0;
                int equipCount = 0;
                int invCount = 0;

                if (pawn.apparel != null)
                {
                    apparelCount = pawn.apparel.WornApparelCount;
                    pawn.apparel.DestroyAll(DestroyMode.Vanish);
                }
                if (pawn.equipment != null)
                {
                    equipCount = pawn.equipment.AllEquipmentListForReading.Count;
                    pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
                }
                if (pawn.inventory != null)
                {
                    invCount = pawn.inventory.innerContainer.Count;
                    pawn.inventory.DestroyAll(DestroyMode.Vanish);
                }

                LogFailsafe(string.Format(
                    "{0}: stripped {1} apparel, {2} equipment, {3} inventory - body reformed from nothing, gear does not return.",
                    reasonLabel, apparelCount, equipCount, invCount));
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] StripAllGear failed: " + ex.Message);
            }
        }

        public string DumpState()
        {
            var settings = KurinDemigodessMod.Settings;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[KurinDemigodess] Tracker state:");
            sb.AppendLine(string.Format("  consecutiveMissCount: {0}/{1}", consecutiveMissCount, settings.failsafeMissThreshold));
            sb.AppendLine(string.Format("  scanInterval: {0} ticks", settings.failsafeScanInterval));
            sb.AppendLine(string.Format("  savedDemigodess: {0}", savedDemigodess == null ? "null" : (savedDemigodess.Destroyed ? "destroyed" : savedDemigodess.LabelShort + (savedDemigodess.Dead ? " (dead)" : " (alive)"))));
            sb.AppendLine(string.Format("  ascensionCount: {0}", ascendedDemigodesses.Count));
            foreach (var a in ascendedDemigodesses)
            {
                sb.AppendLine(string.Format("    - pawn={0}, ticksRemaining={1}", a.pawn == null ? "null" : a.pawn.LabelShort, a.ticksRemaining));
            }
            return sb.ToString();
        }

        public void BeginAscension(Pawn pawn)
        {
            int baseDays = KurinDemigodessMod.Settings.ascensionDurationDays;
            int days = baseDays;

            // Divine shrine halves ascension time if one exists on any home map
            bool shrineBoost = DivineShrineExistsOnAnyHomeMap();
            if (shrineBoost)
            {
                days = System.Math.Max(1, days / 2);
            }

            int ascensionTicks = days * 60000;
            var data = new AscensionData
            {
                pawn = pawn,
                ticksRemaining = ascensionTicks,
                originMapId = pawn.Map?.uniqueID ?? -1
            };

            ascendedDemigodesses.Add(data);

            // Ascension shockwave (lore fun G): visual flash + colony mood + letter
            TriggerAscensionShockwave(pawn, days, shrineBoost);
        }

        /// <summary>
        /// Returns true if at least one DG_DivineShrine is built on any player home map.
        /// Used by BeginAscension to halve ascension time, and by AethiraGizmos_Patch
        /// to double the duration of the Bless Colonist buff.
        /// </summary>
        public static bool DivineShrineExistsOnAnyHomeMap()
        {
            if (Kurin_DefOf.DG_DivineShrine == null) return false;
            if (Find.Maps == null) return false;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                var shrines = map.listerThings.ThingsOfDef(Kurin_DefOf.DG_DivineShrine);
                if (shrines != null && shrines.Count > 0) return true;
            }
            return false;
        }

        private static void TriggerAscensionShockwave(Pawn pawn, int days, bool shrineBoost)
        {
            try
            {
                var map = pawn.Map;
                if (map != null && pawn.Spawned)
                {
                    // Big visual flash centered on her position
                    FleckMaker.Static(pawn.DrawPos, map, FleckDefOf.ExplosionFlash, 14f);
                    FleckMaker.Static(pawn.DrawPos, map, FleckDefOf.ExplosionFlash, 8f);

                    // Sound (best-effort lookup; silent if not present)
                    var sound = DefDatabase<SoundDef>.GetNamedSilentFail("EnergyShield_Broken")
                             ?? DefDatabase<SoundDef>.GetNamedSilentFail("Psycast_Skip_Pulse")
                             ?? DefDatabase<SoundDef>.GetNamedSilentFail("Thunder_OnMap");
                    if (sound != null)
                    {
                        sound.PlayOneShot(new TargetInfo(pawn.Position, map));
                    }

                    // Apply awe thought to everyone who witnessed it
                    if (Kurin_DefOf.DG_AethiraAscending != null)
                    {
                        foreach (var c in map.mapPawns.FreeColonistsSpawned)
                        {
                            if (c == pawn) continue;
                            if (c.needs?.mood?.thoughts?.memories != null)
                            {
                                c.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_AethiraAscending);
                            }
                        }
                    }
                }

                string dayWord = days == 1 ? "day" : "days";
                string letterBody;
                if (shrineBoost)
                {
                    letterBody = string.Format(
                        "In a blinding wave of divine light, Aethira Dawnforge's body dissolves beyond the mortal plane. She has ascended. The Divine Shrine pulses with her light, drawing her back at double speed: she will return in {0} {1} instead of the usual span. The colony stands in awed silence.",
                        days, dayWord);
                }
                else
                {
                    letterBody = string.Format(
                        "In a blinding wave of divine light, Aethira Dawnforge's body dissolves beyond the mortal plane. She has ascended, and will not return for {0} {1}. The colony stands in awed silence.\n\nIf a Divine Shrine had been built, her absence would be half as long.",
                        days, dayWord);
                }

                Find.LetterStack.ReceiveLetter(
                    "The Demigodess Ascends",
                    letterBody,
                    LetterDefOf.NeutralEvent);
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Ascension shockwave failed: " + ex.Message);
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            // Lazily generate a per-save UUID for isolating the on-disk snapshot (Layer 7)
            if (string.IsNullOrEmpty(worldUuid))
            {
                worldUuid = System.Guid.NewGuid().ToString("N");
            }

            // Single unified scan every failsafeScanInterval ticks (Tier 1 #3: merged from 4 separate loops)
            int scanInterval = KurinDemigodessMod.Settings.failsafeScanInterval;
            if (scanInterval < 1) scanInterval = 250;
            if (Find.TickManager.TicksGame % scanInterval == 0)
            {
                ScanAndMaintain();
            }

            for (int i = ascendedDemigodesses.Count - 1; i >= 0; i--)
            {
                var data = ascendedDemigodesses[i];
                data.ticksRemaining--;

                // Daily status update (every 60000 ticks = 1 day)
                if (data.ticksRemaining > 0 && data.ticksRemaining % 60000 == 0)
                {
                    int daysLeft = data.ticksRemaining / 60000;
                    Messages.Message(
                        string.Format("Aethira Dawnforge is still ascending beyond the mortal plane. {0} days until she returns.", daysLeft),
                        MessageTypeDefOf.NeutralEvent, false);
                }

                if (data.ticksRemaining <= 0)
                {
                    RespawnDemigodess(data);
                    ascendedDemigodesses.RemoveAt(i);
                }
            }
        }

        // ============================================================
        // UNIFIED FAILSAFE SCAN (Tier 1 #1, #2, #3)
        // ============================================================

        /// <summary>
        /// Single-pass scan that:
        ///  - Finds all Demigodess pawns across maps, corpses, world pawns, caravans
        ///  - Refreshes the backup reference from the best available source (Tier 1 #2)
        ///  - Repairs living pawns (extracted from old RepairBrokenDemigodess)
        ///  - Processes corpses (extracted from old ProcessDemigodessCorpses)
        ///  - Triggers emergency respawn only after N consecutive misses (Tier 1 #1)
        /// </summary>
        private void ScanAndMaintain()
        {
            var settings = KurinDemigodessMod.Settings;
            int threshold = settings.failsafeMissThreshold;
            if (threshold < 1) threshold = 3;
            int scanInterval = settings.failsafeScanInterval;
            if (scanInterval < 1) scanInterval = 250;

            if (!firstScanLogged)
            {
                firstScanLogged = true;
                LogFailsafe(string.Format("=== Failsafe scan active. Miss threshold: {0} scans (~{1}s in-game). ===",
                    threshold, threshold * scanInterval / 60));
            }

            var inv = ScanForDemigodess();

            // Tier 1 #2: refresh backup from best candidate across all sources
            Pawn best = inv.BestBackup;
            if (best != null)
            {
                savedDemigodess = best;
            }

            if (inv.livingSpawned.Count > 1)
            {
                LogFailsafe(string.Format("WARNING: {0} living Demigodess pawns found. Possible duplicate.", inv.livingSpawned.Count));
            }

            foreach (var pawn in inv.livingSpawned)
            {
                RepairLivingDemigodess(pawn);
            }

            // Perspective Shift compat: if she's back alive and we dropped her avatar
            // control when she died, re-acquire her and restore the saved mode.
            // Silent no-op if Perspective Shift isn't installed or we never dropped control.
            if (inv.livingSpawned.Count > 0)
            {
                PerspectiveShiftCompat_Patch.TryRestoreAvatar(inv.livingSpawned[0]);
            }

            foreach (var corpse in inv.corpses)
            {
                ProcessCorpse(corpse, scanInterval);
            }

            // Layer 7: on-disk snapshot (throttled). Only save a living, spawned Aethira.
            ticksSinceLastSnapshot += scanInterval;
            if (ticksSinceLastSnapshot >= SnapshotIntervalTicks && inv.livingSpawned.Count > 0)
            {
                ticksSinceLastSnapshot = 0;
                DemigodessSnapshot.Save(inv.livingSpawned[0], worldUuid);
            }

            // Failsafe miss counting is skipped during ascension (she's legitimately absent)
            if (ascendedDemigodesses.Count > 0)
            {
                if (consecutiveMissCount != 0) consecutiveMissCount = 0;
                return;
            }

            if (inv.Any)
            {
                if (consecutiveMissCount > 0)
                {
                    LogFailsafe(string.Format("Demigodess found. Miss count reset from {0}/{1}.", consecutiveMissCount, threshold));
                    consecutiveMissCount = 0;
                }
            }
            else
            {
                consecutiveMissCount++;
                LogFailsafe(string.Format("Demigodess NOT FOUND. Miss count: {0}/{1}", consecutiveMissCount, threshold));

                if (consecutiveMissCount >= threshold)
                {
                    LogFailsafe("Threshold reached. Attempting EmergencyRespawn.");
                    EmergencyRespawn();
                    consecutiveMissCount = 0;
                }
            }
        }

        private DemigodessInventory ScanForDemigodess()
        {
            var inv = new DemigodessInventory();

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                        {
                            if (IsImpostor(pawn))
                            {
                                StripDemigodessGenes(pawn);
                                continue;
                            }
                            inv.livingSpawned.Add(pawn);
                        }
                    }

                    foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                    {
                        var corpse = thing as Corpse;
                        if (corpse != null && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
                        {
                            if (IsImpostor(corpse.InnerPawn))
                            {
                                StripDemigodessGenes(corpse.InnerPawn);
                                continue;
                            }
                            inv.corpses.Add(corpse);
                        }
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
                    {
                        if (IsImpostor(pawn))
                        {
                            StripDemigodessGenes(pawn);
                            continue;
                        }
                        inv.worldPawns.Add(pawn);
                    }
                }
            }

            if (Find.WorldObjects != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    foreach (var pawn in caravan.PawnsListForReading)
                    {
                        if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
                        {
                            if (IsImpostor(pawn))
                            {
                                StripDemigodessGenes(pawn);
                                continue;
                            }
                            inv.caravanPawns.Add(pawn);
                        }
                    }
                }
            }

            return inv;
        }

        /// <summary>
        /// Returns true if the given pawn has Demigodess genes but is NOT the registered
        /// Aethira. Used by the scan loop to detect gene-siphon recipients, dev-mode
        /// copies, or any other path that leaked genes to the wrong pawn.
        /// Returns false (= pawn is legitimate) if no Aethira is registered yet or if
        /// the registered one is destroyed - in those cases this pawn IS or will be her.
        /// </summary>
        private bool IsImpostor(Pawn pawn)
        {
            if (pawn == null) return false;
            if (savedDemigodess == null || savedDemigodess.Destroyed) return false;
            if (savedDemigodess == pawn) return false;
            return true;
        }

        /// <summary>
        /// Removes all 5 Demigodess genes from the given pawn. Used to clean up
        /// gene-transfer leaks that bypassed the Harmony AddGene prefix (existing
        /// broken saves, mods using reflection, etc.).
        /// </summary>
        private static void StripDemigodessGenes(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null) return;

            string[] demigodessDefNames = new string[]
            {
                "DG_DivineConstitution",
                "DG_DivineVitality",
                "DG_DivineGrace",
                "DG_DivinePresence",
                "DG_HairSnowWhite",
            };

            int removed = 0;
            foreach (var defName in demigodessDefNames)
            {
                var geneDef = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                if (geneDef == null) continue;
                var genes = pawn.genes.GenesListForReading;
                for (int i = genes.Count - 1; i >= 0; i--)
                {
                    if (genes[i] != null && genes[i].def == geneDef)
                    {
                        try
                        {
                            pawn.genes.RemoveGene(genes[i]);
                            removed++;
                        }
                        catch (System.Exception ex)
                        {
                            Log.Warning("[KurinDemigodess] Failed to strip gene " + defName + " from " + pawn.LabelShort + ": " + ex.Message);
                        }
                    }
                }
            }

            if (removed > 0)
            {
                Log.Message(string.Format(
                    "[KurinDemigodess] Stripped {0} Demigodess gene(s) from {1} (not the real Aethira).",
                    removed, pawn.LabelShort));
                LogFailsafe(string.Format(
                    "Stripped {0} Demigodess gene(s) from {1} - genes rejected foreign vessel.",
                    removed, pawn.LabelShort));
                Messages.Message(
                    string.Format("Aethira's divine essence rejects {0}. Her genes cannot be carried by another.", pawn.LabelShort),
                    pawn, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>
        /// Repairs a living, spawned Demigodess: removes leftover resurrection hediffs,
        /// restores player faction, fixes the world-pawn bug.
        /// (Extracted from old RepairBrokenDemigodess.)
        /// </summary>
        private void RepairLivingDemigodess(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return;

            // Tier 3 #10: centralized purge
            DemigodessHealing.PurgeResurrectionLeftovers(pawn);

            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                pawn.SetFaction(Faction.OfPlayer);
                LogFailsafe(string.Format("Restored player faction on living Demigodess ({0}).", pawn.LabelShort));
            }

            if (pawn.Spawned && Find.WorldPawns.Contains(pawn))
            {
                WorldPawns_Remove_Patch.allowRemoval = true;
                Find.WorldPawns.RemovePawn(pawn);
                WorldPawns_Remove_Patch.allowRemoval = false;
                Log.Message("[KurinDemigodess] Fixed world pawn bug - removed alive Demigodess from world pawns");
                LogFailsafe("Fixed world-pawn bug - removed alive Demigodess from world pawns.");
            }
        }

        /// <summary>
        /// Ticks a Demigodess corpse: resets rot, repairs HP, advances resurrection
        /// and regeneration hediffs manually (dead pawns don't get health ticks),
        /// and runs gene enforcement while dead.
        /// (Extracted from old ProcessDemigodessCorpses.)
        /// </summary>
        private void ProcessCorpse(Corpse corpse, int scanInterval)
        {
            if (corpse == null || corpse.InnerPawn == null) return;
            var pawn = corpse.InnerPawn;

            // Reset rot
            var rotComp = corpse.TryGetComp<CompRottable>();
            if (rotComp != null)
            {
                rotComp.RotProgress = 0f;
            }

            // Repair corpse HP
            if (corpse.HitPoints < corpse.MaxHitPoints)
            {
                corpse.HitPoints = corpse.MaxHitPoints;
            }

            // Ensure resurrection hediff exists
            if (Kurin_DefOf.DG_DivineResurrecting == null) return;

            var resHediff = pawn.health.hediffSet.hediffs
                .OfType<Hediff_DivineResurrecting>()
                .FirstOrDefault();

            if (resHediff == null)
            {
                resHediff = (Hediff_DivineResurrecting)HediffMaker.MakeHediff(Kurin_DefOf.DG_DivineResurrecting, pawn);
                pawn.health.AddHediff(resHediff);
                LogFailsafe("Re-added missing DG_DivineResurrecting to corpse.");
            }

            // Manually tick the resurrection hediff (dead pawns don't get health ticks!)
            resHediff.ManualTick(scanInterval);

            // Also tick the regenerating hediff if present
            var regenHediff = pawn.health.hediffSet.hediffs
                .OfType<Hediff_DivineRegenerating>()
                .FirstOrDefault();
            if (regenHediff != null)
            {
                regenHediff.Advance(scanInterval);
            }

            // Run gene enforcement on the dead pawn too
            if (pawn.genes != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (gene is Gene_Demigodess demigodessGene && gene.Active)
                    {
                        demigodessGene.ForceEnforceWhileDead();
                        break;
                    }
                }
            }
        }

        // ============================================================
        // EMERGENCY RESPAWN
        // ============================================================

        private void EmergencyRespawn()
        {
            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (homeMap == null)
            {
                LogFailsafe("EmergencyRespawn aborted: no player home map.");
                return;
            }

            try
            {
                Pawn pawn = null;
                bool rebuiltFromNothing = false;

                // Branch 1: savedDemigodess reference is valid - use that pawn instance
                if (savedDemigodess != null && !savedDemigodess.Destroyed)
                {
                    pawn = savedDemigodess;
                    Log.Message("[KurinDemigodess] Emergency respawn: using savedDemigodess reference.");
                    LogFailsafe("Emergency respawn: savedDemigodess valid, routing through ascension.");
                }
                // Branch 2: no backup - generate fresh from PawnKindDef
                else
                {
                    if (Kurin_DefOf.DG_KurinDemigodess_Kind == null)
                    {
                        Log.Error("[KurinDemigodess] Emergency respawn failed: PawnKindDef not found");
                        LogFailsafe("EmergencyRespawn FAILED: PawnKindDef DG_KurinDemigodess_Kind not found.");
                        return;
                    }

                    pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        Kurin_DefOf.DG_KurinDemigodess_Kind,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true));

                    if (pawn == null)
                    {
                        Log.Error("[KurinDemigodess] Emergency respawn failed: PawnGenerator returned null");
                        LogFailsafe("EmergencyRespawn FAILED: PawnGenerator returned null.");
                        return;
                    }

                    rebuiltFromNothing = true;

                    Log.Message("[KurinDemigodess] Emergency respawn: generated new Aethira from PawnKindDef.");
                    LogFailsafe("Emergency respawn: no backup, generated fresh pawn.");

                    // Layer 7: overlay on-disk snapshot (mind-state only)
                    bool restored = DemigodessSnapshot.LoadAndApply(pawn, worldUuid, false);
                    if (restored)
                    {
                        LogFailsafe("Emergency respawn: overlaid on-disk snapshot onto fresh pawn.");
                        Log.Message("[KurinDemigodess] Emergency respawn: snapshot applied to fresh pawn.");
                    }
                }

                // Whatever path we took, strip all gear - she's about to ascend, nothing material comes along.
                StripAllGear(pawn, rebuiltFromNothing ? "EmergencyRespawn fallback" : "EmergencyRespawn ascension path");

                // Force player faction
                if (pawn.Faction == null || !pawn.Faction.IsPlayer)
                {
                    pawn.SetFaction(Faction.OfPlayer);
                }

                // Refresh backup reference to the pawn we're about to ascend
                savedDemigodess = pawn;

                // Hand off to the ascension system - she'll return in `ascensionDurationDays` days
                // via RespawnDemigodess, which handles spawning, healing, needs-max, letters, and thoughts.
                LogFailsafe(string.Format(
                    "Emergency respawn: triggering ascension (rebuiltFromNothing={0}). She returns in {1} day(s).",
                    rebuiltFromNothing, KurinDemigodessMod.Settings.ascensionDurationDays));
                BeginAscension(pawn);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("[KurinDemigodess] Emergency respawn error: {0}", ex));
                LogFailsafe(string.Format("EmergencyRespawn threw exception: {0}", ex.Message));
            }
        }

        // ============================================================
        // ASCENSION RESPAWN (unchanged)
        // ============================================================

        private void RespawnDemigodess(AscensionData data)
        {
            var pawn = data.pawn;
            if (pawn == null)
            {
                Log.Error("[KurinDemigodess] Ascension pawn is null! Cannot respawn.");
                return;
            }

            // Ascension return = rebuilt from nothing. Strip all gear before spawning.
            StripAllGear(pawn, "Ascension return");

            // Find the map to respawn on (prefer home map)
            Map targetMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (targetMap == null)
            {
                targetMap = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            }

            if (targetMap == null)
            {
                Log.Error("[KurinDemigodess] No map found for Demigodess respawn!");
                return;
            }

            // Check for shrine
            IntVec3 spawnPos = FindShrinePosition(targetMap);
            if (!spawnPos.IsValid)
            {
                spawnPos = CellFinder.RandomEdgeCell(targetMap);
            }

            try
            {
                // Allow world pawn removal during respawn
                WorldPawns_Remove_Patch.allowRemoval = true;

                // If pawn is dead, we need to resurrect them
                if (pawn.Dead)
                {
                    // Need to spawn corpse first, then resurrect
                    if (pawn.Corpse != null && !pawn.Corpse.Spawned)
                    {
                        GenSpawn.Spawn(pawn.Corpse, spawnPos, targetMap);
                    }
                    else if (!pawn.Spawned)
                    {
                        // No corpse exists, spawn the pawn directly
                        GenSpawn.Spawn(pawn, spawnPos, targetMap);
                    }

                    // Fully heal before resurrection
                    FullyHealPawn(pawn);

                    // Resurrect
                    ResurrectionUtility.TryResurrect(pawn);

                    // Remove resurrection sickness
                    foreach (var hediff in pawn.health.hediffSet.hediffs
                        .Where(h => h.def.defName.Contains("Resurrection") ||
                                    h.def.defName.Contains("Sickness") ||
                                    h.def.defName.Contains("Coma") ||
                                    h.def.defName == "DG_DivineResurrecting" ||
                                    h.def.defName == "DG_CorpsePreservation")
                        .ToList())
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }
                else
                {
                    // Pawn is alive but despawned
                    FullyHealPawn(pawn);
                    if (!pawn.Spawned)
                    {
                        GenSpawn.Spawn(pawn, spawnPos, targetMap);
                    }
                }

                WorldPawns_Remove_Patch.allowRemoval = false;

                // Re-apply presence hediff
                if (Kurin_DefOf.DG_DemigodessPresence != null && !pawn.health.hediffSet.HasHediff(Kurin_DefOf.DG_DemigodessPresence))
                {
                    pawn.health.AddHediff(Kurin_DefOf.DG_DemigodessPresence);
                }

                // Apply "Demigodess Returns" thought to colony
                if (Kurin_DefOf.DG_DemigodessReturns != null)
                {
                    foreach (var colonist in targetMap.mapPawns.FreeColonistsSpawned)
                    {
                        if (colonist != pawn && colonist.needs?.mood?.thoughts?.memories != null)
                        {
                            colonist.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_DemigodessReturns);
                        }
                    }
                }

                // Max her needs - she just reformed, fresh and whole
                MaxAllNeeds(pawn);

                int days = KurinDemigodessMod.Settings.ascensionDurationDays;
                Messages.Message(
                    "The Demigodess has returned from her divine ascension!",
                    pawn, MessageTypeDefOf.PositiveEvent, false);

                Find.LetterStack.ReceiveLetter(
                    "The Demigodess Returns",
                    string.Format(
                        "After {0} day{1} beyond the mortal plane, Aethira Dawnforge has returned. She materializes in a flash of divine light, fully restored and ready to protect the colony once more. She wears nothing but her own skin; whatever she carried when she ascended remains beyond the veil.",
                        days, days == 1 ? "" : "s"),
                    LetterDefOf.PositiveEvent, pawn);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("[KurinDemigodess] Error during Demigodess respawn: {0}", ex));
            }
        }

        private void FullyHealPawn(Pawn pawn)
        {
            // Tier 3 #10: centralized full purge
            DemigodessHealing.FullPurge(pawn);
        }

        private IntVec3 FindShrinePosition(Map map)
        {
            if (Kurin_DefOf.DG_DivineShrine == null) return IntVec3.Invalid;

            var shrines = map.listerThings.ThingsOfDef(Kurin_DefOf.DG_DivineShrine);
            if (shrines == null || shrines.Count == 0) return IntVec3.Invalid;

            var shrine = shrines.First();
            // Find a standable cell adjacent to the shrine
            foreach (var cell in GenAdj.CellsAdjacent8Way(shrine))
            {
                if (cell.InBounds(map) && cell.Standable(map))
                {
                    return cell;
                }
            }

            return shrine.Position; // Fallback to shrine position itself
        }

        // ============================================================
        // FAILSAFE LOG FILE (Tier 1 #4)
        // ============================================================

        /// <summary>
        /// Appends a line to KurinDemigodess_FailsafeLog.txt in the RimWorld config folder.
        /// Rotates the file when it grows past 200KB, keeping the second half.
        /// Never throws - safe to call from any code path.
        /// </summary>
        private static void LogFailsafe(string message)
        {
            // Respect user's setting - silent if disabled
            if (!KurinDemigodessMod.Settings.enableFailsafeLog) return;

            try
            {
                int tick = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [Tick {1}] {2}",
                    DateTime.Now, tick, message);

                // Rotate if file grows past 200KB
                if (File.Exists(LogFilePath))
                {
                    long size = new FileInfo(LogFilePath).Length;
                    if (size > 200 * 1024)
                    {
                        string[] existing = File.ReadAllLines(LogFilePath);
                        int keep = existing.Length / 2;
                        var trimmed = new string[keep + 1];
                        trimmed[0] = string.Format("--- log rotated at {0:yyyy-MM-dd HH:mm:ss}, older entries dropped ---", DateTime.Now);
                        Array.Copy(existing, existing.Length - keep, trimmed, 1, keep);
                        File.WriteAllLines(LogFilePath, trimmed);
                    }
                }

                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Failed to write failsafe log: " + ex.Message);
            }
        }

        // ============================================================
        // SAVE/LOAD
        // ============================================================

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ascendedDemigodesses, "ascendedDemigodesses",
                LookMode.Deep);
            if (ascendedDemigodesses == null)
            {
                ascendedDemigodesses = new List<AscensionData>();
            }
            Scribe_References.Look(ref savedDemigodess, "savedDemigodess");
            Scribe_Values.Look(ref consecutiveMissCount, "consecutiveMissCount", 0);
            Scribe_Values.Look(ref worldUuid, "worldUuid", null);
            Scribe_Values.Look(ref ticksSinceLastSnapshot, "ticksSinceLastSnapshot", 0);

            // Layer 7: always write a fresh snapshot on the main game save, so the
            // disk backup is guaranteed up-to-date relative to the save file.
            if (Scribe.mode == LoadSaveMode.Saving && savedDemigodess != null
                && !savedDemigodess.Dead && !savedDemigodess.Destroyed)
            {
                DemigodessSnapshot.Save(savedDemigodess, worldUuid);
            }
        }

        // ============================================================
        // INVENTORY CONTAINER
        // ============================================================

        private class DemigodessInventory
        {
            public List<Pawn> livingSpawned = new List<Pawn>();
            public List<Corpse> corpses = new List<Corpse>();
            public List<Pawn> worldPawns = new List<Pawn>();
            public List<Pawn> caravanPawns = new List<Pawn>();

            public bool Any
            {
                get { return livingSpawned.Count > 0 || corpses.Count > 0 || worldPawns.Count > 0 || caravanPawns.Count > 0; }
            }

            public Pawn BestBackup
            {
                get
                {
                    if (livingSpawned.Count > 0) return livingSpawned[0];
                    if (corpses.Count > 0) return corpses[0].InnerPawn;
                    if (caravanPawns.Count > 0) return caravanPawns[0];
                    if (worldPawns.Count > 0) return worldPawns[0];
                    return null;
                }
            }
        }
    }

    public class AscensionData : IExposable
    {
        public Pawn pawn;
        public int ticksRemaining;
        public int originMapId;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Values.Look(ref originMapId, "originMapId", -1);
        }
    }
}
