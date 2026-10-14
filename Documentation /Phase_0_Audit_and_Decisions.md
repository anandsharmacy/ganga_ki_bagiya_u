# Phase 0 — Audit and Decision Log

**Project:** गंगा की बगिया (Ganga ki Bagiya)  
**Status:** Phase 0 complete — baseline defaults selected 12 October 2026; revise before implementation if desired  
**Purpose:** Record the prototype and Unity baseline, porting corrections, and selected first-release rules for save, reward, and sync implementation.

## Confirmed direction

- Android is the first-release platform; iOS and Web are follow-on targets.
- The first release includes account-based progression synchronization through a Google Cloud Run backend.
- Audio playback is undecided. Build named/no-op-capable audio event hooks only; add no sound assets and enable no playback for now.
- A new profile starts with a Stage 1 mango, 0 growth points, and health 3; its first eligible game day anchors the reward cycle.
- The design baseline gives unlimited failed-run retries, one successful watering per local day, missed-day health loss without growth loss, and a fixed seven-day reward calendar where missed rewards are forfeited rather than shifted.
- A successful bank earns one base growth point plus one bonus point for each 30 retained water units above the daily goal. A post-goal hit retains exactly the goal and spills the surplus.

## Prototype audit

The standalone HTML in Downloads is a browser behavior and visual reference, not production runtime code. It contains a single in-memory state object, inline SVG scene art, a requestAnimationFrame simulation rendered at stepped intervals, `Math.random()` obstacle selection, and timer-driven watering animation.

| Area | Prototype behavior / finding | Porting implication |
|---|---|---|
| Controls | Pointer-down holds a lane; pointer movement changes lanes; pointer release/cancel stops collection. Keyboard lane controls are also present. | Preserve hold/slide/release semantics; production Android input is touch-first. |
| River tuning | Fast/medium/slow rates are 7.5/4.2/2.2 units per second; obstacle speeds are 150/100/62 reference pixels per second. Difficulty ramps with simulated run time and plant stage. | Keep values configurable and convert pixel speeds to resolution-independent units. |
| Goal and outcomes | Goal is `24 + 8 × (stage − 1)`. Pre-goal hit leaves watering incomplete; reaching goal enables banking; a hit after the goal remains watering-eligible. | Implement explicit retained/spilled-water result values, not just the total collected display. |
| **Known scoring defect** | After a secured-goal hit, the watering flow calls `grow()` using total collected water. It therefore calculates bonus GP from water that should have spilled. Example: Stage 1, hit at 54; prototype can award a bonus from 30 surplus even though only 24 should be retained. | Fix in Unity: calculate GP from retained water; post-goal hit must yield 0 bonus, while a bank may yield bonus GP. Add a regression test. |
| Growth | Thresholds are 2/2/2/3/3 for the first five stage transitions. A `while` loop carries surplus points through multiple stages; Stage 6 has no next-stage transition, so additional points can accumulate without a defined use. | Preserve intermediate carry-over; cap growth at Stage 6 per the selected baseline. |
| Daily progression | Demo buttons manually advance a day, skip one day, or force the next stage. Skipping reduces health by one. There is no real date/timezone calculation. | Replace demo controls with a calendar-day model and idempotent reconciliation. |
| Persistence/rewards/sync | State resets on page reload. No save/recovery, reward calendar, authentication, account, backend, or sync behavior exists. | These are production systems, not prototype behavior to copy. |
| Determinism | Obstacle type and placement use `Math.random()`; timers are used for watering transitions. | Use a versioned deterministic simulation and serialized run state for production recovery. |
| Content | Hindi and English text and six encounter types are present (bag, foam, boat, dolphin, diya, lotus). The HTML draws its scene using inline SVG. | Review content against the approved roster and source-check facts before release; map visual assets separately. |

## Unity project baseline

- Unity editor version recorded by the project: `6000.6.4f1`.
- The project is a 2D URP starter, with Input System, UGUI, and Unity Test Framework packages declared.
- The build scene list contains only `Assets/Scenes/SampleScene.unity`; the sample scene has a main camera and 2D global light, not game flow or gameplay objects.
- The only C# source under `Assets/` is the Unity 2D Welcome template script; no game logic, gameplay prefabs, or project test files are present. The code graph reports that script only; its `Assets` coverage report lists image assets as intentionally not indexed, so image inventory was checked directly from disk.
- Existing visual assets include river backgrounds, clouds/sun, and obstacle art (including plastic, pollution, boat, dolphin, fish, diya, flower, dirt, and crocodile). Mango stages, matka, garden/plant states, visitors, skyline, and production UI are not yet represented as Unity gameplay assets.
- Android settings show minimum SDK 26, target SDK left at Unity's default (`0` in project settings), and no Android-specific application identifier recorded. Signing, target API, package ID, and supported-device matrix still need release configuration.
- Unity build/editor execution was not part of this document-only audit pass; no gameplay build or runtime verification is claimed.

## Initial porting backlog

