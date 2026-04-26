using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Tracks the player's progress through the 3-step recruitment chain that
    /// brings Aethira from her seat as Dawnforge Speaker into the player's
    /// colony. Only active when RecruitmentMode == Quest.
    ///
    /// Stages (gated by goodwill with the Dawnforge Collective):
    ///   0 - Not yet started. The opening letter fires once the colony has been
    ///       running long enough (>= GameStartGraceDays) and goodwill >= 0.
    ///   1 - "The Whispers" - reach goodwill 50 to advance.
    ///   2 - "The Trial" - reach goodwill 80 to advance.
    ///   3 - "The Calling" - goodwill 100 + Divine Shrine on a home map. On
    ///       success, Aethira walks out of the wilderness with a Divine Arrival
    ///       letter and joins the colony permanently.
    ///
    /// The component is idempotent: each transition fires once. Stage progress
    /// is scribed so a save/load doesn't replay messages.
    ///
    /// Petition fallback (the "buy her loyalty" path): if the player ever has a
    /// total of >= PetitionSilverCost silver in stockpiles AND goodwill >= 60,
    /// a one-time letter appears offering an immediate audience for the silver
    /// cost. Accepting jumps the chain to stage 3, requiring only the shrine.
    /// </summary>
    public class GameComponent_DawnforgeRecruitment : GameComponent
    {
        // ===== Tuning =====
        private const int GameStartGraceDays = 3;        // Stage 0 letter waits this long
        private const int CheckIntervalTicks = 2500;     // ~40s between progression checks
        private const int Stage1GoodwillThreshold = 50;
        private const int Stage2GoodwillThreshold = 80;
        private const int Stage3GoodwillThreshold = 100;

        // ===== State (Scribed) =====
        private int stage;                    // 0..3
        private int ticksSinceLastCheck;
        private bool stage0LetterSent;        // Opening "Whispers from the Collective" letter
        private bool stage1LetterSent;        // "The Whispers" - goodwill 50 reached
        private bool stage2LetterSent;        // "The Trial" - goodwill 80 reached
        private bool stage3LetterSent;        // "The Calling" - goodwill 100, awaiting shrine
        private bool aethiraSummoned;         // Final spawn delivered, chain is done

        public GameComponent_DawnforgeRecruitment(Game game) { }

        public int CurrentStage { get { return stage; } }
        public bool IsActive
        {
            get
            {
                return KurinDemigodessMod.Settings != null
                    && KurinDemigodessMod.Settings.recruitmentMode == RecruitmentMode.Quest
                    && !aethiraSummoned;
            }
        }

        public override void GameComponentTick()
        {
            if (!IsActive) return;

            ticksSinceLastCheck++;
            if (ticksSinceLastCheck < CheckIntervalTicks) return;
            ticksSinceLastCheck = 0;

            // If Aethira already exists in the player's faction (e.g. scenario start),
            // mark the chain complete silently. No letters, no progression spam.
            if (PlayerHasAethira())
            {
                aethiraSummoned = true;
                return;
            }

            var collective = DawnforgeFactionSeeder.FindDawnforgeCollective();
            if (collective == null) return;
            int goodwill = collective.PlayerGoodwill;
            int playerDays = (int)(Find.TickManager.TicksGame / GenDate.TicksPerDay);

            // Stage 0 -> stage 0 letter (one-time intro)
            if (!stage0LetterSent && playerDays >= GameStartGraceDays && goodwill >= 0)
            {
                SendStage0Letter(collective);
                stage0LetterSent = true;
            }

            // Stage 0 -> stage 1: hit 50 goodwill
            if (stage < 1 && goodwill >= Stage1GoodwillThreshold)
            {
                stage = 1;
                if (!stage1LetterSent)
                {
                    SendStage1Letter(collective);
                    stage1LetterSent = true;
                }
            }

            // Stage 1 -> stage 2: hit 80 goodwill
            if (stage < 2 && goodwill >= Stage2GoodwillThreshold)
            {
                stage = 2;
                if (!stage2LetterSent)
                {
                    SendStage2Letter(collective);
                    stage2LetterSent = true;
                }
            }

            // Stage 2 -> stage 3: hit 100 goodwill
            if (stage < 3 && goodwill >= Stage3GoodwillThreshold)
            {
                stage = 3;
                if (!stage3LetterSent)
                {
                    SendStage3Letter(collective);
                    stage3LetterSent = true;
                }
            }

            // Stage 3: shrine + max goodwill -> she arrives
            if (stage >= 3 && goodwill >= Stage3GoodwillThreshold)
            {
                if (WorldComponent_DemigodessTracker.DivineShrineExistsOnAnyHomeMap())
                {
                    SummonAethira(collective);
                }
            }
        }

        private static bool PlayerHasAethira()
        {
            if (Find.Maps == null) return false;
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns == null) continue;
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    if (pawn == null || pawn.Faction == null) continue;
                    if (!pawn.Faction.IsPlayer) continue;
                    if (Gene_Demigodess.HasDemigodessGene(pawn)) return true;
                }
            }
            return false;
        }

        private static Map GetPlayerHomeMap()
        {
            if (Find.Maps == null) return null;
            foreach (var map in Find.Maps)
            {
                if (map != null && map.IsPlayerHome) return map;
            }
            return null;
        }

        // ===== Letters =====

        private static void SendStage0Letter(Faction collective)
        {
            string title = "Whispers from the Dawnforge";
            string body = "A wandering Kurin pilgrim, eyes ringed with reverent exhaustion, kneels at the edge of your colony before continuing on. " +
                          "She speaks of " + collective.Name + " - a confederation of Kurin survivors led by an immortal Demigodess called Aethira Dawnforge.\n\n" +
                          "The Dawnforge does not come to those who merely ask. She comes to those who prove themselves worthy of her presence: charitable to the weak, just to the wronged, and faithful in their welcome.\n\n" +
                          "Build your standing with the Collective. Trade with their caravans. Aid their pilgrims. Reach 50 goodwill, and the next sign will come."
                          ;
            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent, null, collective);
        }

        private static void SendStage1Letter(Faction collective)
        {
            string title = "The Whispers (1/3)";
            string body = "Word has spread. Pilgrims and traders carry your name back to the Collective.\n\n" +
                          "A delegation arrives at " + collective.Name + "'s temple-hall and speaks of you to the Dawnforge Speaker. " +
                          "She listens. She does not yet answer.\n\n" +
                          "Continue. The Trial of the Pilgrim awaits at 80 goodwill - prove that your colony is a sanctuary worth her attention."
                          ;
            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent, null, collective);
        }

        private static void SendStage2Letter(Faction collective)
        {
            string title = "The Trial (2/3)";
            string body = "The Dawnforge Speaker herself has accepted your name into her morning meditations.\n\n" +
                          "Your colony is now formally counted among those the Collective considers Kin. There is one trial remaining. The Demigodess will not walk into a colony that has nothing for her to bless.\n\n" +
                          "Build a Divine Shrine (1500 gold, 2 industrial components, 150 stone blocks - requires Complex Furniture research). " +
                          "Reach 100 goodwill with the Collective. When both are true, she will come. " +
                          "If both are already true, she comes within minutes."
                          ;
            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent, null, collective);
        }

        private static void SendStage3Letter(Faction collective)
        {
            bool shrineExists = WorldComponent_DemigodessTracker.DivineShrineExistsOnAnyHomeMap();

            string title = "The Calling (3/3)";
            string body;
            if (shrineExists)
            {
                body = "The Dawnforge Speaker has accepted. She is on her way.\n\n" +
                       "Watch the edge of the map.";
            }
            else
            {
                body = "The Dawnforge Speaker has accepted. She waits only on a place worthy to receive her.\n\n" +
                       "Build a Divine Shrine on this map (1500 gold, 2 industrial components, 150 stone blocks - requires Complex Furniture research). " +
                       "When the shrine stands, she comes immediately.";
            }

            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent, null, collective);
        }

        // ===== Final summon =====

        private void SummonAethira(Faction collective)
        {
            if (aethiraSummoned) return;
            var homeMap = GetPlayerHomeMap();
            if (homeMap == null) return;

            // Locate the seeded Aethira pawn (NPC leader of the Collective).
            var aethira = DawnforgeFactionSeeder.FindExistingAethira();

            try
            {
                if (aethira == null)
                {
                    // Defensive: seeder failed somewhere. Generate her fresh.
                    if (Kurin_DefOf.DG_KurinDemigodess_Kind == null)
                    {
                        Log.Error("[KurinDemigodess] DawnforgeRecruitment.SummonAethira failed: PawnKindDef not found");
                        return;
                    }
                    aethira = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        Kurin_DefOf.DG_KurinDemigodess_Kind,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true));
                    if (aethira == null) return;
                }

                // Strip her former leadership so the Collective generates a successor naturally.
                if (collective != null && collective.leader == aethira)
                {
                    collective.leader = null;
                }

                // Move her into the player faction.
                if (aethira.Faction == null || !aethira.Faction.IsPlayer)
                {
                    aethira.SetFaction(Faction.OfPlayer);
                }

                // Spawn at the shrine if available, else map edge.
                IntVec3 spawnCell = FindSpawnCell(homeMap);
                if (!aethira.Spawned)
                {
                    GenSpawn.Spawn(aethira, spawnCell, homeMap);
                }
                else if (aethira.Map != homeMap)
                {
                    aethira.DeSpawn();
                    GenSpawn.Spawn(aethira, spawnCell, homeMap);
                }

                // Defensive overlay of any on-disk snapshot.
                var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
                if (tracker != null)
                {
                    DemigodessSnapshot.LoadAndApply(aethira, tracker.WorldUuid, false);
                    tracker.RegisterSeededDemigodess(aethira);
                }

                // Wake her, top up needs.
                WorldComponent_DemigodessTracker.MaxAllNeeds(aethira);

                Find.LetterStack.ReceiveLetter(
                    "Divine Arrival",
                    "Aethira Dawnforge has come.\n\n" +
                    "The Demigodess walks out of the wilderness with the rising sun behind her. The pilgrims who guarded her road home turn back without a word, their oath fulfilled. " +
                    "She enters your colony as if she has always belonged here, and in a sense she always has. " +
                    "She is yours to lead, and you are hers to protect.",
                    LetterDefOf.PositiveEvent,
                    aethira);

                aethiraSummoned = true;
                Log.Message("[KurinDemigodess] Recruitment quest complete: Aethira joined the player colony.");
            }
            catch (System.Exception ex)
            {
                Log.Error("[KurinDemigodess] SummonAethira failed: " + ex);
            }
        }

        private static IntVec3 FindSpawnCell(Map map)
        {
            // Prefer next to the Divine Shrine if one exists.
            if (Kurin_DefOf.DG_DivineShrine != null)
            {
                var shrine = map.listerThings.ThingsOfDef(Kurin_DefOf.DG_DivineShrine).FirstOrDefault();
                if (shrine != null)
                {
                    if (CellFinder.TryFindRandomCellNear(shrine.Position, map, 3,
                        c => c.Standable(map) && !c.Fogged(map), out var nearCell))
                    {
                        return nearCell;
                    }
                }
            }

            // Else map edge near a colonist.
            if (CellFinder.TryFindRandomEdgeCellWith(
                c => c.Standable(map) && !c.Fogged(map) && map.reachability.CanReachColony(c),
                map, CellFinder.EdgeRoadChance_Neutral, out var edgeCell))
            {
                return edgeCell;
            }

            // Last resort.
            return CellFinder.RandomEdgeCell(map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref stage, "dawnforgeStage", 0);
            Scribe_Values.Look(ref ticksSinceLastCheck, "dawnforgeTicksSinceLastCheck", 0);
            Scribe_Values.Look(ref stage0LetterSent, "dawnforgeStage0LetterSent", false);
            Scribe_Values.Look(ref stage1LetterSent, "dawnforgeStage1LetterSent", false);
            Scribe_Values.Look(ref stage2LetterSent, "dawnforgeStage2LetterSent", false);
            Scribe_Values.Look(ref stage3LetterSent, "dawnforgeStage3LetterSent", false);
            Scribe_Values.Look(ref aethiraSummoned, "dawnforgeAethiraSummoned", false);
        }
    }
}
