# Implementation Plan: Ganga ki Bagiya

This plan uses the [Game Design Document](./Ganga_ki_Bagiya_GDD.md), [Software Requirements Specification](./Ganga_ki_Bagiya_SRS.md), and [Technical Design Document](./Ganga_ki_Bagiya_TDD.md) as its baselines. The supplied standalone HTML is the playable browser prototype and behavior reference; the Unity repository is a greenfield implementation. The first release targets Android and includes account-based progression sync through Google Cloud Run. iOS and Web are follow-on targets. Audio playback remains undecided; the first release should include audio event hooks but no sound assets or playback.

Existing art is organised under `Assets/background/`, `Assets/obstacles/` and `Assets/water/` (see "Asset organisation" in the GDD).

## Phase 0: Audit and lock the rules

**Status: Complete (12 October 2026).** The audit, selected first-release defaults, and daily-cycle/save acceptance baseline are recorded in [Phase 0 Audit and Decision Log](./Phase_0_Audit_and_Decisions.md). The defaults are planning baselines and may be revised before backend deployment or release.

**Purpose:** Audit the browser prototype, establish the Unity implementation baseline, and resolve remaining rules before building dependent systems.

**Work**
- Inventory the supplied HTML prototype's working behavior, demo-only controls, assets, and known gaps against the GDD/SRS.
- Inventory the Unity project's scenes, packages, art, and code; treat Unity gameplay as a new implementation and the HTML as a reference, not production runtime code.
- Record behavior that must be corrected while porting, including retained-water and bonus handling after a hit once the goal is secured.
- Agree acceptance criteria for the daily cycle and saved progress; see the Phase 0 decision log.
- Preserve settled rules: missed daily rewards are forfeited and do not shift; every 30 retained water units above the goal earns one bonus growth point.
- Set first-release baselines for timezone and midnight handling, the repeating reward-cycle/claim window, stage carry and final-stage behavior, Firebase/Google guest-link onboarding, Firestore behind Cloud Run, same-day sync conflicts, and local save recovery.

**Exit criteria (met):** Verified HTML-prototype/Unity feature inventory, prioritized porting backlog, identified prototype scoring correction, and documented daily/save acceptance criteria and first-release baselines for progression, account access, and sync conflicts. Implement against the decision log; re-open a baseline if product direction changes rather than silently assuming otherwise.

## Phase 1: Build a playable core-game vertical slice

**Status (12 October 2026): Core implementation and APK build complete.** The Unity `SampleScene` contains the playable three-lane run, touch/pointer controls, fixed-step rules, results HUD, and audio-event hooks. All supplied PNG art is referenced by the scene setup. The seven EditMode gameplay-rule tests pass. The APK is at `Builds/Android/GangaKiBagiya-Phase1.apk` and passed package/signature verification. On-device acceptance remains pending; this test APK uses Unity's Android Debug certificate and the placeholder package ID, so production signing and package identity are still required before store distribution.

**Purpose:** Make one complete river run playable before adding long-term systems.

**Work**
- Implement the three-lane river and touch, slide, and release controls.
- Add water collection, the pot meter, lane-specific speeds, obstacle movement, and collision detection.
- Implement the daily water goal, pre-goal failure, secured-goal hit, and "take the pot" flow.
- Put speeds, rates, obstacle gaps, goals, and difficulty multipliers in one tuning configuration.
- Use the existing river, obstacle and background art, with placeholders where needed, so the game loop can be tested early.
- Port the HTML prototype's interaction and visual intent into Unity; do not copy its in-memory demo state or non-deterministic scheduling as production architecture.
- Verify result math explicitly: a pre-goal hit retains zero, a post-goal hit retains exactly the goal, and banking retains all water. Bonus growth is calculated from retained water only.

**Exit criteria:** On Android, a player can start, collect water, avoid hazards, fail or secure the goal, bank the pot, and see accurate retained/spilled amounts without progression exploits.

## Phase 2: Implement plant growth and daily care

**Purpose:** Connect successful runs to the garden and make the day-to-day rules reliable.

**Status (12 October 2026): Core progression implemented.** The six-stage mango progression, configurable 2/2/2/3/3 GP thresholds, health recovery/wilting, one successful watering per day, retries, and missed-day reconciliation are wired into the Phase 1 loop and HUD. All 14 EditMode tests pass. The updated Android test APK is `Builds/Android/GangaKiBagiya-Phase2.apk`. Plant state is still session-only and uses the device's local clock; durable saves and canonical account timezone handling are Phase 3. Final plant-stage artwork is Phase 7. The APK uses a debug signing certificate and placeholder package ID; device acceptance is still pending.

**Work**
- Implement the six mango stages, their growth-point thresholds, and stage transitions.
- Add health levels, missed-day wilting, and recovery from watering.
- Allow unlimited retries; a failed run must not use the daily watering opportunity or harm the plant.
- Apply growth and health changes only once, after a successful daily watering.
- Show current stage, growth progress, health, and whether watering is available today.

**Exit criteria:** Failed runs have no progression penalty; a successful watering applies the correct growth and health effects exactly once; missed days reduce health but preserve growth.

## Phase 3: Add local persistence, daily reset, and rewards

**Purpose:** Make progression survive closing the app, date changes, and interruptions.

**Work**
- Save plant state, daily status, local date, reward state, and active-run state on device.
- Implement the local-calendar-day reset and process one or multiple missed days consistently on reopen.
- Add the fixed seven-day reward calendar with upcoming, available, claimed, and forfeited states.
- Make watering and reward claims idempotent so loading a save cannot grant them again.
- Validate save data and recover safely from invalid or incomplete saves.
- Implement active-run snapshot and restore, including obstacle positions, collected water, timer, difficulty, goal state, and whether banking is available.
- On resume, require a fresh touch if restoring the previous touch state would be unsafe.

