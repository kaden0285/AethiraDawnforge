using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Automatically assigns the Demigodess as the ideology leader role
    /// whenever she joins a colony - but ONLY when the colony's ideology has
    /// the Demigodess Worship meme. If the player picked their own ideology
    /// (cannibal, tribal hunter, etc.), she does NOT seize their leader role;
    /// she just joins as a regular colonist with her own beliefs intact.
    /// Checks periodically via a MapComponent.
    /// </summary>
    public class MapComponent_DemigodessLeader : MapComponent
    {
        private const string DemigodessWorshipMemeDefName = "DG_DemigodessWorship";

        private int checkInterval = 500; // Check every ~8 seconds
        private int tickCounter;
        private bool assigned;

        public MapComponent_DemigodessLeader(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            tickCounter++;
            if (tickCounter < checkInterval) return;
            tickCounter = 0;

            TryAssignLeader();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // Try assigning on map load
            assigned = false;
        }

        private void TryAssignLeader()
        {
            // Find a Demigodess pawn on this map
            Pawn demigodess = null;
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (Gene_Demigodess.HasDemigodessGene(pawn))
                {
                    demigodess = pawn;
                    break;
                }
            }

            if (demigodess == null) return;

            // Check if she has an ideology
            var ideo = demigodess.Ideo;
            if (ideo == null) return;

            // Gate: only auto-assign when the colony ideology actually worships
            // her. Don't seize the leader role of an unrelated player ideology.
            if (!IdeologyHasDemigodessWorshipMeme(ideo)) return;

            // Find the leader role precept
            Precept_RoleSingle leaderPrecept = null;
            foreach (var precept in ideo.PreceptsListForReading)
            {
                if (precept is Precept_RoleSingle roleSingle && roleSingle.def.leaderRole)
                {
                    leaderPrecept = roleSingle;
                    break;
                }
            }

            if (leaderPrecept == null) return;

            // Check if she's already the leader
            if (leaderPrecept.ChosenPawnSingle() == demigodess)
            {
                return;
            }

            // Assign her as leader
            leaderPrecept.Assign(demigodess, true);

            Messages.Message(
                "The Demigodess has been recognized as the divine leader of the colony.",
                demigodess, MessageTypeDefOf.PositiveEvent, false);
        }

        private static bool IdeologyHasDemigodessWorshipMeme(Ideo ideo)
        {
            if (ideo == null || ideo.memes == null) return false;
            for (int i = 0; i < ideo.memes.Count; i++)
            {
                if (ideo.memes[i] != null && ideo.memes[i].defName == DemigodessWorshipMemeDefName)
                {
                    return true;
                }
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref assigned, "DG_LeaderAssigned", false);
        }
    }
}
