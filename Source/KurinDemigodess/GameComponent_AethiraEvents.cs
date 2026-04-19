using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Hosts three feature loops:
    ///  - Weather/condition purge (every ~40s) - dispels bad GameConditions on
    ///    home maps where Aethira is present (toxic fallout, cold snap, etc.)
    ///  - Aethira's Guidance (daily roll) - ~33% chance to bless a random colonist
    ///    with a mood buff + flavor letter.
    ///  - Day of Remembrance (annual) - colony-wide mood buff every 60 in-game days.
    /// </summary>
    public class GameComponent_AethiraEvents : GameComponent
    {
        private int ticksSinceLastGuidance;
        private int ticksSinceLastRemembrance;

        private const int GuidanceCheckInterval = 60000;   // 1 in-game day
        private const float GuidanceChance = 0.33f;        // ~once every 3 days on average
        private const int RemembranceInterval = 3600000;   // 60 in-game days (1 year)
        private const int WeatherCheckInterval = 2500;     // ~40s in-game

        private static readonly string[] BadConditionDefs = new string[]
        {
            "ToxicFallout",
            "VolcanicWinter",
            "ColdSnap",
            "HeatWave",
            "Flashstorm",
            "NoxiousHaze",
            "PsychicDrone",
        };

        public GameComponent_AethiraEvents(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksSinceLastGuidance, "ticksSinceLastGuidance", 0);
            Scribe_Values.Look(ref ticksSinceLastRemembrance, "ticksSinceLastRemembrance", 0);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // Weather purge - runs every WeatherCheckInterval ticks
            if (Find.TickManager.TicksGame % WeatherCheckInterval == 0)
            {
                TryPurgeBadConditions();
            }

            // Aethira's Guidance - daily roll
            ticksSinceLastGuidance++;
            if (ticksSinceLastGuidance >= GuidanceCheckInterval)
            {
                ticksSinceLastGuidance = 0;
                if (KurinDemigodessMod.Settings.guidanceEventsEnabled)
                {
                    TryTriggerGuidance();
                }
            }

            // Day of Remembrance - annual
            ticksSinceLastRemembrance++;
            if (ticksSinceLastRemembrance >= RemembranceInterval)
            {
                ticksSinceLastRemembrance = 0;
                if (KurinDemigodessMod.Settings.dayOfRemembranceEnabled)
                {
                    TriggerRemembrance();
                }
            }
        }

        private void TryPurgeBadConditions()
        {
            if (!KurinDemigodessMod.Settings.weatherPurgeEnabled) return;
            if (Find.Maps == null) return;

            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                if (!AethiraPresentOnMap(map)) continue;
                if (map.gameConditionManager == null) continue;

                foreach (var name in BadConditionDefs)
                {
                    var def = DefDatabase<GameConditionDef>.GetNamedSilentFail(name);
                    if (def == null) continue;

                    var cond = map.gameConditionManager.GetActiveCondition(def);
                    if (cond != null)
                    {
                        cond.End();
                        Messages.Message(
                            string.Format("Aethira's divine presence dispels the {0}.", def.label),
                            MessageTypeDefOf.PositiveEvent, false);
                    }
                }
            }
        }

        private void TryTriggerGuidance()
        {
            if (Rand.Value > GuidanceChance) return;
            if (Find.Maps == null) return;

            Pawn aethira = null;
            Map aethiraMap = null;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                    {
                        aethira = pawn;
                        aethiraMap = map;
                        break;
                    }
                }
                if (aethira != null) break;
            }
            if (aethira == null || aethiraMap == null) return;

            var candidates = aethiraMap.mapPawns.FreeColonistsSpawned
                .Where(c => c != null && !c.Dead && c != aethira && !Gene_Demigodess.HasDemigodessGene(c))
                .ToList();
            if (candidates.Count == 0) return;

            var target = candidates.RandomElement();
            if (Kurin_DefOf.DG_GuidedByDemigodess != null && target.needs?.mood?.thoughts?.memories != null)
            {
                target.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_GuidedByDemigodess);
            }

            // Favor source #4: Aethira's Guidance grants favor
            if (KurinDemigodessMod.Settings.divineFavorEnabled)
            {
                Current.Game?.GetComponent<GameComponent_DivineFavor>()?.Add(3);
            }

            Find.LetterStack.ReceiveLetter(
                "Aethira's Guidance",
                string.Format(
                    "Aethira Dawnforge sought out {0} today. They spoke for hours of {0}'s worries, and when {1} finally left, {0} felt the weight of the world lift from their shoulders.",
                    target.LabelShort, aethira.LabelShort),
                LetterDefOf.PositiveEvent, new LookTargets(target, aethira));
        }

        private void TriggerRemembrance()
        {
            if (Find.Maps == null) return;

            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                int touched = 0;
                foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist == null || Gene_Demigodess.HasDemigodessGene(colonist)) continue;
                    if (Kurin_DefOf.DG_DayOfRemembrance != null && colonist.needs?.mood?.thoughts?.memories != null)
                    {
                        colonist.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_DayOfRemembrance);
                        touched++;
                    }
                }

                if (touched > 0)
                {
                    // Favor source #3: annual remembrance grants a large favor burst
                    if (KurinDemigodessMod.Settings.divineFavorEnabled)
                    {
                        Current.Game?.GetComponent<GameComponent_DivineFavor>()?.Add(30);
                    }

                    Find.LetterStack.ReceiveLetter(
                        "Day of Remembrance",
                        "The colony honors the Divine Herald today. Songs are sung, old stories retold, and all pause to remember what Aethira Dawnforge has done for the Kurin people. Every heart is lighter.",
                        LetterDefOf.PositiveEvent);
                    break;
                }
            }
        }

        private static bool AethiraPresentOnMap(Map map)
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn != null && !pawn.Dead && Gene_Demigodess.HasDemigodessGene(pawn))
                    return true;
            }
            return false;
        }
    }
}