**Exit criteria:** Relaunching cannot duplicate watering, growth, or rewards; the run resumes without real-world elapsed time advancing gameplay; calendar and missed-day behavior matches the decisions from Phase 0.

**Status (12 October 2026): Complete.** Local save, daily reset, reward calendar and run snapshot/restore are implemented; 20 EditMode tests passed. On-device suspend/resume is untested.

## Phase 4: Implement account sync through Google Cloud Run

**Purpose:** Ship cross-device account progression synchronization in the first release while preserving local-first play.

**Work**
- Implement the selected Firebase Authentication/Google guest-link onboarding and Firestore Native datastore behind a stateless Cloud Run HTTPS API; define remaining recovery, deployment-region, monitoring and cost details before deployment.
- Define and implement the authenticated HTTPS sync API and versioned event contract.
- Add a durable local sync outbox, unique event IDs, bounded retries, and reconnect handling.
- Validate event ownership, one-watering-per-day rules, reward uniqueness, and progression invariants on the server.
- Implement server revisions, duplicate-event idempotency, multi-device conflict responses, and client reconciliation.
- Keep gameplay and local save/recovery available when offline or when Cloud Run is unavailable; never wait on network calls in the simulation loop.
- Configure service identity, secret handling, logging/monitoring, deployment environments, and account-data retention/deletion requirements.

**Exit criteria:** Android account progress syncs through Cloud Run; offline progress queues and reconciles after reconnect; duplicate or conflicting submissions cannot grant watering, growth, health, or rewards twice; gameplay remains responsive during backend failure.

## Phase 5: Complete the player-facing screens and localization

**Purpose:** Make the full loop understandable and usable for the intended audience.

**Work**
- Complete the home/ready screen, gameplay HUD, result card, watering sequence, daily-done card, and reward calendar.
- Add clear instructions for touch, lane switching, goal security, and banking versus continuing.
- Add Hindi-first and English-toggle text throughout the game.
- Verify Devanagari font rendering and layout on target Android screen sizes in portrait and landscape.
- Make daily status and wilting visible without making missed days feel punitive.

**Exit criteria:** Every screen communicates what the player can do and what happened; Hindi and English layouts work in portrait and landscape on supported Android devices.

**Status (12 October 2026): Built early, awaiting review.** UI Toolkit screens (home, HUD, result, watering, done, rewards) and Hindi/English localization are coded; 25 EditMode tests pass. Hindi shaping and layout are not yet checked on a device; hit messages are generic, watering art is placeholder and reward contents are TBD.

## Phase 6: Produce and integrate the game's art

**Purpose:** Replace placeholders with the paper-cut, stop-motion visual identity.

**Work**
- Create the missing art: skyline (factory, temple, ghat, mountain), matka, mango stages, visitors, and UI.
- Rename misspelled asset files (`clod back 2`, `dolphine`) and update references.
- Add stepped animation, paper texture, cut edges, shadows, and the specified palette.
- Add named audio events/hooks for collection, obstacle hit, goal secured, banking, watering, and growth so sound can be connected later.
- Keep audio playback disabled and add no sound assets in this release; revisit whether to ship audio after a separate product decision.
- Keep assets and animation costs appropriate for low-end Android devices.

**Exit criteria:** The MVP visual set is integrated, readable on target Android screens, and performant. Audio hooks exist, with no audio content or playback enabled.

## Phase 7: Finish educational and obstacle content

**Purpose:** Deliver the learning layer while preserving readability and cultural respect.

**Work**
- Add the initial obstacle set, hit messages, and short facts.
- Decide how the unmapped art (`croc`, `dirt`, `fish 1`, `fish 2`, `flower`) is used or deferred.
- Add plant facts for successful waterings.
- Verify every educational fact with an authoritative reviewer before release.
- Test warning cues and lane-changing behavior for any non-drifting obstacle behaviors included in the MVP.
- Keep additional obstacles and rarer behaviors behind scope control rather than delaying the core release.

**Exit criteria:** Facts are approved, messages are localized, and obstacle behaviors are fair and understandable.

## Phase 8: Test, tune, and prepare the Android first release

**Purpose:** Verify the complete game on real devices and close release risks.

**Work**
- Test the full flow: run, result, watering, daily status, next local day.
- Cover edge cases such as multiple missed days, midnight transitions, app suspension, invalid saves, and duplicate reward attempts.
- Test authenticated Cloud Run sync, offline queue/reconnect, duplicate events, account recovery, and same-day cross-device conflicts.
- Test touch controls, portrait/landscape layouts, Hindi/English readability, loading, and performance on agreed low-end Android devices and Android OS versions.
- Tune lane rates, obstacle spacing, run difficulty, and daily goals using playtests.
- Check that the app remains free of ads, purchases, and real-money mechanics; account-based sync is required.
- Keep iOS and Web builds as follow-on platform work, not Android first-release gates.

**Exit criteria:** The Android release candidate meets agreed functional, Cloud Run sync, security, and measurable performance criteria; progression survives tested interruption, offline, and date-change cases.

## Phase 9: Expand after the Android first release

Keep these out of the first release unless they become explicit requirements:

- Additional tree species such as neem.
- iOS and Web platform releases, plus more obstacle types and advanced obstacle behaviors.
- A river that becomes cleaner as the garden grows.
- Regional rivers and state-specific content.

## Recommended milestone

Treat Phases 0-4 as the core implementation milestone: a playable Unity game with persistent local progression and first-release Cloud Run account sync. Phases 5-8 complete the Android first release; iOS, Web, and optional audio playback follow separate decisions and delivery milestones.
