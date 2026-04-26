# Kurin Demigodess - RimWorld Mod

## What this is

A RimWorld mod adding the **Kurin Demigodess** xenotype, centered on **Aethira Dawnforge**, an immortal divine protector created by the AIs of Aolara. Morally just, anti-slavery, pro-charity, watches over her people forever. Ideology: "The Dawnforge Covenant" with a Divine Herald / Sacred Voice moral guide. RimWorld 1.6 only.

## Why this mod is unusual

Aethira is meant to be **permanently indestructible**. Losing her breaks the player's playthrough, she is the entire point of the mod. This is why there are 34 Harmony patches and a seven-layer failsafe chain. When something can silently go wrong, she dies forever.

## Key files

### Mod infrastructure
- `Source/KurinDemigodess/KurinDemigodessSettings.cs` - `KurinDemigodessMod` (Verse.Mod subclass) hosts the settings menu AND applies all Harmony patches in its constructor. Static accessor: `KurinDemigodessMod.Settings.failsafeScanInterval` etc. **Always grep the RimWorld log for `[KurinDemigodess] Harmony patches applied.` first when debugging; if missing, the DLL didn't load.** Expected count: 34 succeeded, 0 failed. (PerspectiveShiftCompat has a silent try/catch and does NOT increment the success counter, so it is not included in the 34.)
- `Source/KurinDemigodess/Mod.cs` - intentionally empty (comment-only). Old `[StaticConstructorOnStartup]` class was removed when Harmony init moved to the Mod subclass.
- `Source/KurinDemigodess/Kurin_DefOf.cs` - `[DefOf]` class with every custom `DG_*` def reference.
- `Source/KurinDemigodess/DemigodessHealing.cs` - static helpers: `PurgeHarmfulStatusEffects`, `PurgeInjuriesAndBloodLoss`, `PurgeResurrectionLeftovers`, `PurgeDiseases`, `RemovePsylink`, `FullPurge`. Single source of truth for all hediff purging.

### Aethira's gene and identity
- `Source/KurinDemigodess/Gene_Demigodess.cs` - staggered enforcement cycles. Every 60 ticks: heal injuries. Normal cycle (`normalEnforceInterval`, default 600): purge status, tend, food, regen, downed-check, one scar. Heavy cycle (`heavyEnforceInterval`, default 7500): identity, genes, traits, passions.
- `Source/KurinDemigodess/AntiGeneTransfer_Patch.cs` - Harmony prefix on `Pawn_GeneTracker.AddGene` that rejects any attempt to add Demigodess genes to pawns other than the registered Aethira. Backed by scan-time cleanup in `WorldComponent_DemigodessTracker.ScanForDemigodess` that strips stray genes from impostor pawns.

### Failsafes and persistence
- `Source/KurinDemigodess/WorldComponent_DemigodessTracker.cs` - unified failsafe scan every `failsafeScanInterval` ticks (default 1200 = 20s). Merged from 4 old loops. Emergency respawn only fires after `failsafeMissThreshold` (default 3) consecutive misses, so total time-to-trigger is 60 in-game seconds. Logs to `<Config>/KurinDemigodess_FailsafeLog.txt`. Drives the Layer 7 disk-backed snapshot. EmergencyRespawn routes through `BeginAscension`, not immediate spawn. Public accessors: `ConsecutiveMissCount`, `SavedDemigodess`, `AscensionCount`, `WorldUuid`, `ForceEmergencyRespawn()`, `DumpState()`, `DivineShrineExistsOnAnyHomeMap()` (static), `MinAscensionTicksRemaining()`, `MaxAllNeeds()` (static).
- `Source/KurinDemigodess/DemigodessSnapshot.cs` - static helper for Layer 7 disk-backed snapshot. Writes `<Config>/KurinDemigodess_PawnBackup_{worldUuid}.txt` with skills, relations, memories, work priorities, policy assignments, player settings, and royal titles. Apparel/equipment/inventory are deliberately NOT saved: gear cannot teleport back if her body dissolved. `LoadAndApply(pawn, uuid, skipIdentityData)` has two modes: full restore for rebuild-from-nothing, and settings-only for corpse resurrection where the pawn instance is preserved but work priorities and policies get reset.

### Aura system
- `Source/KurinDemigodess/HediffComp_CalmingPresence.cs` - 50-tile base radius, scales with favor. Ends mental breaks on nearby colonists.
- `Source/KurinDemigodess/HediffComp_HealingPresence.cs` - 50-tile base radius, scales with favor. Heals injuries (rate scales with favor), reduces hunger/disease/blood loss, applies combat buff hediff, auto-tends, removes scars, regrows parts.
- `Source/KurinDemigodess/HediffComp_IntimidationAura.cs` - 50-tile base radius, scales with favor. Debuffs hostile pawns.
- `Source/KurinDemigodess/HediffComp_BlessedBuff.cs` - portable aura carried by blessed colonists. Ticks on the pawn wearing `DG_AethirasBlessing`, applies all aura effects wherever they are (maps, caravans, other worlds). Active even during Aethira's ascension. Reapplies `DG_DivineInspirationHediff` combat buff every tick.

