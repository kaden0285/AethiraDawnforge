using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Adds two extra gizmos to Aethira's command bar when she is a selected
    /// player-controlled colonist:
    ///  1. "Favor: N/100" - displays current Divine Favor and invokes the
    ///     heal-all Divine Blessing when clicked (cost: 100).
    ///  2. "Bless colonist" - targeted action that applies a big mood buff to
    ///     any non-Aethira colonist (cost: 10).
    /// Both gizmos honor the divineFavorEnabled setting.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class AethiraGizmos_Patch
    {
        private const int BlessTargetedCost = 10;

        // Cached gizmo icons - loaded lazily on first access. Falls back through
        // a chain of paths so we never end up with BaseContent.BadTex (the pink X).
        private static Texture2D _iconFavor;
        private static Texture2D _iconBless;

        private static Texture2D IconFavor
        {
            get
            {
                if (_iconFavor == null)
                {
                    _iconFavor = ContentFinder<Texture2D>.Get("UI/Icons/Genes/Gene_WoundHealingRateSuperfast", false)
                              ?? ContentFinder<Texture2D>.Get("UI/Icons/Genes/Gene_Deathless", false)
                              ?? TexCommand.HoldOpen
                              ?? BaseContent.BadTex;
                }
                return _iconFavor;
            }
        }

        private static Texture2D IconBless
        {
            get
            {
                if (_iconBless == null)
                {
                    _iconBless = ContentFinder<Texture2D>.Get("UI/Icons/Genes/Gene_ExtremePsychicAbility", false)
                              ?? ContentFinder<Texture2D>.Get("UI/Icons/Genes/Gene_PsychicallyHypersensitive", false)
                              ?? TexCommand.HoldOpen
                              ?? BaseContent.BadTex;
                }
                return _iconBless;
            }
        }

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (var g in values) yield return g;

            if (__instance == null) yield break;
            if (__instance.Dead) yield break;
            if (!Gene_Demigodess.HasDemigodessGene(__instance)) yield break;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) yield break;
            if (!KurinDemigodessMod.Settings.divineFavorEnabled) yield break;

            var favor = Current.Game?.GetComponent<GameComponent_DivineFavor>();
            if (favor == null) yield break;

            // ===== Gizmo 1: Divine Favor readout + heal-all blessing =====
            var healCmd = new Command_Action
            {
                defaultLabel = string.Format("Favor: {0}/{1}", favor.Current, favor.MaxCapacity),
                defaultDesc = string.Format(
                    "Aethira's accumulated divine favor.\n\nClick to invoke Divine Blessing. Every colonist on this map is fully restored, as if each had just stepped out of Aethira's own ascension:\n\n" +
                    "- All injuries, missing parts, and blood loss healed\n" +
                    "- All diseases cured (flu, plague, infection, etc.)\n" +
                    "- All status effects removed (temperature, toxic, addiction, malnutrition, catatonic)\n" +
                    "- Food, rest, joy, mood, and all other needs maxed\n\n" +
                    "Cost: {0} favor (the full bar).",
                    favor.BlessingCost),
                icon = IconFavor,
                action = delegate
                {
                    var map = __instance.Map;
                    if (map == null) return;
                    if (!favor.TryInvokeBlessingHeal(map))
                    {
                        Messages.Message(
                            string.Format("Not enough divine favor (need {0}, have {1}).", favor.BlessingCost, favor.Current),
                            MessageTypeDefOf.RejectInput, false);
                    }
                },
            };
            if (favor.Current < favor.BlessingCost)
                healCmd.Disable("Insufficient divine favor");
            yield return healCmd;

            // ===== Gizmo 2: Targeted bless =====
            var pawnForTargeting = __instance;
            var blessCmd = new Command_Action
            {
                defaultLabel = "Bless colonist",
                defaultDesc = string.Format(
                    "Target a colonist and bestow Aethira's blessing upon them. For 1.5 days (3 days if a Divine Shrine is built) they carry Aethira's full aura inside them, wherever they go:\n\n" +
                    "- +15 mood\n" +
                    "- +50% global work speed\n" +
                    "- +45% melee damage, +9 dodge/hit, +5 shooting, +75% learning (via Divine Inspiration)\n" +
                    "- Constant healing, auto-tend, blood-loss and disease reduction\n" +
                    "- Scar removal and limb regrowth over time\n" +
                    "- Half hunger rate\n\n" +
                    "Carried across maps and caravans. Works even during Aethira's ascension.\n\nCost: {0} favor.",
                    BlessTargetedCost),
                icon = IconBless,
                action = delegate
                {
                    if (favor.Current < BlessTargetedCost) return;

                    var tp = new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetBuildings = false,
                        canTargetItems = false,
                        mapObjectTargetsMustBeAutoAttackable = false,
                        validator = delegate (TargetInfo t)
                        {
                            var p = t.Thing as Pawn;
                            return p != null && !p.Dead && p.IsColonist && p != pawnForTargeting;
                        }
                    };

                    Find.Targeter.BeginTargeting(tp, delegate (LocalTargetInfo target)
                    {
                        OnBlessTarget(pawnForTargeting, target, favor);
                    });
                },
            };
            if (favor.Current < BlessTargetedCost)
                blessCmd.Disable("Insufficient divine favor");
            yield return blessCmd;
        }

        private static void OnBlessTarget(Pawn caster, LocalTargetInfo target, GameComponent_DivineFavor favor)
        {
            var p = target.Pawn;
            if (p == null || p.Dead || p == caster || !p.IsColonist) return;

            if (!favor.TrySpend(BlessTargetedCost))
            {
                Messages.Message("Not enough divine favor.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool shrineBoost = WorldComponent_DemigodessTracker.DivineShrineExistsOnAnyHomeMap();

            // Mood memory (+15 mood, normally 1.5 days, doubled to 3 days if shrine exists)
            if (Kurin_DefOf.DG_BlessedByDemigodess != null && p.needs?.mood?.thoughts?.memories != null)
            {
                p.needs.mood.thoughts.memories.TryGainMemory(Kurin_DefOf.DG_BlessedByDemigodess);

                if (shrineBoost)
                {
                    // Trick: a Thought_Memory's "age" ticks up to durationTicks.
                    // Setting age negative effectively extends the remaining duration.
                    // -90000 ticks = +1.5 days, so total = 3 days.
                    var memory = p.needs.mood.thoughts.memories.Memories
                        .FirstOrDefault(m => m.def == Kurin_DefOf.DG_BlessedByDemigodess);
                    if (memory != null)
                    {
                        memory.age = -90000;
                    }
                }
            }

            // Blessing hediff: portable aura (work speed, combat stats, healing, hunger, etc.)
            // Normally lasts 1.5 days, doubled to 3 days if a Divine Shrine exists.
            if (Kurin_DefOf.DG_AethirasBlessing != null && p.health != null)
            {
                // Remove any existing copy so the duration refreshes
                var existing = p.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_AethirasBlessing);
                if (existing != null) p.health.RemoveHediff(existing);
                p.health.AddHediff(Kurin_DefOf.DG_AethirasBlessing);

                if (shrineBoost)
                {
                    var hediff = p.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_AethirasBlessing);
                    var disappearComp = hediff?.TryGetComp<HediffComp_Disappears>();
                    if (disappearComp != null)
                    {
                        disappearComp.ticksToDisappear = 180000; // 3 days
                    }
                }
            }

            string suffix = shrineBoost ? " The shrine's power doubles the blessing's duration." : "";
            Messages.Message(
                string.Format("Aethira blesses {0}. Divine light surrounds them.{1}", p.LabelShort, suffix),
                p, MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
