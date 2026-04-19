using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Compile-time-safe references to all custom Demigodess defs.
    /// RimWorld's [DefOf] attribute auto-populates these fields by matching
    /// field names to defNames via reflection during def loading.
    /// Field names MUST match the XML defName exactly.
    /// </summary>
    [DefOf]
    public static class Kurin_DefOf
    {
        // ===== HediffDef =====
        public static HediffDef DG_DemigodessPresence;
        public static HediffDef DG_DivineInspirationHediff;
        public static HediffDef DG_IntimidatedHediff;
        public static HediffDef DG_DivineResurrecting;
        public static HediffDef DG_DivineRegenerating;
        public static HediffDef DG_DivineRecoveryComa;
        public static HediffDef DG_CorpsePreservation;
        public static HediffDef DG_AethirasBlessing;

        // ===== ThingDef =====
        public static ThingDef DG_DivineShrine;

        // ===== ThoughtDef =====
        public static ThoughtDef DG_DemigodessCalmedMe;
        public static ThoughtDef DG_DemigodessReturns;
        public static ThoughtDef DG_DemigodessHasFallen;
        public static ThoughtDef DG_GuidedByDemigodess;
        public static ThoughtDef DG_BlessedByDemigodess;
        public static ThoughtDef DG_DayOfRemembrance;
        public static ThoughtDef DG_AethiraAscending;

        // ===== GeneDef =====
        public static GeneDef DG_HairSnowWhite;
        public static GeneDef DG_DivineConstitution;
        public static GeneDef DG_DivineVitality;
        public static GeneDef DG_DivineGrace;
        public static GeneDef DG_DivinePresence;

        // ===== TraitDef =====
        public static TraitDef DG_DemigodessBeauty;
        public static TraitDef DG_EternalWisdom;

        // ===== BackstoryDef =====
        public static BackstoryDef DG_BackstoryChild_DivineFoxKit;
        public static BackstoryDef DG_BackstoryAdult_KurinDemigodess;

        // ===== PawnKindDef =====
        public static PawnKindDef DG_KurinDemigodess_Kind;

        static Kurin_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Kurin_DefOf));
        }
    }
}