1. **Before dependent systems:** implement the selected rules below and turn the daily/save baselines into acceptance tests.
2. **Core game:** create a playable Android Unity river run with touch lane switching, tuning configuration, obstacles, collision, goal-secured and banked outcomes; test all retained/spilled/bonus arithmetic, especially the prototype defect above.
3. **Progression and persistence:** implement mango stages and health, calendar-day reconciliation, idempotent watering/reward claims, and interruption recovery against approved rules.
4. **Cloud sync:** implement the selected Firebase/Google onboarding and Firestore-backed Cloud Run API/outbox with authoritative day keys and same-day conflict resolution.
5. **Release readiness:** configure Android identity/signing/API targets and agree device/performance acceptance; later complete localization, missing art, facts, accessibility, and device testing.

## Phase 0 baseline decisions

These defaults were selected to complete Phase 0 and unblock planning. They are revisable before implementation reaches backend deployment or release.

| Decision | Phase 0 baseline selected | Status |
|---|---|---|
| Daily timezone and clock changes | Store a canonical IANA timezone on the profile, initially captured from the device. Use server time when online; offline day changes are provisional and anchored to the last verified server time. Explicit timezone changes take effect at the next day boundary and never rewrite past day keys. | Selected |
| Run crossing midnight | Bind each run to its start-day key. A run active at midnight may resolve until 00:15 in the profile timezone; no new run for the old date may start after midnight. A new-day run waits if the previous run is still active. At 00:15, abandon an unresolved run and reconcile that date as missed if it was not watered. | Selected |
| Seven-day reward calendar | Anchor on the profile's first eligible game day and repeat every seven calendar days. A reward becomes claimable after successful watering and can be claimed through 00:15 after its date; unclaimed/missed rewards are then forfeited without shifting. Reward item definitions remain content work. | Selected |
| Growth-point carry and Stage 6 | Carry surplus GP across intermediate thresholds, including multiple stages in one watering. Stage 6 is the cap: later successful waterings still restore health and may claim the daily reward but add no GP until a new species is approved. | Selected |
| Identity and onboarding | Firebase Authentication with Google sign-in. Allow local guest play; link the guest profile on sign-in so existing local progress is retained. Cloud sync requires a linked account; offline play does not. | Selected |
| Cloud Run datastore | Firestore Native mode, accessed only by the Cloud Run service using its service identity. Store canonical profile state/revision and immutable progression events. | Selected |
| Same-day sync conflict | A transaction accepts at most one valid watering per account/day; the first valid event committed by the server wins, retries are no-ops. Active runs stay device-local. A losing offline completion remains in local conflict history, is not credited, and is clearly explained; canonical progress is reconciled from the server. | Selected |
| Save/recovery acceptance | Use a versioned local snapshot plus event/outbox journal and last-good backup. Checkpoint at least every 2 simulated seconds, on critical state transitions, and before background/pause; writes are atomic. Resume after normal suspension at the same logical tick with fresh touch required. Forced termination restores the latest complete checkpoint (normally no more than 2 simulated seconds behind when writes succeed); never claim unsaved per-frame recovery. | Selected |
| Same-tick goal/collision order | Process input and obstacle motion, then collision, then water increment/goal check, then bank. A collision in that tick wins over reaching the goal in that same tick. | Selected |
| Initial profile state | Stage 1 mango, 0 GP, health 3. The profile-creation game day anchors the seven-day calendar. | Selected |

## Daily-cycle and saved-progress acceptance baseline

- One successful watering and one reward claim are allowed per canonical game-day key; retries and duplicate callbacks cannot apply them twice.
- Each elapsed, unwatered date applies exactly one health loss (clamped at zero) and forfeits that date's reward by 00:15 after its midnight boundary; GP is preserved. Watering restores two health up to three.
- An active run is assigned to its start day. The 15-minute rollover grace is measured against canonical/profile time, not simulation time; a suspended run does not advance. No new attempt for an old date may start after midnight. An unresolved run expires at the grace deadline and cannot award progress.
- The reward slot advances by calendar date even when missed; after Day 7, the cycle returns to Day 1. The reward claim window closes at 00:15 after its eligible date. Exact reward contents and boost values remain outside Phase 0.
- Reopening after suspension must not advance simulation time. On graceful background/pause, restore the same committed logical tick and require a fresh touch. On forced termination, restore the latest valid atomic checkpoint; test that successful checkpoints are at most two simulated seconds apart during active play.
- Corrupt saves recover the last-good backup or a safe local state without inventing rewards or discarding confirmed cloud progress. Offline events are queued; server retries are idempotent, and same-day conflicts are surfaced rather than silently merged.
- Test profile-timezone boundaries, an explicit timezone change, manual device-clock drift, DST transitions in a non-India IANA zone, a run reaching its grace deadline, multi-day absence, duplicate watering/reward events, concurrent same-day device events, backend outage/reconnect, and interrupted/corrupt snapshots.

## Exit status

The prototype and Unity baselines are inventoried; the post-goal-hit bonus defect, prioritized porting backlog, progression rules, calendar policy, identity/datastore baseline, conflict policy, and save acceptance criteria are recorded above. Phase 0 is **complete for first-release planning** using these defaults. They are product baselines selected to unblock Phase 1 and remain revisable before backend deployment and release; do not treat the proposed Unity game or Cloud Run service as already implemented.
