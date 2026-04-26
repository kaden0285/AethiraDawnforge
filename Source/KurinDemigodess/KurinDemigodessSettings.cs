using UnityEngine;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// How Aethira enters the player's colony when they didn't pick a Dawnforge
    /// scenario. Scenario starts always place her at tick 0 regardless of this
    /// setting; this only governs non-scenario starts and existing-save adoption.
    /// </summary>
    public enum RecruitmentMode
    {
        /// <summary>
        /// (Default for non-scenario starts.) Aethira is the leader of the
        /// Dawnforge Collective, an NPC faction in the world. The player must
        /// complete a 3-step quest chain (or use the Petition the Collective
        /// comms-console option) to recruit her. This is the canonical post-1.6
        /// experience.
        /// </summary>
        Quest = 0,

        /// <summary>
        /// Legacy behavior: ~60 seconds after game start the failsafe
        /// "Welcome Spawns" Aethira out of the wilderness directly into the
        /// player's colony with a Divine Arrival letter, no quest required.
        /// </summary>
        AutoSpawn = 1,

        /// <summary>
        /// Off. The mod will never auto-spawn Aethira into the player's colony
        /// or seed her as a Dawnforge Collective leader. The player must use
        /// one of the included scenarios. The failsafe still protects her if
        /// she's already present, it just won't conjure her from nothing.
        /// </summary>
        Disabled = 2,
    }

    /// <summary>
    /// Mod settings data class. Values default to the hard-coded behavior
    /// from the mod's original design. Users can tune them in the mod options menu.
    /// </summary>
    public class KurinDemigodessSettings : ModSettings
    {
        // ===== Failsafe =====
        public int failsafeScanInterval = 1200;     // Ticks between unified scans (default = 20s in-game; × 3 miss threshold = 60s time-to-trigger)
        public int failsafeMissThreshold = 3;       // Consecutive misses before emergency respawn
        public bool enableFailsafeLog = true;       // Write KurinDemigodess_FailsafeLog.txt

        // ===== Ascension =====
        public int ascensionDurationDays = 7;       // Days before ascended Demigodess returns

        // ===== Gene enforcement =====
        public int normalEnforceInterval = 600;    // Cheap checks: food, regen, tend, psylink, purge status
        public int heavyEnforceInterval = 7500;    // Expensive checks: identity, genes, traits, passions

        // ===== Recruitment =====
        public RecruitmentMode recruitmentMode = RecruitmentMode.Quest;

        // ===== Lore/fun features =====
        public bool divineFavorEnabled = true;
        public bool deathRecoilEnabled = true;
        public float deathRecoilRadius = 12f;
        public bool pilgrimageEnabled = true;
        public bool firstLoadLetterEnabled = true;
        public bool weatherPurgeEnabled = true;
        public bool guidanceEventsEnabled = true;
        public bool dayOfRemembranceEnabled = true;
        public bool favorScalingAuraEnabled = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref failsafeScanInterval, "failsafeScanInterval", 1200);
            Scribe_Values.Look(ref failsafeMissThreshold, "failsafeMissThreshold", 3);
            Scribe_Values.Look(ref enableFailsafeLog, "enableFailsafeLog", true);
            Scribe_Values.Look(ref ascensionDurationDays, "ascensionDurationDays", 7);
            Scribe_Values.Look(ref normalEnforceInterval, "normalEnforceInterval", 600);
            Scribe_Values.Look(ref heavyEnforceInterval, "heavyEnforceInterval", 7500);
            Scribe_Values.Look(ref recruitmentMode, "recruitmentMode", RecruitmentMode.Quest);
            Scribe_Values.Look(ref divineFavorEnabled, "divineFavorEnabled", true);
            Scribe_Values.Look(ref deathRecoilEnabled, "deathRecoilEnabled", true);
            Scribe_Values.Look(ref deathRecoilRadius, "deathRecoilRadius", 12f);
            Scribe_Values.Look(ref pilgrimageEnabled, "pilgrimageEnabled", true);
            Scribe_Values.Look(ref firstLoadLetterEnabled, "firstLoadLetterEnabled", true);
            Scribe_Values.Look(ref weatherPurgeEnabled, "weatherPurgeEnabled", true);
            Scribe_Values.Look(ref guidanceEventsEnabled, "guidanceEventsEnabled", true);
            Scribe_Values.Look(ref dayOfRemembranceEnabled, "dayOfRemembranceEnabled", true);
            Scribe_Values.Look(ref favorScalingAuraEnabled, "favorScalingAuraEnabled", true);
        }
    }

    /// <summary>
    /// The Verse.Mod subclass that RimWorld instantiates on load. Hosts the settings
    /// menu and applies all Harmony patches. Replaces the old [StaticConstructorOnStartup]
    /// init class - patches now apply in this constructor instead.
    /// </summary>
    public class KurinDemigodessMod : Mod
    {
        private static KurinDemigodessSettings _settings;

        /// <summary>
        /// Global settings accessor. Call from anywhere: KurinDemigodessMod.Settings.failsafeMissThreshold
        /// Falls back to defaults if the mod hasn't been instantiated yet (shouldn't happen in practice).
        /// </summary>
        public static KurinDemigodessSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = new KurinDemigodessSettings();
                }
                return _settings;
            }
        }

        public KurinDemigodessMod(ModContentPack content) : base(content)
        {
            _settings = GetSettings<KurinDemigodessSettings>();

            // One-time migration: old default was 250 ticks (~12s time-to-trigger at
            // 3-miss threshold). New default is 1200 ticks (~60s). Users who customized
            // to something else keep their value.
            if (_settings.failsafeScanInterval == 250)
            {
                _settings.failsafeScanInterval = 1200;
                Log.Message("[KurinDemigodess] Migrated failsafeScanInterval 250 → 1200 (60s time-to-trigger).");
            }

            ApplyHarmonyPatches();
        }

        public override string SettingsCategory()
        {
            return "Kurin Demigodess";
        }

        // Scroll state for the settings window. The full menu has ~14 controls
        // plus headers and overflows the default settings rect on smaller
        // resolutions, hiding the bottom checkboxes (Day of Remembrance,
        // Aethira's Guidance, etc). Wrapping in a scroll view keeps every
        // option reachable at any window height.
        private Vector2 settingsScrollPos = Vector2.zero;
        private const float SettingsContentHeight = 1100f;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var viewRect = new Rect(0f, 0f, inRect.width - 16f, SettingsContentHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPos, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            list.Label("Failsafe");
            list.GapLine(4f);

            list.Label(string.Format("Scan interval: {0} ticks (~{1}s in-game)",
                Settings.failsafeScanInterval, Settings.failsafeScanInterval / 60));
            Settings.failsafeScanInterval = (int)list.Slider(Settings.failsafeScanInterval, 60, 3600);

            list.Label(string.Format("Miss threshold before emergency respawn: {0} (~{1}s total)",
                Settings.failsafeMissThreshold,
                Settings.failsafeMissThreshold * Settings.failsafeScanInterval / 60));
            Settings.failsafeMissThreshold = (int)list.Slider(Settings.failsafeMissThreshold, 1, 10);

            list.CheckboxLabeled("Write failsafe log file", ref Settings.enableFailsafeLog,
                "Appends events to KurinDemigodess_FailsafeLog.txt in the RimWorld config folder. Rotates at 200KB.");

            list.Gap(12f);
            list.Label("Recruitment (non-scenario starts)");
            list.GapLine(4f);

            list.Label("How Aethira enters the colony if you didn't pick a Dawnforge scenario:");

            if (list.RadioButton("Quest (default) - she leads the Dawnforge Collective; recruit via 3-step quest chain or Petition the Collective comms option",
                Settings.recruitmentMode == RecruitmentMode.Quest, 0f,
                "She is the NPC leader of the Dawnforge Collective faction. Complete the quest chain (Sanctuary -> Trial -> Calling) to bring her to your colony. As an alternative, the comms console can Petition the Collective for a steep silver/goodwill cost."))
            {
                Settings.recruitmentMode = RecruitmentMode.Quest;
            }

            if (list.RadioButton("Auto-spawn (legacy) - she walks out of the wilderness 60 seconds after game start",
                Settings.recruitmentMode == RecruitmentMode.AutoSpawn, 0f,
                "Old behavior: the failsafe Welcome-Spawns Aethira directly into your colony with a Divine Arrival letter, no quest required."))
            {
                Settings.recruitmentMode = RecruitmentMode.AutoSpawn;
            }

            if (list.RadioButton("Disabled - she is never auto-spawned; only the included scenarios place her",
                Settings.recruitmentMode == RecruitmentMode.Disabled, 0f,
                "The mod will not seed her into any faction or colony. The failsafe still protects her if she's already present, it just won't conjure her from nothing. For players who want only the scenario experience."))
            {
                Settings.recruitmentMode = RecruitmentMode.Disabled;
            }

            list.Gap(12f);
            list.Label("Ascension");
            list.GapLine(4f);

            list.Label(string.Format("Ascension duration: {0} day(s)", Settings.ascensionDurationDays));
            Settings.ascensionDurationDays = (int)list.Slider(Settings.ascensionDurationDays, 1, 30);

            list.Gap(12f);
            list.Label("Gene enforcement");
            list.GapLine(4f);

            list.Label(string.Format("Normal enforcement interval: {0} ticks", Settings.normalEnforceInterval));
            Settings.normalEnforceInterval = (int)list.Slider(Settings.normalEnforceInterval, 60, 2500);

            list.Label(string.Format("Heavy enforcement interval (identity/genes/traits): {0} ticks", Settings.heavyEnforceInterval));
            Settings.heavyEnforceInterval = (int)list.Slider(Settings.heavyEnforceInterval, 600, 30000);

            list.Gap(12f);
            list.Label("Features");
            list.GapLine(4f);

            list.CheckboxLabeled("First-load narrative letter", ref Settings.firstLoadLetterEnabled,
                "Send a lore-intro letter the first time Aethira appears on a new colony.");

            list.CheckboxLabeled("Death-recoil AoE pulse", ref Settings.deathRecoilEnabled,
                "When Aethira is killed, an explosive wave of divine energy erupts around her corpse.");

            if (Settings.deathRecoilEnabled)
            {
                list.Label(string.Format("Death-recoil radius: {0:0.0} tiles", Settings.deathRecoilRadius));
                Settings.deathRecoilRadius = list.Slider(Settings.deathRecoilRadius, 4f, 30f);
            }

            list.CheckboxLabeled("Pilgrimage events", ref Settings.pilgrimageEnabled,
                "Occasional Kurin pilgrims arrive to pay respects to the Demigodess.");

            list.CheckboxLabeled("Divine Favor resource", ref Settings.divineFavorEnabled,
                "Aethira accumulates Divine Favor over time that can be spent on powerful effects.");

            list.CheckboxLabeled("Favor-scaling healing aura", ref Settings.favorScalingAuraEnabled,
                "Aethira's healing aura strength scales with current Divine Favor (0 favor = 1x, 100 = 2x).");

            list.CheckboxLabeled("Weather / condition purge", ref Settings.weatherPurgeEnabled,
                "While Aethira is on a home map, toxic fallout / cold snap / heat wave / volcanic winter / flashstorm / noxious haze / psychic drone auto-dispel.");

            list.CheckboxLabeled("Aethira's Guidance random events", ref Settings.guidanceEventsEnabled,
                "Occasionally, Aethira visits a colonist and grants them a mood buff + flavor letter.");

            list.CheckboxLabeled("Day of Remembrance annual event", ref Settings.dayOfRemembranceEnabled,
                "Once per in-game year, a colony-wide mood buff + letter celebrating Aethira's role.");

            list.End();
            Widgets.EndScrollView();
        }

        private void ApplyHarmonyPatches()
        {
            try
            {
                var harmony = new HarmonyLib.Harmony("Kaden.Kurin.HAR.Demigodess");

                int success = 0;
                int failed = 0;

                success += TryPatch(harmony, typeof(DamageCap_Patch), "DamageCap", ref failed);
                success += TryPatch(harmony, typeof(DeathlessDemigodess_Patch), "Deathless", ref failed);
                success += TryPatch(harmony, typeof(AntiKidnap_Patch), "AntiKidnap", ref failed);
                success += TryPatch(harmony, typeof(AntiMentalBreak_Patch), "AntiMentalBreak", ref failed);
                success += TryPatch(harmony, typeof(AntiMentalBreaker_Patch), "AntiMentalBreaker", ref failed);
                success += TryPatch(harmony, typeof(AntiFactionChange_Patch), "AntiFactionChange", ref failed);
                success += TryPatch(harmony, typeof(AntiSell_Patch), "AntiSell", ref failed);
                success += TryPatch(harmony, typeof(CertaintyLock_Patch), "CertaintyLock", ref failed);
                success += TryPatch(harmony, typeof(AntiPsylink_Patch), "AntiPsylink", ref failed);
                success += TryPatch(harmony, typeof(CorpseProtection_Destroy_Patch), "CorpseDestroy", ref failed);
                success += TryPatch(harmony, typeof(CorpseProtection_Damage_Patch), "CorpseDamage", ref failed);
                success += TryPatch(harmony, typeof(CorpseProtection_DeSpawn_Patch), "CorpseDeSpawn", ref failed);
                success += TryPatch(harmony, typeof(CorpseProtection_Prey_Patch), "CorpsePrey", ref failed);
                success += TryPatch(harmony, typeof(CorpseProtection_Food_Patch), "CorpseFood", ref failed);
                success += TryPatch(harmony, typeof(CorpseProtection_Haul_Patch), "CorpseHaul", ref failed);
                success += TryPatch(harmony, typeof(RoofCollapse_Patch), "RoofCollapse", ref failed);
                success += TryPatch(harmony, typeof(MapProtection_Patch), "MapProtection", ref failed);
                success += TryPatch(harmony, typeof(WorldPawnGC_Patch), "WorldPawnGC", ref failed);
                success += TryPatch(harmony, typeof(WorldPawns_PassToWorld_Patch), "WorldPawnsPass", ref failed);
                success += TryPatch(harmony, typeof(WorldPawns_Remove_Patch), "WorldPawnsRemove", ref failed);
                success += TryPatch(harmony, typeof(PawnDiscard_Patch), "PawnDiscard", ref failed);
                success += TryPatch(harmony, typeof(CaravanCapacity_Patch), "CaravanCapacity", ref failed);
                success += TryPatch(harmony, typeof(AntiPsycast_Helper), "AntiPsycastHelper", ref failed);
                success += TryPatch(harmony, typeof(AethiraGizmos_Patch), "AethiraGizmos", ref failed);
                success += TryPatch(harmony, typeof(GuestFavor_Patch), "GuestFavor", ref failed);
                success += TryPatch(harmony, typeof(AntiGeneTransfer_Patch), "AntiGeneTransfer", ref failed);
                success += TryPatch(harmony, typeof(AntiWeaponDrop_Patch), "AntiWeaponDrop", ref failed);
                success += TryPatch(harmony, typeof(KurinNoBeard_Patch), "KurinNoBeard", ref failed);
                success += TryPatch(harmony, typeof(InventoryPreservation_Rot_CompTick_Patch), "InvPreserveRotTick", ref failed);
                success += TryPatch(harmony, typeof(InventoryPreservation_Rot_CompTickRare_Patch), "InvPreserveRotTickRare", ref failed);
                success += TryPatch(harmony, typeof(InventoryPreservation_Deterioration_Patch), "InvPreserveDeterioration", ref failed);

                // Manual: uses runtime type resolution for CostToMoveIntoCell
                try
                {
                    TerrainSpeedImmunity_Patch.ApplyPatch(harmony);
                    success++;
                }
                catch (System.Exception ex)
                {
                    Log.Warning("[KurinDemigodess] Failed to patch TerrainSpeedImmunity: " + ex.Message);
                    failed++;
                }

                // Manual patches (methods with multiple overloads)
                try
                {
                    DiseaseImmunity_Patch.ApplyPatch(harmony);
                    success++;
                }
                catch (System.Exception ex)
                {
                    Log.Warning("[KurinDemigodess] Failed to patch DiseaseImmunity: " + ex.Message);
                    failed++;
                }

                try
                {
                    AntiBanish_Patch.ApplyPatch(harmony);
                    success++;
                }
                catch (System.Exception ex)
                {
                    Log.Warning("[KurinDemigodess] Failed to patch AntiBanish: " + ex.Message);
                    failed++;
                }

                // Optional compat: silent no-op if Perspective Shift isn't installed.
                try
                {
                    PerspectiveShiftCompat_Patch.ApplyPatch(harmony);
                }
                catch (System.Exception ex)
                {
                    Log.Warning("[KurinDemigodess] Failed to patch PerspectiveShiftCompat: " + ex.Message);
                }

                Log.Message(string.Format("[KurinDemigodess] Harmony patches applied. {0} succeeded, {1} failed.", success, failed));
            }
            catch (System.Exception ex)
            {
                Log.Error("[KurinDemigodess] CRITICAL: Mod initialization failed completely: " + ex);
            }
        }

        private static int TryPatch(HarmonyLib.Harmony harmony, System.Type type, string name, ref int failed)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                return 1;
            }
            catch (System.Exception ex)
            {
                Log.Warning(string.Format("[KurinDemigodess] Failed to patch {0}: {1}", name, ex.Message));
                failed++;
                return 0;
            }
        }
    }
}