### Death, ascension, regeneration
- `Source/KurinDemigodess/Hediff_DivineResurrecting.cs` - corpse-resurrection hediff, manually ticked by WorldComponent since dead pawns don't tick. After `ResurrectionUtility.TryResurrect` succeeds, calls `DemigodessSnapshot.LoadAndApply(pawn, worldUuid, skipIdentityData: true)` to restore work priorities and player settings that `TryResurrect` resets. Also calls `MaxAllNeeds(pawn)` so she wakes fully rested and fed.
- `Source/KurinDemigodess/Hediff_DivineRegenerating.cs` - body part regrowth engine with per-part timers.
- `Source/KurinDemigodess/DeathlessDemigodess_Patch.cs` - death hook. On-home-map death applies resurrection hediff. Away-from-home death teleports body home with 7-day coma. Also grants kill favor to the player when hostile pawns die while Aethira is on the map (capped 20 raw kills/day, doubled to 40/day actual with shrine). Triggers `TriggerDeathRecoil` AoE pulse when Aethira dies.

### Gizmos, UI, alerts
- `Source/KurinDemigodess/AethiraGizmos_Patch.cs` - Harmony postfix on `Pawn.GetGizmos` adds two command-bar buttons when Aethira is selected:
  1. "Favor: N/100" button - shows current Divine Favor, costs 100 to invoke Divine Blessing which fully restores every colonist on the map (all injuries, diseases, status effects, maxes needs).
  2. "Bless colonist" button - targeted, 10 favor. Applies `DG_AethirasBlessing` hediff (portable aura) + `DG_BlessedByDemigodess` mood thought. Duration 1.5 days, doubled to 3 if a Divine Shrine is built.
- `Source/KurinDemigodess/Alert_DemigodessAscending.cs` - bottom-right alert that shows a live countdown during her ascension. Mentions shrine halving effect in the tooltip.

### Game components
- `Source/KurinDemigodess/GameComponent_DemigodessNarrative.cs` - sends a one-time lore letter the first time Aethira appears on a home map. Gated by `firstLoadLetterEnabled`.
- `Source/KurinDemigodess/GameComponent_DivineFavor.cs` - tracks Divine Favor (max 100). Daily passive `+5` (doubled to `+10` if average mood >= 50%). With shrine, all favor gain is doubled via the `Add()` method. `TryInvokeBlessingHeal(map)` spends 100 favor to full-heal every colonist. `TryAddKillFavor(1)` used by the kill hook, capped 20 raw kills/day. `GetAuraMultiplier()` (static) returns 1x at 0 favor, 2x at max. Accessors: `Current`, `MaxCapacity`, `BlessingCost`.
- `Source/KurinDemigodess/GameComponent_AethiraEvents.cs` - hosts three event loops: weather purge (~40s, dispels toxic fallout/cold snap/heat wave/volcanic winter/flashstorm/noxious haze/psychic drone on home maps), Aethira's Guidance (daily 33% roll, mood buff on random colonist + `+3` favor), Day of Remembrance (annual, colony-wide mood buff + `+30` favor).

### Lore incidents
- `Source/KurinDemigodess/IncidentWorker_KurinPilgrimage.cs` + `1.6/Defs/IncidentDefs/IncidentDefs_Demigodess.xml` - `DG_KurinPilgrimage` incident. 2-5 Kurin visitors arrive from an allied faction when Aethira is present. Drops silver/herbal/wood offerings. Grants `+20` favor on arrival.
- `Source/KurinDemigodess/GuestFavor_Patch.cs` - Harmony patch on `Pawn.ExitMap` that grants `+2` favor when a friendly humanlike non-player pawn leaves a home map alive.

