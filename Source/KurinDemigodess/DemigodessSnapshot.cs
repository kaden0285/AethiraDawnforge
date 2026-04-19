using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// On-disk backup of Aethira's mutable mind-state.
    /// Saved to the RimWorld config folder as a plain-text file, per-save via a UUID.
    /// Used as Layer 7 of the death-protection chain: when EmergencyRespawn falls back
    /// to generating a fresh pawn from PawnKindDef (savedDemigodess reference is null or
    /// destroyed), this snapshot overlays as much of the player's real Aethira onto the
    /// new pawn as possible.
    ///
    /// WHAT IT SAVES (v2):
    ///  - Skills: level, xpSinceLastLevel, passion
    ///  - Direct relations (to other named pawns)
    ///  - Mood memories (ThoughtDef + optional other pawn name)
    ///  - Work priorities
    ///  - Policy assignments: outfit, drug, food, area
    ///  - Player settings: hostility response, medical care level
    ///  - Royal titles per faction (Royalty DLC) - best-effort restore
    ///
    /// WHAT IT DELIBERATELY DOES NOT SAVE:
    ///  - Apparel / equipment / inventory - if her body dissolved to nothing, gear cannot
    ///    teleport back with her. Narrative rule.
    ///  - Identity fields (name, age, genes, traits, backstories, hair, head, body, tattoos)
    ///    - all force-enforced by Gene_Demigodess.EnforceIdentity on every PostAdd + heavy
    ///    tick. Redundant to save.
    ///  - Health state (injuries, scars, hediffs) - she's being rebuilt from nothing.
    ///  - Transient state (needs, current job, position).
    ///
    /// Intentionally NOT serialized via Scribe - avoids cross-reference tangles and runs
    /// outside the main save/load flow. Plain-text line format so it's debuggable.
    ///
    /// Line format (pipe-separated):
    ///   VERSION|2
    ///   SAVED_AT|2026-04-15 02:30:00
    ///   TICK|123456789
    ///   NAME|Aethira 'Aethira' Dawnforge
    ///   SKILL|defName|level|xp|passionInt
    ///   RELATION|relationDefName|otherPawnFullName
    ///   MEMORY|thoughtDefName|otherPawnFullName_or_empty
    ///   WORK|workTypeDefName|priority
    ///   POLICY|kind|idOrLabel         (kind = OUTFIT/DRUG/FOOD/AREA)
    ///   SETTING|name|value            (name = HOSTILITY/MEDCARE)
    ///   ROYALTITLE|factionName|titleDefName
    /// </summary>
    public static class DemigodessSnapshot
    {
        private const int CurrentVersion = 2;

        private static string GetFilePath(string worldUuid)
        {
            string suffix = string.IsNullOrEmpty(worldUuid) ? "default" : worldUuid;
            return Path.Combine(
                GenFilePaths.ConfigFolderPath,
                "KurinDemigodess_PawnBackup_" + suffix + ".txt");
        }

        public static string GetDiskPath(string worldUuid)
        {
            return GetFilePath(worldUuid);
        }

        public static bool FileExists(string worldUuid)
        {
            try { return File.Exists(GetFilePath(worldUuid)); }
            catch { return false; }
        }

        // ============================================================
        // SAVE
        // ============================================================

        public static void Save(Pawn pawn, string worldUuid)
        {
            if (pawn == null || pawn.Dead) return;

            try
            {
                var lines = new List<string>();
                lines.Add("VERSION|" + CurrentVersion);
                lines.Add("SAVED_AT|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add("TICK|" + (Find.TickManager != null ? Find.TickManager.TicksGame : 0));
                lines.Add("NAME|" + (pawn.Name != null ? pawn.Name.ToStringFull : "unknown"));

                // Skills
                if (pawn.skills != null)
                {
                    foreach (var s in pawn.skills.skills)
                    {
                        if (s == null || s.def == null) continue;
                        lines.Add(string.Format("SKILL|{0}|{1}|{2}|{3}",
                            s.def.defName, s.Level, s.xpSinceLastLevel, (int)s.passion));
                    }
                }

                // Relations
                if (pawn.relations != null)
                {
                    foreach (var r in pawn.relations.DirectRelations)
                    {
                        if (r == null || r.def == null || r.otherPawn == null) continue;
                        string otherName = r.otherPawn.Name != null ? r.otherPawn.Name.ToStringFull : "";
                        if (string.IsNullOrEmpty(otherName)) continue;
                        lines.Add(string.Format("RELATION|{0}|{1}", r.def.defName, otherName));
                    }
                }

                // Memories (mood thoughts)
                if (pawn.needs != null && pawn.needs.mood != null
                    && pawn.needs.mood.thoughts != null && pawn.needs.mood.thoughts.memories != null)
                {
                    foreach (var memory in pawn.needs.mood.thoughts.memories.Memories)
                    {
                        if (memory == null || memory.def == null) continue;
                        string other = memory.otherPawn != null && memory.otherPawn.Name != null
                            ? memory.otherPawn.Name.ToStringFull
                            : "";
                        lines.Add(string.Format("MEMORY|{0}|{1}", memory.def.defName, other));
                    }
                }

                // Work priorities
                if (pawn.workSettings != null && pawn.workSettings.EverWork)
                {
                    foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (wt == null) continue;
                        int priority = pawn.workSettings.GetPriority(wt);
                        lines.Add(string.Format("WORK|{0}|{1}", wt.defName, priority));
                    }
                }

                // Policies
                if (pawn.outfits != null && pawn.outfits.CurrentApparelPolicy != null)
                {
                    lines.Add(string.Format("POLICY|OUTFIT|{0}", pawn.outfits.CurrentApparelPolicy.id));
                }
                if (pawn.drugs != null && pawn.drugs.CurrentPolicy != null)
                {
                    lines.Add(string.Format("POLICY|DRUG|{0}", pawn.drugs.CurrentPolicy.id));
                }
                if (pawn.foodRestriction != null && pawn.foodRestriction.CurrentFoodPolicy != null)
                {
                    lines.Add(string.Format("POLICY|FOOD|{0}", pawn.foodRestriction.CurrentFoodPolicy.id));
                }
                if (pawn.playerSettings != null && pawn.playerSettings.AreaRestrictionInPawnCurrentMap != null)
                {
                    lines.Add(string.Format("POLICY|AREA|{0}", pawn.playerSettings.AreaRestrictionInPawnCurrentMap.Label));
                }

                // Player settings
                if (pawn.playerSettings != null)
                {
                    lines.Add(string.Format("SETTING|HOSTILITY|{0}", (int)pawn.playerSettings.hostilityResponse));
                    lines.Add(string.Format("SETTING|MEDCARE|{0}", (int)pawn.playerSettings.medCare));
                }

                // Royal titles (Royalty DLC) - best effort
                if (pawn.royalty != null)
                {
                    try
                    {
                        foreach (var title in pawn.royalty.AllTitlesForReading)
                        {
                            if (title == null || title.def == null || title.faction == null) continue;
                            lines.Add(string.Format("ROYALTITLE|{0}|{1}",
                                title.faction.Name ?? "", title.def.defName));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[KurinDemigodess] Snapshot: royal title save failed: " + ex.Message);
                    }
                }

                File.WriteAllLines(GetFilePath(worldUuid), lines);
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Snapshot save failed: " + ex.Message);
            }
        }

        // ============================================================
        // LOAD + APPLY
        // ============================================================

        /// <summary>
        /// Reads the snapshot from disk and overlays it onto the given pawn.
        /// </summary>
        /// <param name="skipIdentityData">
        /// When true, restore ONLY work priorities, policies, player settings, and royal titles.
        /// Skip skills, relations, and memories - used for corpse resurrection where those fields
        /// are preserved on the pawn instance but the colonist-management settings get reset.
        /// When false (default), restore everything - used for "rebuilt from nothing" respawns.
        /// </param>
        public static bool LoadAndApply(Pawn pawn, string worldUuid, bool skipIdentityData = false)
        {
            if (pawn == null) return false;

            string path = GetFilePath(worldUuid);
            if (!File.Exists(path)) return false;

            try
            {
                var lines = File.ReadAllLines(path);
                int version = 0;
                int savedTick = 0;

                var skillRecs = new List<SkillRec>();
                var relationRecs = new List<RelationRec>();
                var memoryRecs = new List<MemoryRec>();
                var workRecs = new List<WorkRec>();
                var policyRecs = new List<PolicyRec>();
                var settingRecs = new List<SettingRec>();
                var royalTitleRecs = new List<RoyalTitleRec>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;

                    switch (parts[0])
                    {
                        case "VERSION":
                            int.TryParse(parts[1], out version);
                            break;

                        case "TICK":
                            int.TryParse(parts[1], out savedTick);
                            break;

                        case "SKILL":
                            if (parts.Length >= 5)
                            {
                                int lvl; float xp; int pas;
                                if (int.TryParse(parts[2], out lvl) &&
                                    float.TryParse(parts[3], out xp) &&
                                    int.TryParse(parts[4], out pas))
                                {
                                    skillRecs.Add(new SkillRec
                                    {
                                        defName = parts[1], level = lvl, xp = xp, passion = (Passion)pas
                                    });
                                }
                            }
                            break;

                        case "RELATION":
                            if (parts.Length >= 3)
                                relationRecs.Add(new RelationRec { relationDef = parts[1], otherName = parts[2] });
                            break;

                        case "MEMORY":
                            if (parts.Length >= 3)
                                memoryRecs.Add(new MemoryRec { thoughtDef = parts[1], otherName = parts[2] });
                            break;

                        case "WORK":
                            if (parts.Length >= 3)
                            {
                                int prio;
                                if (int.TryParse(parts[2], out prio))
                                    workRecs.Add(new WorkRec { workDef = parts[1], priority = prio });
                            }
                            break;

                        case "POLICY":
                            if (parts.Length >= 3)
                                policyRecs.Add(new PolicyRec { kind = parts[1], value = parts[2] });
                            break;

                        case "SETTING":
                            if (parts.Length >= 3)
                                settingRecs.Add(new SettingRec { name = parts[1], value = parts[2] });
                            break;

                        case "APPAREL":
                            // Legacy v1 files may have APPAREL lines. Ignore.
                            // Gear is intentionally never restored from snapshot.
                            break;

                        case "ROYALTITLE":
                            if (parts.Length >= 3)
                                royalTitleRecs.Add(new RoyalTitleRec { factionName = parts[1], titleDef = parts[2] });
                            break;
                    }
                }

                if (version < 1 || version > CurrentVersion)
                {
                    Log.Warning(string.Format(
                        "[KurinDemigodess] Snapshot version unsupported (file={0}, expected 1-{1}). Skipping restore.",
                        version, CurrentVersion));
                    return false;
                }

                int applied = 0;

                // ===== Identity data (skills / relations / memories) =====
                // Skipped for corpse resurrection since the pawn instance preserves these.

                if (!skipIdentityData)
                {
                    // Skills
                    if (pawn.skills != null)
                    {
                        foreach (var rec in skillRecs)
                        {
                            var def = DefDatabase<SkillDef>.GetNamedSilentFail(rec.defName);
                            if (def == null) continue;
                            var skill = pawn.skills.GetSkill(def);
                            if (skill == null || skill.TotallyDisabled) continue;
                            skill.Level = rec.level;
                            skill.xpSinceLastLevel = rec.xp;
                            skill.passion = rec.passion;
                            applied++;
                        }
                    }

                    // Relations (best effort - match by full name across maps + world)
                    if (pawn.relations != null)
                    {
                        foreach (var rec in relationRecs)
                        {
                            var def = DefDatabase<PawnRelationDef>.GetNamedSilentFail(rec.relationDef);
                            if (def == null) continue;
                            var other = FindPawnByFullName(rec.otherName);
                            if (other == null || other == pawn) continue;

                            try
                            {
                                if (!pawn.relations.DirectRelationExists(def, other))
                                {
                                    pawn.relations.AddDirectRelation(def, other);
                                    applied++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning("[KurinDemigodess] Snapshot: relation restore failed (" + def.defName + "): " + ex.Message);
                            }
                        }
                    }

                    // Memories (mood thoughts)
                    if (pawn.needs != null && pawn.needs.mood != null
                        && pawn.needs.mood.thoughts != null && pawn.needs.mood.thoughts.memories != null)
                    {
                        foreach (var rec in memoryRecs)
                        {
                            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(rec.thoughtDef);
                            if (def == null) continue;
                            try
                            {
                                Pawn other = null;
                                if (!string.IsNullOrEmpty(rec.otherName))
                                    other = FindPawnByFullName(rec.otherName);
                                pawn.needs.mood.thoughts.memories.TryGainMemory(def, other);
                                applied++;
                            }
                            catch (Exception ex)
                            {
                                Log.Warning("[KurinDemigodess] Snapshot: memory restore failed (" + rec.thoughtDef + "): " + ex.Message);
                            }
                        }
                    }
                }

                // Apparel/equipment/inventory are intentionally NOT restored (see class doc).

                // Work priorities
                if (pawn.workSettings != null && pawn.workSettings.EverWork)
                {
                    foreach (var rec in workRecs)
                    {
                        var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(rec.workDef);
                        if (wt == null) continue;
                        try
                        {
                            pawn.workSettings.SetPriority(wt, rec.priority);
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[KurinDemigodess] Snapshot: work priority restore failed (" + wt.defName + "): " + ex.Message);
                        }
                    }
                }

                // Policies
                foreach (var rec in policyRecs)
                {
                    try
                    {
                        ApplyPolicy(pawn, rec.kind, rec.value);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[KurinDemigodess] Snapshot: policy restore failed (" + rec.kind + "): " + ex.Message);
                    }
                }

                // Player settings
                if (pawn.playerSettings != null)
                {
                    foreach (var rec in settingRecs)
                    {
                        try
                        {
                            int val;
                            if (!int.TryParse(rec.value, out val)) continue;
                            if (rec.name == "HOSTILITY")
                                pawn.playerSettings.hostilityResponse = (HostilityResponseMode)val;
                            else if (rec.name == "MEDCARE")
                                pawn.playerSettings.medCare = (MedicalCareCategory)val;
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[KurinDemigodess] Snapshot: setting restore failed (" + rec.name + "): " + ex.Message);
                        }
                    }
                }

                // Royal titles (restored in both modes - they can get reset)
                if (pawn.royalty != null && royalTitleRecs.Count > 0)
                {
                    foreach (var rec in royalTitleRecs)
                    {
                        try
                        {
                            var titleDef = DefDatabase<RoyalTitleDef>.GetNamedSilentFail(rec.titleDef);
                            if (titleDef == null) continue;
                            var faction = Find.FactionManager?.AllFactions?.FirstOrDefault(f => f.Name == rec.factionName);
                            if (faction == null) continue;
                            pawn.royalty.SetTitle(faction, titleDef, false, false, false);
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[KurinDemigodess] Snapshot: royal title restore failed (" + rec.titleDef + "): " + ex.Message);
                        }
                    }
                }

                Log.Message(string.Format(
                    "[KurinDemigodess] Snapshot v{0} restored (skipIdentity={1}). Applied {2} records from tick {3}.",
                    version, skipIdentityData, applied, savedTick));
                return applied > 0;
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Snapshot load/apply failed: " + ex.Message);
                return false;
            }
        }

        private static void ApplyPolicy(Pawn pawn, string kind, string value)
        {
            int id;
            switch (kind)
            {
                case "OUTFIT":
                    if (int.TryParse(value, out id) && pawn.outfits != null && Current.Game?.outfitDatabase != null)
                    {
                        var outfit = Current.Game.outfitDatabase.AllOutfits.FirstOrDefault(o => o.id == id);
                        if (outfit != null) pawn.outfits.CurrentApparelPolicy = outfit;
                    }
                    break;

                case "DRUG":
                    if (int.TryParse(value, out id) && pawn.drugs != null && Current.Game?.drugPolicyDatabase != null)
                    {
                        var drug = Current.Game.drugPolicyDatabase.AllPolicies.FirstOrDefault(d => d.id == id);
                        if (drug != null) pawn.drugs.CurrentPolicy = drug;
                    }
                    break;

                case "FOOD":
                    if (int.TryParse(value, out id) && pawn.foodRestriction != null && Current.Game?.foodRestrictionDatabase != null)
                    {
                        var food = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.FirstOrDefault(f => f.id == id);
                        if (food != null) pawn.foodRestriction.CurrentFoodPolicy = food;
                    }
                    break;

                case "AREA":
                    if (pawn.playerSettings != null && pawn.Map != null && pawn.Map.areaManager != null)
                    {
                        var area = pawn.Map.areaManager.AllAreas.FirstOrDefault(a => a.Label == value);
                        if (area != null) pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    }
                    break;
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static Pawn FindPawnByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    foreach (var p in map.mapPawns.AllPawns)
                    {
                        if (p != null && p.Name != null && p.Name.ToStringFull == fullName)
                            return p;
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (p != null && p.Name != null && p.Name.ToStringFull == fullName)
                        return p;
                }
            }

            return null;
        }

        // ============================================================
        // RECORD STRUCTS
        // ============================================================

        private class SkillRec
        {
            public string defName;
            public int level;
            public float xp;
            public Passion passion;
        }

        private class RelationRec
        {
            public string relationDef;
            public string otherName;
        }

        private class MemoryRec
        {
            public string thoughtDef;
            public string otherName;
        }

        private class WorkRec
        {
            public string workDef;
            public int priority;
        }

        private class PolicyRec
        {
            public string kind;
            public string value;
        }

        private class SettingRec
        {
            public string name;
            public string value;
        }

        private class RoyalTitleRec
        {
            public string factionName;
            public string titleDef;
        }
    }
}
