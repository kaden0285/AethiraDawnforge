using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Dev-mode debug actions for the Demigodess mod. Appears in the RimWorld
    /// Debug Actions Menu under "Kurin Demigodess" when dev mode is enabled.
    /// </summary>
    public static class DebugActions_Demigodess
    {
        private const string Category = "Kurin Demigodess";

        // ----- State inspection -----

        [DebugAction(Category, "Dump failsafe state", allowedGameStates = AllowedGameStates.Playing)]
        private static void DumpFailsafeState()
        {
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            if (tracker == null)
            {
                Log.Message("[KurinDemigodess] No tracker component found.");
                return;
            }
            Log.Message(tracker.DumpState());
        }

        [DebugAction(Category, "Dump divine favor", allowedGameStates = AllowedGameStates.Playing)]
        private static void DumpDivineFavor()
        {
            var favor = Current.Game?.GetComponent<GameComponent_DivineFavor>();
            if (favor == null)
            {
                Log.Message("[KurinDemigodess] No favor component found.");
                return;
            }
            Log.Message(string.Format("[KurinDemigodess] Divine Favor: {0}/{1} (blessing cost: {2})",
                favor.Current, favor.MaxCapacity, favor.BlessingCost));
        }

        [DebugAction(Category, "Find Aethira", allowedGameStates = AllowedGameStates.Playing)]
        private static void FindAethiraLog()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[KurinDemigodess] Aethira search:");

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
                            sb.AppendLine(string.Format("  LIVE on map {0}: {1} at {2}", map.uniqueID, pawn.LabelShort, pawn.Position));
                    }
                    foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                    {
                        var corpse = thing as Corpse;
                        if (corpse != null && corpse.InnerPawn != null && Gene_Demigodess.HasDemigodessGene(corpse.InnerPawn))
                            sb.AppendLine(string.Format("  CORPSE on map {0}: {1} at {2}", map.uniqueID, corpse.InnerPawn.LabelShort, corpse.Position));
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
                        sb.AppendLine(string.Format("  WORLDPAWN: {0} ({1})", pawn.LabelShort, pawn.Dead ? "dead" : "alive"));
                }
            }

            if (Find.WorldObjects != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    foreach (var pawn in caravan.PawnsListForReading)
                    {
                        if (pawn != null && Gene_Demigodess.HasDemigodessGene(pawn))
                            sb.AppendLine(string.Format("  CARAVAN {0}: {1}", caravan.Name, pawn.LabelShort));
                    }
                }
            }

            Log.Message(sb.ToString());
        }

        // ----- Failsafe test actions -----

        [DebugAction(Category, "Reset miss counter", allowedGameStates = AllowedGameStates.Playing)]
        private static void ResetMissCounter()
        {
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            if (tracker != null)
            {
                tracker.ConsecutiveMissCount = 0;
                Log.Message("[KurinDemigodess] Miss counter reset.");
            }
        }

        [DebugAction(Category, "Force emergency respawn", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceEmergencyRespawn()
        {
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            tracker?.ForceEmergencyRespawn();
        }

        // ----- Aethira state changes -----

        [DebugAction(Category, "Force kill Aethira", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceKillAethira()
        {
            var pawn = FindLiveAethira();
            if (pawn == null) { Log.Message("[KurinDemigodess] No living Aethira found."); return; }
            pawn.Kill(null);
        }

        [DebugAction(Category, "Trigger ascension (7 days)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TriggerAscensionAction()
        {
            var pawn = FindLiveAethira();
            if (pawn == null) { Log.Message("[KurinDemigodess] No living Aethira found."); return; }
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            tracker?.BeginAscension(pawn);
            Log.Message(string.Format("[KurinDemigodess] Ascension started for {0}.", pawn.LabelShort));
        }

        [DebugAction(Category, "Trigger death-recoil pulse (no kill)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TriggerDeathRecoilDebug()
        {
            var pawn = FindLiveAethira();
            if (pawn == null) { Log.Message("[KurinDemigodess] No living Aethira found."); return; }
            DeathlessDemigodess_Patch.TriggerDeathRecoil(pawn);
        }

        // ----- Divine Favor -----

        [DebugAction(Category, "Grant +50 divine favor", allowedGameStates = AllowedGameStates.Playing)]
        private static void GrantDivineFavor()
        {
            var favor = Current.Game?.GetComponent<GameComponent_DivineFavor>();
            if (favor == null) return;
            favor.Add(50);
            Log.Message(string.Format("[KurinDemigodess] Divine Favor: +50 (now {0}/{1})", favor.Current, favor.MaxCapacity));
        }

        [DebugAction(Category, "Invoke divine blessing (heal all)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InvokeDivineBlessing()
        {
            var favor = Current.Game?.GetComponent<GameComponent_DivineFavor>();
            if (favor == null) return;
            var map = Find.CurrentMap;
            if (map == null) return;
            bool ok = favor.TryInvokeBlessingHeal(map);
            if (!ok) Log.Message(string.Format("[KurinDemigodess] Not enough favor. Need {0}, have {1}.", favor.BlessingCost, favor.Current));
        }

        // ----- Pilgrimage -----

        [DebugAction(Category, "Force pilgrimage event", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForcePilgrimage()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            var incident = DefDatabase<IncidentDef>.GetNamedSilentFail("DG_KurinPilgrimage");
            if (incident == null) { Log.Message("[KurinDemigodess] DG_KurinPilgrimage incident def not found."); return; }
            var parms = new IncidentParms { target = map };
            incident.Worker.TryExecute(parms);
        }

        // ----- Helpers -----

        private static Pawn FindLiveAethira()
        {
            if (Find.Maps == null) return null;
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                        return pawn;
                }
            }
            return null;
        }
    }
}
