using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Sends a one-time narrative letter the first time Aethira appears on a
    /// player home map. Respects the firstLoadLetterEnabled setting.
    /// State persists in the save.
    /// </summary>
    public class GameComponent_DemigodessNarrative : GameComponent
    {
        private bool firstLoadLetterSent;

        public GameComponent_DemigodessNarrative(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref firstLoadLetterSent, "firstLoadLetterSent", false);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (firstLoadLetterSent) return;
            if (!KurinDemigodessMod.Settings.firstLoadLetterEnabled) return;

            // Cheap throttle - check every 500 ticks (~8s in-game)
            if (Find.TickManager.TicksGame % 500 != 0) return;

            if (Find.Maps == null) return;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                    {
                        SendFirstLoadLetter(pawn);
                        firstLoadLetterSent = true;
                        return;
                    }
                }
            }
        }

        private void SendFirstLoadLetter(Pawn aethira)
        {
            Find.LetterStack.ReceiveLetter(
                "The Divine Herald Walks Among You",
                "Aethira Dawnforge stands before you - ancient beyond reckoning, eternal in vigil.\n\n" +
                "Five centuries past, the AIs of Aolara wrought her from the last sparks of their world's dying sun. " +
                "They named her Dawnforge, for she was to be the first light of a new dawn for the Kurin. " +
                "Where she walks, tyranny withers. Where she watches, her people sleep without fear.\n\n" +
                "She cannot be unmade. She cannot be broken. She has buried every colony that ever called her sister, " +
                "and she will bury this one too - but not before you have grown old together, time and time again.\n\n" +
                "She does not age. She does not flee. She does not grieve the dead, for she knows the way back to them. " +
                "What she asks of you is simple: be worthy of her protection.",
                LetterDefOf.PositiveEvent, aethira);
        }
    }
}
