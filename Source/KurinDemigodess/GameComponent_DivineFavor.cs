using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Divine Favor resource system.
    /// Accumulates from multiple sources:
    ///  1. Daily passive: +5/day while Aethira is alive on a home map (or +10 if average
    ///     colony mood is at least 50%).
    ///  2. Pilgrimage arrival: +20 when Kurin pilgrims visit.
    ///  3. Day of Remembrance: +30 on the annual event.
    ///  4. Aethira's Guidance: +3 each time the guidance event fires.
    ///  5. Friendly visitors departing the map alive: +2 each.
    ///  6. Hostiles killed while Aethira is on the map: +1 each, capped at 20/day.
    /// Capped at 100. Spent on Divine Blessing effects via the command-bar gizmos.
    /// </summary>
    public class GameComponent_DivineFavor : GameComponent
    {
        private const int MaxFavor = 100;
        private const int FavorPerDay = 5;
        private const int BlessingFullHealCost = 100;
        private const int MaxKillFavorPerDay = 20;
        private const float MoodScalingThreshold = 0.5f;

        private int currentFavor;
        private int ticksSinceLastGain;
        private int killFavorGainedToday;
        private int ticksSinceDailyKillReset;

        public GameComponent_DivineFavor(Game game)
        {
        }

        public int Current
        {
            get { return currentFavor; }
        }

        public int MaxCapacity
        {
            get { return MaxFavor; }
        }

        public int BlessingCost
        {
            get { return BlessingFullHealCost; }
        }

        /// <summary>
        /// Shared favor-to-multiplier formula used by every aura in the mod:
        /// linear scale from 1x at 0 favor to 2x at max favor (100). Used by
        /// HealingPresence (heal rate + range), CalmingPresence (range), and
        /// IntimidationAura (range). Returns 1x if favor or favor-scaling is
        /// disabled in settings, or if the game component isn't loaded yet.
        /// </summary>
        public static float GetAuraMultiplier()
        {
            if (!KurinDemigodessMod.Settings.favorScalingAuraEnabled) return 1f;
            if (!KurinDemigodessMod.Settings.divineFavorEnabled) return 1f;
            // Fully qualified: `Current` here would shadow Verse.Current because this
            // class has an instance `Current` property. Use Verse.Current explicitly.
            var favor = Verse.Current.Game?.GetComponent<GameComponent_DivineFavor>();
            if (favor == null || favor.MaxCapacity <= 0) return 1f;
            return 1f + ((float)favor.Current / favor.MaxCapacity);
        }

        public void Add(int amount)
        {
            if (amount <= 0) return;
            // Divine Shrine on a home map doubles all favor gain
            if (WorldComponent_DemigodessTracker.DivineShrineExistsOnAnyHomeMap())
            {
                amount *= 2;
            }
            currentFavor = System.Math.Min(MaxFavor, currentFavor + amount);
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (currentFavor < amount) return false;
            currentFavor -= amount;
            return true;
        }

        /// <summary>
        /// Grant favor from a hostile kill, respecting the per-day cap.
        /// The daily cap counts RAW kills (not shrine-doubled favor). So with a shrine,
        /// 20 kills/day caps at 40 actual favor gained. Without a shrine, 20 favor.
        /// </summary>
        public bool TryAddKillFavor(int amount)
        {
            if (amount <= 0) return false;
            if (killFavorGainedToday >= MaxKillFavorPerDay) return false;
            int canAdd = System.Math.Min(amount, MaxKillFavorPerDay - killFavorGainedToday);
            if (canAdd <= 0) return false;
            killFavorGainedToday += canAdd;
            Add(canAdd); // Add() applies shrine doubling if applicable
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref currentFavor, "currentFavor", 0);
            Scribe_Values.Look(ref ticksSinceLastGain, "ticksSinceLastGain", 0);
            Scribe_Values.Look(ref killFavorGainedToday, "killFavorGainedToday", 0);
            Scribe_Values.Look(ref ticksSinceDailyKillReset, "ticksSinceDailyKillReset", 0);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (!KurinDemigodessMod.Settings.divineFavorEnabled) return;

            // Reset daily kill-favor cap once per in-game day
            ticksSinceDailyKillReset++;
            if (ticksSinceDailyKillReset >= 60000)
            {
                ticksSinceDailyKillReset = 0;
                killFavorGainedToday = 0;
            }

            // Daily passive gain with mood scaling
            ticksSinceLastGain++;
            if (ticksSinceLastGain < 60000) return;
            ticksSinceLastGain = 0;

            if (!AethiraIsPresentAtHome()) return;

            int amount = FavorPerDay;
            float avgMood = GetAverageColonistMoodAtHome();
            if (avgMood >= MoodScalingThreshold)
            {
                amount *= 2; // Happy colony = double daily gain
            }
            Add(amount);
        }

        private static bool AethiraIsPresentAtHome()
        {
            if (Find.Maps == null) return false;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                        return true;
                }
            }
            return false;
        }

        private static float GetAverageColonistMoodAtHome()
        {
            if (Find.Maps == null) return 0f;
            float total = 0f;
            int count = 0;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn == null || pawn.Dead) continue;
                    if (Gene_Demigodess.HasDemigodessGene(pawn)) continue; // don't count Aethira
                    if (pawn.needs == null || pawn.needs.mood == null) continue;
                    total += pawn.needs.mood.CurLevelPercentage;
                    count++;
                }
            }
            return count > 0 ? total / count : 0f;
        }

        /// <summary>
        /// Divine Blessing: fully restore every colonist on the map.
        /// Heals all injuries, blood loss, missing parts, status effects (temperature,
        /// toxic, chemical, food poisoning, addiction, malnutrition, catatonic),
        /// diseases (flu, plague, infection, etc.), and maxes all needs (food, rest,
        /// joy, mood). The same treatment Aethira gets after an ascension.
        /// Costs <see cref="BlessingFullHealCost"/> favor. Returns false if insufficient.
        /// </summary>
        public bool TryInvokeBlessingHeal(Map map)
        {
            if (map == null) return false;
            if (!TrySpend(BlessingFullHealCost)) return false;

            int touched = 0;
            foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist == null || colonist.Dead) continue;

                // Full body restoration: injuries, blood loss, missing parts, all bad hediffs
                DemigodessHealing.FullPurge(colonist);
                DemigodessHealing.PurgeDiseases(colonist);

                // Max every need: food, rest, joy, mood, recreation, comfort, etc.
                WorldComponent_DemigodessTracker.MaxAllNeeds(colonist);

                touched++;
            }

            Messages.Message(
                string.Format("Divine Blessing: {0} colonist{1} fully restored by Aethira's power.",
                    touched, touched == 1 ? "" : "s"),
                MessageTypeDefOf.PositiveEvent, false);

            Find.LetterStack.ReceiveLetter(
                "Divine Blessing",
                string.Format(
                    "Aethira Dawnforge channels the fullness of her divine power across the colony. " +
                    "All wounds close. All diseases fade. All weariness and hunger lift. All hearts settle. " +
                    "{0} colonist{1} stand whole and full, as if each had just stepped out of her own ascension.\n\n" +
                    "Cost: {2} Divine Favor.",
                    touched, touched == 1 ? "" : "s", BlessingFullHealCost),
                LetterDefOf.PositiveEvent);

            return true;
        }
    }
}
