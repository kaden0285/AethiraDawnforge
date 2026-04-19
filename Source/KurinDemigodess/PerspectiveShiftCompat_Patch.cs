using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Optional compatibility patch for Perspective Shift (workshop ID 3686618980).
    /// Perspective Shift lets the player control a single pawn like a top-down RPG.
    /// In Authentic mode, when the controlled pawn dies, it pops a "You Died" dialog
    /// that forces the player to pick a new avatar or end the run. That is incompatible
    /// with Aethira, who always comes back.
    ///
    /// This patch intercepts Perspective Shift's RevokeControl method. If the dying
    /// pawn is Aethira and she was the current avatar, it:
    ///   1. Saves the current playstyle mode
    ///   2. Forces Director mode
    ///   3. Calls ClearAvatar
    ///   4. Skips the original (suppresses the You Died dialog)
    ///
    /// When Aethira returns alive and spawned, the periodic failsafe scan calls
    /// TryRestoreAvatar which re-acquires her as the avatar and restores the saved mode.
    ///
    /// All Perspective Shift types are looked up via reflection. If Perspective Shift
    /// is not installed, the patch is a silent no-op.
    /// </summary>
    public static class PerspectiveShiftCompat_Patch
    {
        private static bool available;
        private static Type stateType;
        private static Type modeType;
        private static FieldInfo currentModeField;
        private static FieldInfo avatarField;
        private static FieldInfo avatarPawnField;
        private static MethodInfo setAvatarMethod;
        private static MethodInfo clearAvatarMethod;
        private static MethodInfo revokeControlMethod;
        private static object directorModeValue;

        // Bookkeeping between death and return.
        private static bool hadAvatarControl;
        private static object savedMode;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                stateType = AccessTools.TypeByName("PerspectiveShift.State");
                if (stateType == null)
                {
                    // Perspective Shift not installed - silent no-op.
                    return;
                }

                modeType = AccessTools.TypeByName("PerspectiveShift.PlaystyleMode");
                var avatarType = AccessTools.TypeByName("PerspectiveShift.Avatar");

                if (modeType == null || avatarType == null)
                {
                    Log.Warning("[KurinDemigodess] Perspective Shift compat: type lookup failed (PlaystyleMode or Avatar).");
                    return;
                }

                currentModeField = AccessTools.Field(stateType, "CurrentMode");
                avatarField = AccessTools.Field(stateType, "Avatar");
                avatarPawnField = AccessTools.Field(avatarType, "pawn");
                setAvatarMethod = AccessTools.Method(stateType, "SetAvatar", new[] { typeof(Pawn), typeof(bool) });
                clearAvatarMethod = AccessTools.Method(stateType, "ClearAvatar");
                revokeControlMethod = AccessTools.Method(stateType, "RevokeControl");

                if (currentModeField == null || avatarField == null || avatarPawnField == null
                    || setAvatarMethod == null || clearAvatarMethod == null || revokeControlMethod == null)
                {
                    Log.Warning("[KurinDemigodess] Perspective Shift compat: one or more reflection lookups returned null.");
                    return;
                }

                directorModeValue = Enum.Parse(modeType, "Director");

                var prefix = new HarmonyMethod(typeof(PerspectiveShiftCompat_Patch), nameof(RevokeControl_Prefix));
                harmony.Patch(revokeControlMethod, prefix);

                available = true;
                Log.Message("[KurinDemigodess] Perspective Shift compat patch applied. Aethira's deaths route through Director mode.");
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Perspective Shift compat init failed: " + ex.Message);
                available = false;
            }
        }

        /// <summary>
        /// Harmony prefix on PerspectiveShift.State.RevokeControl. If the dying pawn
        /// is Aethira and was the current avatar, saves the mode, switches to Director,
        /// clears the avatar, and suppresses the original (no You Died dialog).
        /// </summary>
        public static bool RevokeControl_Prefix(Pawn pawn)
        {
            if (!available || pawn == null) return true;

            try
            {
                var tracker = Find.World?.GetComponent<WorldComponent_DemigodessTracker>();
                if (tracker == null) return true;

                var aethira = tracker.SavedDemigodess;
                if (aethira == null || pawn != aethira) return true;

                // Only intervene if she was actually the avatar.
                var avatar = avatarField.GetValue(null);
                if (avatar == null) return true;
                var avatarPawn = avatarPawnField.GetValue(avatar) as Pawn;
                if (avatarPawn != pawn) return true;

                // Remember the current mode so we can restore it when she returns.
                savedMode = currentModeField.GetValue(null);
                hadAvatarControl = true;

                // Force Director mode BEFORE ClearAvatar, so no death dialog path can fire.
                currentModeField.SetValue(null, directorModeValue);
                clearAvatarMethod.Invoke(null, null);

                Messages.Message(
                    "Aethira has fallen. She will return. Director mode engaged until her divine form reforms.",
                    MessageTypeDefOf.NeutralEvent);

                return false; // skip original, suppresses Dialog_YouDied
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] RevokeControl prefix error: " + ex.Message);
                // On failure, reset bookkeeping and let the original run.
                hadAvatarControl = false;
                savedMode = null;
                return true;
            }
        }

        /// <summary>
        /// Called from WorldComponent_DemigodessTracker.ScanAndMaintain once per scan
        /// with the first living spawned Aethira (if any). If our prefix previously
        /// dropped her avatar control, this re-acquires her and restores the saved mode.
        /// Silent no-op if Perspective Shift isn't installed, if we never dropped control,
        /// or if the user is already controlling a different pawn.
        /// </summary>
        public static void TryRestoreAvatar(Pawn aethira)
        {
            if (!available || !hadAvatarControl || aethira == null) return;
            if (aethira.Dead || !aethira.Spawned) return;

            try
            {
                var avatar = avatarField.GetValue(null);
                Pawn avatarPawn = null;
                if (avatar != null)
                {
                    avatarPawn = avatarPawnField.GetValue(avatar) as Pawn;
                }

                // Already controlling her - nothing to do, clear flags.
                if (avatarPawn == aethira)
                {
                    hadAvatarControl = false;
                    savedMode = null;
                    return;
                }

                // User chose a different pawn while she was gone - respect that, bail out.
                if (avatarPawn != null && !avatarPawn.Dead)
                {
                    hadAvatarControl = false;
                    savedMode = null;
                    return;
                }

                // Restore mode first, then acquire avatar.
                if (savedMode != null)
                {
                    currentModeField.SetValue(null, savedMode);
                }
                setAvatarMethod.Invoke(null, new object[] { aethira, false });

                Messages.Message(
                    "Aethira has returned. Divine control restored.",
                    aethira, MessageTypeDefOf.PositiveEvent);

                hadAvatarControl = false;
                savedMode = null;
            }
            catch (Exception ex)
            {
                Log.Warning("[KurinDemigodess] Restore avatar error: " + ex.Message);
                hadAvatarControl = false;
                savedMode = null;
            }
        }
    }
}
