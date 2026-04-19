using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace KurinDemigodess
{
    public class HediffCompProperties_CalmingPresence : HediffCompProperties
    {
        public float range = 5f;
        public int tickInterval = 120;

        public HediffCompProperties_CalmingPresence()
        {
            compClass = typeof(HediffComp_CalmingPresence);
        }
    }

    /// <summary>
    /// When the Demigodess gets within 5 tiles of a colonist having a mental break,
    /// the break ends immediately and they get catharsis + a mood buff.
    /// </summary>
    public class HediffComp_CalmingPresence : HediffComp
    {
        private int tickCounter;

        public HediffCompProperties_CalmingPresence Props
        {
            get { return (HediffCompProperties_CalmingPresence)props; }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            tickCounter++;
            if (tickCounter < Props.tickInterval) return;
            tickCounter = 0;

            if (!parent.pawn.Spawned || parent.pawn.Dead || parent.pawn.Downed) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;
            float effectiveRange = Props.range * GameComponent_DivineFavor.GetAuraMultiplier();

            var pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var target = pawns[i];
                if (target == parent.pawn) continue;
                if (target.Dead) continue;
                if (target.Position.DistanceTo(pos) > effectiveRange) continue;

                // Check if they're in a mental state
                if (target.InMentalState)
                {
                    // End the mental break
                    target.MentalState.RecoverFromState();

                    // Give catharsis
                    var catharsisDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Catharsis");
                    if (catharsisDef != null && target.needs != null && target.needs.mood != null)
                    {
                        target.needs.mood.thoughts.memories.TryGainMemory(catharsisDef);
                    }

                    // Give "The Demigodess calmed me" thought
                    if (Kurin_DefOf.DG_DemigodessCalmedMe != null && target.needs != null && target.needs.mood != null)
                    {
                        target.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_DemigodessCalmedMe);
                    }

                    Messages.Message(
                        string.Format("The Demigodess's presence calms {0}. Their mind clears and peace returns.", target.LabelShort),
                        target, MessageTypeDefOf.PositiveEvent, false);
                }
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref tickCounter, "DG_CalmingTickCounter", 0);
        }
    }
}
