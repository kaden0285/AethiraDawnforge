using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// World-side seeding for Quest recruitment mode.
    ///
    /// When the player did NOT pick a Dawnforge scenario and the recruitment
    /// mode is Quest, Aethira should exist as the NPC leader of the Dawnforge
    /// Collective faction (Kurin_Faction). Until the player completes the
    /// recruitment quest chain or uses the comms-console Petition option,
    /// she stays with the Collective and the player's colony has no Demigodess.
    ///
    /// EnsureSeeded() is idempotent. It runs:
    ///   - On WorldComponent FinalizeInit (covers fresh worlds and save load)
    ///   - On the first failsafe tick (defensive: if FinalizeInit somehow missed)
    ///   - When the recruitment mode is changed from Disabled/AutoSpawn to Quest
    ///     and a never-had save is detected
    ///
    /// Behavior:
    ///   - If RecruitmentMode is not Quest: no-op.
    ///   - If Aethira already exists anywhere (player faction, world pawns,
    ///     or as a faction leader): no-op. The seeder never duplicates her.
    ///   - If the Dawnforge Collective faction is missing (mod added to a
    ///     world that pre-dates it): logs a warning and falls back to
    ///     declining the seed. The player can either start a new world or
    ///     switch RecruitmentMode to AutoSpawn.
    ///   - Otherwise: generates a fresh Aethira pawn for the Collective,
    ///     assigns her as the faction leader, registers her with the
    ///     world-pawns system, and refreshes the WorldComponent's
    ///     savedDemigodess reference so the failsafe and quest hooks can
    ///     find her later.
    /// </summary>
    public static class DawnforgeFactionSeeder
    {
        private const string DawnforgeCollectiveDefName = "Kurin_Faction";

        public static void EnsureSeeded(WorldComponent_DemigodessTracker tracker)
        {
            if (tracker == null) return;
            if (KurinDemigodessMod.Settings.recruitmentMode != RecruitmentMode.Quest) return;

            // Already exists somewhere? Don't duplicate.
            if (FindExistingAethira() != null) return;

            // Find the Dawnforge Collective faction.
            var collective = FindDawnforgeCollective();
            if (collective == null)
            {
                Log.Warning("[KurinDemigodess] Quest recruitment mode is on but the Dawnforge Collective faction is missing from this world. " +
                            "Switch RecruitmentMode to AutoSpawn or start a new world to get her. Skipping seed.");
                return;
            }

            // Generate Aethira as a member of the Collective.
            if (Kurin_DefOf.DG_KurinDemigodess_Kind == null)
            {
                Log.Error("[KurinDemigodess] DawnforgeFactionSeeder: PawnKindDef DG_KurinDemigodess_Kind not found. Cannot seed.");
                return;
            }

            Pawn aethira;
            try
            {
                aethira = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    Kurin_DefOf.DG_KurinDemigodess_Kind,
                    collective,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true));
            }
            catch (System.Exception ex)
            {
                Log.Error("[KurinDemigodess] DawnforgeFactionSeeder: PawnGenerator threw: " + ex);
                return;
            }

            if (aethira == null)
            {
                Log.Error("[KurinDemigodess] DawnforgeFactionSeeder: PawnGenerator returned null.");
                return;
            }

            // Make her the leader of the Collective.
            collective.leader = aethira;

            // Register with the world-pawn system so she persists across map loads
            // and isn't garbage collected. WorldPawns_PassToWorld_Patch should
            // already protect Demigodess pawns from accidental removal.
            if (!Find.WorldPawns.Contains(aethira))
            {
                Find.WorldPawns.PassToWorld(aethira, PawnDiscardDecideMode.KeepForever);
            }

            // Tell the tracker this is the canonical Aethira so failsafe scans
            // and gene-transfer protections recognize her.
            tracker.RegisterSeededDemigodess(aethira);

            Log.Message(string.Format(
                "[KurinDemigodess] Seeded Aethira as the Dawnforge Speaker of {0}. Player must complete the recruitment quest chain to bring her into the colony.",
                collective.Name));
        }

        public static Pawn FindExistingAethira()
        {
            // 1. Player faction colonists (active or in caravan)
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns == null) continue;
                    foreach (var pawn in map.mapPawns.AllPawns)
                    {
                        if (Gene_Demigodess.HasDemigodessGene(pawn)) return pawn;
                    }
                }
            }

            // 2. World pawns (caravans, undrafted travelers, faction leaders)
            if (Find.WorldPawns != null)
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (Gene_Demigodess.HasDemigodessGene(pawn)) return pawn;
                }
            }

            // 3. Faction leaders specifically (in case they aren't in WorldPawns yet)
            if (Find.FactionManager != null)
            {
                foreach (var faction in Find.FactionManager.AllFactions)
                {
                    if (faction?.leader != null && Gene_Demigodess.HasDemigodessGene(faction.leader))
                    {
                        return faction.leader;
                    }
                }
            }

            return null;
        }

        public static Faction FindDawnforgeCollective()
        {
            if (Find.FactionManager == null) return null;
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (faction?.def?.defName == DawnforgeCollectiveDefName)
                {
                    // Skip hidden/permanent-enemy ghosts; we want the live, settle-able instance.
                    if (faction.Hidden || faction.IsPlayer) continue;
                    return faction;
                }
            }
            return null;
        }
    }
}
