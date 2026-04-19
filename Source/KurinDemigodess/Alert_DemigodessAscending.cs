using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Persistent alert in the bottom-right alerts list showing Aethira's
    /// ascension countdown in real time. Active whenever at least one
    /// Demigodess is ascending. Displays the shortest remaining time across
    /// all currently-ascending Demigodesses.
    ///
    /// RimWorld's AlertsReadout automatically discovers and instantiates every
    /// subclass of Alert in every loaded assembly, so no XML registration needed.
    /// </summary>
    public class Alert_DemigodessAscending : Alert
    {
        public Alert_DemigodessAscending()
        {
            defaultLabel = "Demigodess ascending";
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel()
        {
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            if (tracker == null) return "Demigodess ascending";
            int ticks = tracker.MinAscensionTicksRemaining();
            if (ticks < 0) return "Demigodess ascending";
            return "Ascending: " + FormatTicks(ticks);
        }

        public override TaggedString GetExplanation()
        {
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            if (tracker == null || tracker.AscensionCount == 0)
                return "Aethira Dawnforge is beyond the mortal plane.";

            int ticks = tracker.MinAscensionTicksRemaining();
            string time = ticks > 0 ? FormatTicks(ticks) : "any moment now";

            bool hasShrine = WorldComponent_DemigodessTracker.DivineShrineExistsOnAnyHomeMap();
            string shrineNote = hasShrine
                ? "\n\nThe Divine Shrine is halving her ascension duration."
                : "\n\nIf a Divine Shrine had been built, her absence would be half as long.";

            return string.Format(
                "Aethira Dawnforge is ascending beyond the mortal plane. Her body is being remade from divine essence.\n\n" +
                "She will return in approximately {0}.\n\n" +
                "While she is absent, the colony has no divine protection - no healing aura, no calming presence, no intimidation aura, no weather purge, and no Divine Favor accumulation. Her return will materialize at the divine shrine (or at the map edge if no shrine has been built).{1}\n\n" +
                "Colonists carrying her Blessing still receive all her aura benefits wherever they are, even during her absence.",
                time, shrineNote);
        }

        public override AlertReport GetReport()
        {
            var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
            if (tracker == null) return false;
            if (tracker.AscensionCount == 0) return false;
            return AlertReport.Active;
        }

        /// <summary>
        /// Formats RimWorld ticks as a short human-readable duration.
        /// 60000 ticks = 1 in-game day. 2500 ticks = 1 in-game hour.
        /// </summary>
        private static string FormatTicks(int ticks)
        {
            if (ticks <= 0) return "imminent";

            int days = ticks / 60000;
            int remainderAfterDays = ticks % 60000;
            int hours = remainderAfterDays / 2500;
            int remainderAfterHours = remainderAfterDays % 2500;
            // 1 in-game minute ≈ 2500/60 ≈ 41.66 ticks
            int minutes = remainderAfterHours * 60 / 2500;

            if (days > 0)
                return string.Format("{0}d {1}h", days, hours);
            if (hours > 0)
                return string.Format("{0}h {1}m", hours, minutes);
            return string.Format("{0}m", minutes);
        }
    }
}
