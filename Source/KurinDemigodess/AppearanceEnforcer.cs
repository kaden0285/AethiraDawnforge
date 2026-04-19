using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Reflection-based per-pawn appearance enforcement for Aethira.
    /// Covers two mods she cares about:
    ///
    /// 1. Humanoid Alien Races (HAR) - forces every bodyAddon variant index to 0.
    ///    This pins her ears to the base "A" (straight / pointed-up) texture instead of
    ///    letting HAR roll the "A1" flopped variant.
    ///
    /// 2. Kurin HAR Facial Animation / [NL] Facial Animation - forces eye color to jade
    ///    green on the EyeballControllerComp. Without this, she inherits whatever random
    ///    eye color the facial animation system rolls.
    ///
    /// Both are silent no-ops if the respective mod isn't loaded. Fields/methods are
    /// looked up lazily and cached on first successful hit.
    /// </summary>
    public static class AppearanceEnforcer
    {
        // Jade green, RGB(0, 168, 107) normalized. Chosen to read as a rich divine green.
        public static readonly Color JadeGreen = new Color(0f, 0.659f, 0.420f, 1f);

        // HAR reflection cache
        private static bool harLookupDone;
        private static Type harAlienCompType;
        private static FieldInfo harAddonVariantsField;

        // Facial Animation reflection cache
        private static bool faLookupDone;
        private static Type faEyeballControllerCompType;
        private static FieldInfo faEyeballColorField;
        private static FieldInfo faEyeColorDefField;

        /// <summary>
        /// Runs both enforcement passes on the pawn. Safe to call every heavy cycle.
        /// </summary>
        public static void Enforce(Pawn pawn)
        {
            if (pawn == null) return;
            try { ForceStraightEars(pawn); }
            catch (Exception ex) { Log.Warning("[KurinDemigodess] Ear enforcement failed: " + ex.Message); }

            try { ForceEyeColor(pawn, JadeGreen); }
            catch (Exception ex) { Log.Warning("[KurinDemigodess] Eye enforcement failed: " + ex.Message); }
        }

        /// <summary>
        /// Sets every entry in HAR AlienComp.addonVariants to 0 so the base (pointed-up)
        /// ear texture is always picked. Silent no-op if HAR isn't loaded or the pawn
        /// doesn't have an AlienComp.
        /// </summary>
        public static void ForceStraightEars(Pawn pawn)
        {
            if (!EnsureHarLookup()) return;

            ThingComp comp = null;
            if (pawn.AllComps != null)
            {
                for (int i = 0; i < pawn.AllComps.Count; i++)
                {
                    if (harAlienCompType.IsInstanceOfType(pawn.AllComps[i]))
                    {
                        comp = pawn.AllComps[i];
                        break;
                    }
                }
            }
            if (comp == null) return;

            var list = harAddonVariantsField.GetValue(comp) as IList;
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                // list is List<int>; force 0 for the primary variant.
                if (!(list[i] is int v) || v != 0)
                {
                    list[i] = 0;
                }
            }
        }

        /// <summary>
        /// Sets the Facial Animation EyeballControllerComp color field to the given
        /// color and clears the eyeColorDef (so the def doesn't overwrite us later).
        /// Silent no-op if Facial Animation isn't loaded.
        /// </summary>
        public static void ForceEyeColor(Pawn pawn, Color color)
        {
            if (!EnsureFaLookup()) return;

            ThingComp comp = null;
            if (pawn.AllComps != null)
            {
                for (int i = 0; i < pawn.AllComps.Count; i++)
                {
                    if (faEyeballControllerCompType.IsInstanceOfType(pawn.AllComps[i]))
                    {
                        comp = pawn.AllComps[i];
                        break;
                    }
                }
            }
            if (comp == null) return;

            if (faEyeballColorField != null)
            {
                var current = faEyeballColorField.GetValue(comp);
                if (!(current is Color cc) || cc != color)
                {
                    faEyeballColorField.SetValue(comp, color);
                }
            }

            if (faEyeColorDefField != null)
            {
                // Null the def so our direct color override sticks across redraws.
                if (faEyeColorDefField.GetValue(comp) != null)
                {
                    faEyeColorDefField.SetValue(comp, null);
                }
            }
        }

        private static bool EnsureHarLookup()
        {
            if (harLookupDone) return harAlienCompType != null && harAddonVariantsField != null;
            harLookupDone = true;

            harAlienCompType = AccessTools.TypeByName("AlienRace.AlienComp");
            if (harAlienCompType == null) return false;

            harAddonVariantsField = AccessTools.Field(harAlienCompType, "addonVariants");
            if (harAddonVariantsField == null)
            {
                Log.Warning("[KurinDemigodess] HAR AlienComp.addonVariants field not found; ear override disabled.");
                return false;
            }
            return true;
        }

        private static bool EnsureFaLookup()
        {
            if (faLookupDone) return faEyeballControllerCompType != null && faEyeballColorField != null;
            faLookupDone = true;

            faEyeballControllerCompType = AccessTools.TypeByName("FacialAnimation.EyeballControllerComp");
            if (faEyeballControllerCompType == null) return false;

            faEyeballColorField = AccessTools.Field(faEyeballControllerCompType, "eyeballColor");
            faEyeColorDefField = AccessTools.Field(faEyeballControllerCompType, "eyeColorDef");

            if (faEyeballColorField == null)
            {
                Log.Warning("[KurinDemigodess] FacialAnimation eyeballColor field not found; eye color override disabled.");
                return false;
            }
            return true;
        }
    }
}