### Recruitment system (non-scenario starts)
- `RecruitmentMode` enum in `KurinDemigodessSettings.cs`: `Quest` (default) / `AutoSpawn` (legacy) / `Disabled`. Determines what happens when the mod is loaded into a save without a Dawnforge scenario.
- `Source/KurinDemigodess/DawnforgeFactionSeeder.cs` - `EnsureSeeded(tracker)` is idempotent. In Quest mode, generates Aethira and assigns her as the leader of the `Kurin_Faction` (renamed in XML to **The Dawnforge Collective**). Skips if she already exists anywhere or the faction is missing. Called from `WorldComponent_DemigodessTracker.WorldComponentTick` once per session via `seededThisSession` flag.
- `Source/KurinDemigodess/GameComponent_DawnforgeRecruitment.cs` - tracks the 3-step chain. Stages 0 -> 1 -> 2 -> 3 -> summon, gated by Dawnforge Collective goodwill (50 / 80 / 100) plus a Divine Shrine on a home map for stage 3. On completion, Aethira walks out of the wilderness with a "Divine Arrival" letter, transfers from Collective to player faction, ascending leadership of the Collective falls to a successor.
- `WorldComponent_DemigodessTracker.EmergencyRespawn` is gated on `RecruitmentMode`: AutoSpawn -> WelcomeSpawn (legacy), Quest -> seed via `DawnforgeFactionSeeder` and silence the failsafe (player must complete the chain), Disabled -> just silence the failsafe.
- `1.6/Defs/Factions_Kurin.xml` - the `Kurin_Faction` FactionDef was renamed: `<label>` from "Republic of Kurin" to "The Dawnforge Collective", `<leaderTitle>` from "President" to "Dawnforge Speaker", and the description rewritten to reference Aethira. The historical "Republic of Kurin" survives unchanged in the lore (it's the fallen Aolaran government she stood at the founding of).

### Dev tooling
- `Source/KurinDemigodess/DebugActions_Demigodess.cs` - `[DebugAction]` class, category "Kurin Demigodess". Dump state, find Aethira, reset miss counter, force respawn, force kill, trigger ascension, death-recoil pulse, grant favor, invoke blessing, force pilgrimage.

### Harmony patches list
Currently **34 patches** applied in `KurinDemigodessMod.ApplyHarmonyPatches` (31 TryPatch calls + 3 manual Apply calls that increment `success`): DamageCap, Deathless, AntiKidnap, AntiMentalBreak, AntiMentalBreaker, AntiFactionChange, AntiSell, CertaintyLock, AntiPsylink, 6x CorpseProtection (Destroy/Damage/DeSpawn/Prey/Food/Haul), RoofCollapse, MapProtection, WorldPawnGC, WorldPawnsPass, WorldPawnsRemove, PawnDiscard, CaravanCapacity, AntiPsycast_Helper, AethiraGizmos, GuestFavor, AntiGeneTransfer, AntiWeaponDrop, KurinNoBeard, 3x InventoryPreservation (Rot_CompTick, Rot_CompTickRare, Deterioration), plus manual TerrainSpeedImmunity, DiseaseImmunity, and AntiBanish patches. `PerspectiveShiftCompat_Patch` also runs but is optional compat that silently no-ops when Perspective Shift isn't installed, and does NOT increment the success counter.

## Lessons learned (do not relearn)

- **The C# DLL silently failed to load once, taking down every protection.** A predator ate her corpse with zero resistance. That's why Harmony init uses per-patch try-catch now, one broken patch cannot kill the whole mod.
- **Followers should not grieve when she dies.** They believe she will return. Mood thoughts use "vulnerable/wary" (smaller, situational hit), not grief.
- **`savedDemigodess` Scribe_References backup** was added after she got eaten. Refreshed every scan, used by emergency respawn.

## Design principles (DO NOT break these)

- **Regeneration is her ONE power.** She must not be stronger in combat than a normal pawn without her regen. No flat damage reduction, no armor statOffsets, no `MeleeDamageFactor` buffs on her own genes. Auras that buff allies are fine (she's the source of those). Auras that buff her are not.
- **`PsychicSensitivity +0.5` on `DG_DivinePresence` is INTENTIONAL, not a bug.** She is the antithesis of a psycaster, cannot become one, and is more vulnerable to enemy psycasts. This is the explicit balance tradeoff for her aural powers. Psycasts are her counter. DO NOT "fix" the `+0.5` in a future session thinking it's a typo. If it ever reads `-0.5` or `0.0`, restore it to `+0.5`.
- **Healing aura base is `0.05`, scaling to `0.10` at max Divine Favor (max 100).** Don't raise without user sign-off.
- **Max favor is 100, heal-all blessing costs 100 (full bar), bless-colonist costs 10.** Don't rebalance without sign-off.
- **All aura base radius is 50 tiles**, scaling to 100 tiles at max favor via `GameComponent_DivineFavor.GetAuraMultiplier()`. All four auras (leadership/healing/calming/intimidation) share the same scaled radius.
- **Failsafe time-to-trigger is 60 seconds in-game** (`failsafeScanInterval = 1200` x `failsafeMissThreshold = 3`). The old 12-second window was prone to false positives from transient map-transition states. Don't lower without sign-off.
- **EmergencyRespawn routes through BeginAscension, NOT immediate spawn.** If the failsafe catches her, she goes through the full 7-day ascension recovery. No more "teleport back instantly." Narrative-intentional: if the failsafe had to catch her, her situation warrants the full divine recovery ritual.
- **Shrine effects (all three check `DivineShrineExistsOnAnyHomeMap()`):** halves ascension duration, doubles bless colonist duration, doubles all Divine Favor gain. The shrine is an expensive 1500-gold sink that justifies its impact.
- **Demigodess genes cannot be transferred.** `AntiGeneTransfer_Patch` blocks `Pawn_GeneTracker.AddGene` for any pawn that isn't the registered Aethira. Backed by scan-time cleanup that strips stray genes from impostors. DO NOT remove this; if you do, Gene Siphon / Gene Assembler / dev-mode copies will break the one-Aethira invariant.
- **The auto-leader hook is gated on the colony's ideology actually having the `DG_DemigodessWorship` meme.** `MapComponent_DemigodessLeader` no longer seizes the leader role of unrelated player ideologies (Cannibals, Tribal Hunters, etc.). Don't loosen this without sign-off; it caused user complaints.
- **Quest is the default Recruitment Mode for non-scenario starts.** Non-scenario players reach Aethira via the goodwill chain (50 / 80 / 100 + shrine). The legacy AutoSpawn path is opt-in. Disabled is also opt-in. Don't flip the default back without sign-off.
- **Recruitment goodwill thresholds are 50 / 80 / 100.** Tuned to encourage real faction-relationship investment via gifts and trade caravans, not to be skippable in 10 minutes. Don't lower without sign-off. The shrine requirement at stage 3 is the "buy-in" - it makes the recruitment commitment material, not just political.
- **The Dawnforge Collective is the renamed `Kurin_Faction`, not a new def.** Don't add a new FactionDef for it; the rename is a label/title/description edit on the existing def in `1.6/Defs/Factions_Kurin.xml`. The historical "Republic of Kurin" lore name is preserved (it's the fallen Aolaran government she co-founded).
- **Gear does NOT teleport with her when she rebuilds from nothing.** Both `RespawnDemigodess` (ascension return) and `EmergencyRespawn` fallback branch (fresh from PawnKindDef) strip apparel, equipment, and inventory. Only her mind-state (skills, memories, relations, work priorities, policies, royal titles) persists via the snapshot. Narrative-intentional.
- **On respawn/resurrection, all her needs are maxed** via `MaxAllNeeds(pawn)`. She never comes back starving or exhausted.
- **No active abilities.** The user explicitly declined adding targeted active abilities. Everything is passive aura + gizmo-based. DO NOT add AbilityDefs without sign-off.
- **Movement speed bonus is removed.** She moves at vanilla Kurin base speed. Do not add `MoveSpeed` offsets to her genes.
- **Damage cap is 25% of max HP per hit** (was 40%). Set in `DamageCap_Patch.MaxDamagePercent`. Don't change without sign-off.
- **Carrying capacity +500 kg on-pawn, +1000 kg effective in caravan.** The `CarryingCapacity` statOffset on `DG_DivineGrace` adds +500 kg to her hauling limit. This also flows through `MassUtility.Capacity` for caravan math, and `CaravanCapacity_Patch` postfix adds another +500 kg on top when she's in a caravan. They STACK, so caravan capacity is effectively +1000 kg. Do not describe this as "500 kg" in user-facing text; the total in-caravan is 1000. Was briefly raised to 1500/1500 then reverted.
- **Nothing rots or deteriorates in Aethira's inventory.** `InventoryPreservation_Patch` prefixes `CompRottable.CompTick`/`CompTickRare` and postfixes `SteadyEnvironmentEffects.FinalDeteriorationRate`. Walks `ParentHolder` up to 8 levels to detect Demigodess ownership (covers inventory, equipment, apparel, nested holders).

## Collaboration style

- Direct and terse. No restating what was just asked.
- Don't suggest restarts, the save is precious.
- Don't add scope creep or over-engineer. Fix the thing asked.
- Don't write comments or docstrings for code that wasn't changed.
- **Never use em dashes** in user-facing text (in-game descriptions, letters, labels, tooltips) or in code comments. Use regular hyphens, commas, or periods. This is a user preference, enforced across the entire codebase.
- Build flow: `dotnet build` in `Source/KurinDemigodess/`, then copy `Assemblies/KurinDemigodess.dll` to `1.6/Assemblies/KurinDemigodess.dll`. DLL file is locked while RimWorld is running; user must close the game before deploying.
- Version support: RimWorld 1.6 only. Old version folders (1.1-1.5) have been removed.
