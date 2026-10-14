# Software Requirements Specification (SRS)

# गंगा की बगिया (Ganga ki Bagiya)

**Project:** Dainik Bhaskar Game Lab 2026  
**Document version:** 1.3 (Phase 0 baseline)  
**Document date:** 12 October 2026  
**Status:** Phase 0 baseline — Android first release and Cloud Run account sync; remaining release details listed in §11  
**Authors / reviewers:** TBD  
**Basis:** Existing game write-up, updated progression specification, game design document (GDD), and technical design direction

> **Requirement convention:** **MUST** denotes a required behavior for the planned release; **SHOULD** denotes a desirable behavior subject to prioritization; **TBD** denotes an unresolved product or engineering decision. Prototype values are baselines, not locked balance commitments. Planned future features are separated from release requirements.

## 1. Introduction

### 1.1 Purpose
This SRS defines the observable behavior, constraints, data requirements and verification criteria for *Ganga ki Bagiya*, an Android-first, Hindi-first casual river-water collection and plant-care game with adaptive portrait and landscape layouts. It is intended for designers, developers, QA testers and project reviewers.

### 1.2 Scope
The player collects clean water from one of three river lanes by holding and sliding a finger, avoids objects floating downstream, and waters a mango plant once per local calendar day. Successful daily watering advances plant growth; missed days reduce reversible plant health. The game includes short environmental and cultural facts and a fixed seven-day reward calendar.

**In scope:** single-finger river gameplay; hit resolution; daily goal; risk/reward after reaching goal; six mango growth stages; plant health and missed-day recovery; local-date progression; seven-day rewards; Hindi/English UI; local-first persistence; exact-state run suspension/resumption; educational cards; adaptive mobile UI; and first-release account progression synchronization through a Google Cloud Run-hosted backend. Android is the first release target; iOS and Web are follow-on targets.

**Out of scope for the baseline:** ads, in-app purchases, real-money mechanics, multiplayer and server leaderboards. Audio, new tree species, additional obstacle behavior, cleaner river visuals and regional rivers are planned enhancements, not mandatory baseline features unless separately approved.

### 1.3 Product perspective
A local-first Unity game client designed for low-end Android phones. The first release includes account-based progression synchronization through Google Cloud Run. The selected first-release authentication baseline is Firebase Authentication with Google sign-in and guest-to-account linking; Cloud Run accesses Firestore Native mode server-side. Gameplay, local saves and suspended-run recovery MUST remain available when the backend is unavailable. Exact Android support matrix and deployment details remain **TBD**; iOS and Web are follow-on targets.

### 1.4 Target users
Primary audience: Hindi-speaking, mobile-first adults approximately 24–44 years old in Tier 2/3 Indian cities, including non-gamers. English is an alternate language. This is a design target, not an access restriction.

### 1.5 Definitions
| Term | Definition |
|---|---|
| Run | One attempt at collecting water until a hit or the player banks the pot. |
| Daily goal | Water threshold needed to secure that day's watering. |
| Secured goal | Goal reached during a run; the goal quantity survives a subsequent hit. |
| Bank / Take the pot | Player ends the run after reaching goal, preserving all collected water. |
| Growth point (GP) | Unit of plant progression; one earned per completed watering plus eligible bonus. |
| Health | Plant condition level, integer 0–3; distinct from permanent growth. |
| Local day | Calendar date in the canonical IANA timezone stored on the player profile, with boundary at midnight in that timezone. |
| Active-run snapshot | Persisted simulation state sufficient to resume at the saved frame. |
| Forfeited reward | Calendar reward missed and unavailable for later make-up claim. |
| Cloud sync | Background reconciliation of locally recorded progression events with the Google Cloud Run backend. |

## 2. Stakeholders, environment and assumptions

### 2.1 Stakeholders
- **Player:** plays, waters the plant, views facts and rewards.
- **Design/content team:** manages obstacle facts, translations, visuals and balance.
- **Engineering team:** implements gameplay, local storage, recovery, localization, the Cloud Run API and synchronization.
- **QA/reviewers:** verify functional behavior, device compatibility and content accuracy.

### 2.2 Operating environment
- Android is the first-release platform, targeting low-end devices; touch input is required for primary play.
- The UI MUST adapt to portrait and landscape orientations. iOS and Web are follow-on platform targets.
- Exact supported Android versions, screen-size range and distribution format: **TBD**. Local guest play and offline play after installation are supported; account linking is required for cloud sync.
- Persistent game state is stored locally first. When online and authenticated, the client synchronizes progression with an HTTPS API deployed on Google Cloud Run.
- Firebase Authentication with Google sign-in is the selected identity baseline; Firestore Native mode is the selected managed store. Cloud Run uses its service identity to access Firestore. Region, detailed service topology and operational monitoring remain **TBD**.

### 2.3 Constraints and dependencies
- No ads, purchases or real-money incentives.
- The Google Cloud Run backend MUST not be in the real-time gameplay loop; a failed or slow network request MUST NOT pause input, simulation, saving or recovery.
- Backend credentials, service-account keys and privileged database credentials MUST NOT be embedded in the client build.
- No unlicensed copyrighted assets, insensitive portrayals or direct cloning of existing games.
- Environmental/wildlife/cultural fact copy requires source or editorial verification before release.
- Game simulation must not advance while the app is suspended.
- The account's canonical IANA timezone is captured from the device at profile creation; explicit timezone changes take effect at the next day boundary and never rewrite prior day keys. Server time is authoritative online; offline calendar results remain provisional against the last verified server-time anchor.

### 2.4 Phase 0 progression, calendar and sync baseline

- Growth-point surplus carries through intermediate mango-stage thresholds during the same watering. Stage 6 is the first-release cap: later waterings still restore health and may claim a daily reward, but do not accumulate growth points until another species is approved.
- Each run is assigned the canonical game-day key at start. A run active at midnight may resolve until 00:15 in the profile timezone; no new attempt for the old date may start after midnight. A new-day run waits while the previous run remains active. At 00:15 an unresolved run is abandoned and its unwatered date is reconciled as missed.
- A new profile starts with a Stage 1 mango, 0 growth points, and health 3. The profile-creation game day anchors its reward calendar.
- The reward calendar begins on the profile's first eligible game day and repeats every seven calendar days. The day's reward becomes claimable after successful watering and can be claimed through 00:15 after that date; then an unclaimed or missed reward is forfeited without shifting the calendar. Exact reward contents remain content work.
- Local guest play is allowed. Firebase Authentication Google sign-in links the guest profile for cloud sync; account linking preserves existing local progress. Sync uses a durable outbox and Firestore behind the Cloud Run API.
- For a same-account/day conflict, the first valid watering event committed by the server wins; retries are idempotent. Losing offline completions remain in local conflict history, are not credited, and are explained before canonical state is reconciled. Active runs are device-local.
- Persist a versioned local snapshot plus event/outbox journal and last-good backup. Checkpoint at least every 2 simulated seconds, on critical state transitions, and before background/pause. Resume graceful suspension at the same committed logical tick with a fresh touch. After forced termination, recover the latest complete checkpoint; a successful normal checkpoint cadence targets no more than 2 simulated seconds of rewind.
- Within one simulation tick, process input and obstacle movement, then collision, then water/goal, then bank; collision wins a same-tick goal crossing.

## 3. System overview and user journey

1. Player opens Home/Ready and sees the plant's current condition and daily status.
2. If watering is pending, player starts a river run.
3. Touching/holding a lane collects water; sliding changes lanes; releasing stops collection.
4. A hit before the goal spills all collected water and permits unlimited retries.
5. Once the goal is reached, watering is secured; player can bank or continue for bonus water.
6. Banking retains all collected water. A hit after securing the goal retains only the goal amount.
7. Result card shows retained/spilled amounts and a contextual fact where applicable.
8. Successful run leads to watering, a single application of growth/health effects, and the Daily Done screen.
9. At the next profile-timezone midnight, a new game day begins. An already-active previous-day run may resolve until 00:15; the previous date's missed-day/reward state is finalized by then.

## 4. Functional requirements

### 4.1 Navigation and presentation
| ID | Requirement | Priority |
|---|---|---|
| FR-001 | System MUST present a Home/Ready screen with a Play action, concise controls and current plant/day status. | Must |
| FR-002 | System MUST display three horizontal river lanes, collection zone, pot/water meter, current goal, day and plant stage during gameplay. | Must |
| FR-003 | System MUST show a result card after a run ends, including kept water and spilled water; if a collision occurred, show its message and fact. | Must |
| FR-004 | System MUST show a watering animation/sequence after a qualifying run and a Daily Done confirmation afterward. | Must |
| FR-005 | System MUST prevent a second successful watering application on the same local day. | Must |
| FR-006 | System MUST provide Hindi-first text and an English language toggle for supported UI and educational copy. | Must |
| FR-007 | System MUST provide a seven-day reward-calendar view showing available, claimed, upcoming and forfeited states. | Must |

### 4.2 River input and simulation
| ID | Requirement | Priority |
|---|---|---|
| FR-008 | Touch-down on a valid lane MUST begin collecting water from that lane while held. | Must |
| FR-009 | Sliding an active touch into another valid lane MUST switch the collection lane without requiring lift/re-touch. | Must |
| FR-010 | Releasing or canceling the active touch MUST stop water collection. | Must |
| FR-011 | Water MUST accumulate only while a valid lane is actively held and the run is active. | Must |
| FR-012 | Obstacles MUST move left-to-right toward the collection zone according to lane/simulation parameters. | Must |
| FR-013 | A collision MUST be registered only when an obstacle overlaps the collection zone in the currently held lane. Objects in other lanes MUST NOT cause a hit. | Must |
| FR-014 | River speed/difficulty MUST ramp within a run and vary by plant stage according to configurable balance parameters. | Must |
| FR-015 | Lane collection rates, obstacle speeds, spawn gaps, goal formula and difficulty modifiers MUST be centrally configurable rather than scattered across gameplay logic. | Must |

**Prototype tuning baseline (subject to balance testing):**

| Lane | Water rate | Obstacle speed | Spawn spacing |
|---|---:|---:|---|
| Top / fast | 7.5 units/s | 150 px/s | Shortest gaps |
| Middle / medium | 4.2 units/s | 100 px/s | Medium gaps |
| Bottom / slow | 2.2 units/s | 62 px/s | Longest gaps |

The prototype describes up to approximately 90% within-run speed increase over ~80 seconds and approximately 5% additional speed per plant stage. Exact ramp function and clamping: **TBD**.

### 4.3 Water goal, collisions and banking
| ID | Requirement | Priority |
|---|---|---|
| FR-016 | The daily water goal MUST equal `24 + 8 × (currentPlantStage − 1)` units, using the stage at the start of the daily challenge. | Must |
| FR-017 | Before goal completion, a collision MUST end the run, spill all collected water and leave watering incomplete. | Must |
| FR-018 | Before goal completion, player MUST be able to retry without an attempt limit or plant-health penalty. | Must |
| FR-019 | Reaching the goal MUST mark the watering objective as secured for the current run and reveal a **Take the pot** action. | Must |
| FR-020 | After goal completion, a collision MUST end the run, preserve exactly the daily-goal quantity and spill only water above the goal. | Must |
| FR-021 | Selecting **Take the pot** after goal completion MUST end the run safely and retain all water collected at that point. | Must |
| FR-022 | Below Stage 6, the system MUST calculate bonus GP as `floor(max(0, retainedWater − dailyGoal) / 30)` for a successful run; Stage 6 does not accumulate GP. | Must |
| FR-023 | Below Stage 6, a successful watering MUST award one base GP plus eligible bonus GP once for that game day. At Stage 6 it MUST still apply health/reward effects but grant no GP. | Must |
| FR-024 | A run that ended before goal completion MUST NOT grant watering, GP, reward claims or health restoration. | Must |

**Examples:** Stage 1 goal = 24 units. If hit at 40 units, keep 24, spill 16, bonus GP = 0. If bank at 54 units, keep 54, bonus GP = 1.

### 4.4 Plant growth and health
| ID | Requirement | Priority |
|---|---|---|
| FR-025 | The initial plant MUST be a mango tree with six visually distinct stages and stage progression driven by earned GP. | Must |
| FR-026 | GP earned by watering MUST persist and MUST NOT be erased by missed days or reduced health. | Must |
| FR-027 | Each fully missed local day MUST decrease health by one, to a minimum of 0. | Must |
| FR-028 | A successful daily watering MUST restore two health levels, to a maximum of 3. | Must |
| FR-029 | Health 0 MUST display a wilted but living plant; the plant MUST never die permanently. | Must |
| FR-030 | Plant visuals MUST reflect stage and health, including wilting/colour changes and growth-stage visitors where specified. | Must |
| FR-031 | The system MUST apply stage advancement when GP thresholds are reached; thresholds MUST be configurable. | Must |

**Mango growth-stage baseline:**

| Stage | Name | Visual description | GP threshold to advance |
|---|---|---|---:|
| 1 | Seed / बीज | Half-buried seed | 2 |
| 2 | Sprout / अंकुर | Stem, copper-red young leaves | 2 |
| 3 | Seedling / पौधा | Taller green leaves with copper tips | 2 |
| 4 | Sapling / छोटा पेड़ | Small trunk, butterfly | 3 |
| 5 | Blossom / बौर | Flowers and bees | 3 |
| 6 | Fruiting / फल | Mangoes, parrot and squirrel | Final |

**Stage-completion semantics:** GP thresholds are per-stage progression thresholds and surplus GP carries across multiple stage boundaries in one watering. Stage 6 is the final first-release cap; growth points stop accumulating there. New species such as neem are planned and not required for baseline release.

### 4.5 Calendar progression and daily care
| ID | Requirement | Priority |
|---|---|---|
| FR-032 | Daily eligibility MUST be keyed to the player's local calendar date, resetting at local midnight, not 24 hours after the last watering. | Must |
| FR-033 | Each new local day MUST require a fresh collection run to complete that day's watering. | Must |
| FR-034 | The system MUST permit unlimited failed attempts while watering remains pending that day. | Must |
| FR-035 | A missed day MUST be counted only after its date boundary grace closes at 00:15 in the canonical profile timezone without successful watering. No new attempt for the old date may start after midnight. | Must |
| FR-036 | On reopening after multiple missed days, system MUST reconcile all fully elapsed, unwatered local dates, applying one health decrement per missed day, clamped at zero. | Must |
| FR-037 | The system MUST record watering completion with its local date and MUST apply growth and health restoration no more than once per date. | Must |
| FR-038 | A run MUST use its start-day key. It may resolve until 00:15 in the canonical profile timezone; a still-active run then MUST be abandoned and the day reconciled as missed if unwatered. A new day's run MUST wait until the prior run resolves or expires. | Must |

### 4.6 Seven-day reward calendar
| ID | Requirement | Priority |
|---|---|---|
| FR-039 | System MUST display a fixed seven-day calendar with reward slots Day 1 through Day 7. | Must |
| FR-040 | Calendar-day progression MUST NOT pause or shift to recover a missed reward. | Must |
| FR-041 | A reward not claimed in its eligible calendar day MUST be marked forfeited and MUST NOT be claimable later. | Must |
| FR-042 | Reward claims MUST be persisted and idempotent: a reward can be granted at most once for its eligible date/slot. | Must |
| FR-043 | Rewards MUST be non-monetary and consistent with the plant-care/learning theme; no ads, purchases or real-money mechanics. | Must |
| FR-044 | The calendar MUST anchor to the profile's first eligible game day, advance on every calendar day, repeat from Day 1 after Day 7, and make the day's reward claimable only after successful watering through 00:15 after that date. Exact reward contents remain subject to content/economy approval. | Must |

### 4.7 Obstacles and educational content
| ID | Requirement | Priority |
|---|---|---|
| FR-045 | Baseline obstacle roster MUST support plastic bag, foam/oil, boat, Ganga dolphin, golden mahseer, floating diya/marigolds and lotus. | Must |
| FR-046 | Each collision object MUST have a contextual short hit message and fact accessible on the result card. | Must |
| FR-047 | The golden mahseer MUST be rarer than standard obstacles; exact spawn weights are configurable/TBD. | Must |
| FR-048 | A short plant fact MUST be shown after successful watering. | Must |
| FR-049 | All educational facts and sensitive cultural representations MUST undergo editorial/factual review before release. | Must |
| FR-050 | The design MUST depict ritual objects such as floating diyas respectfully, without mockery. | Must |

**Content note:** The existing prototype treats lotus as an obstacle, while the write-up suggests it might work better as a bonus. For baseline compatibility it remains an obstacle until a design change is approved. Advanced obstacle behavior and additional animals/objects are future scope.

### 4.8 Save, suspend and resume
| ID | Requirement | Priority |
|---|---|---|
| FR-051 | System MUST save locally: last processed local date, watering completion date(s), plant stage, per-stage GP, health, reward calendar anchor/state, claims and forfeitures. | Must |
| FR-052 | System MUST persist an active-run snapshot including water, obstacle identities/positions/movement, spawn/simulation state, lane/input state where recoverable, elapsed simulation time, difficulty and goal-secured/bank-available flags. | Must |
| FR-053 | On app backgrounding, tab hiding or game exit, the active simulation MUST freeze; elapsed wall-clock time MUST NOT move obstacles, increase water or ramp difficulty. | Must |
| FR-054 | On return, system MUST restore the same saved simulation state/frame, rather than restarting or fast-forwarding the run. | Must |
| FR-055 | If the prior touch cannot safely remain active across suspension, the resumed frame MUST remain intact but collecting MUST require a new touch. | Must |
| FR-056 | System MUST prevent duplicate watering, GP, health restoration and reward claims across reloads, repeated callbacks and crash recovery. | Must |
| FR-057 | System MUST validate saved data and recover safely from missing, malformed or partially written saves without inventing progress or duplicating grants. | Must |
| FR-058 | System MUST persist a versioned snapshot plus event journal/outbox and last-good backup at least every 2 simulated seconds during an active run, on critical state transitions, and before background/pause. Writes MUST be atomic. Graceful resume MUST restore the same committed logical tick; after forced termination, restore the latest valid committed checkpoint. | Must |
| FR-059 | On resume, system MUST reconcile dates using the canonical profile timezone without advancing the frozen simulation. An active run remains assigned to its start day for at most the 15-minute rollover grace; after that it is abandoned and the day is reconciled as missed if unwatered. | Must |

### 4.9 Cloud synchronization and backend
| ID | Requirement | Priority |
|---|---|---|
| FR-060 | The first-release synchronization API MUST be deployed as a service on Google Cloud Run and exposed only over HTTPS. | Must |
| FR-061 | The client MUST persist progression locally before attempting synchronization and MUST allow play, local save/recovery and day reconciliation while offline or when the backend is unavailable. | Must |
| FR-062 | The client MUST queue immutable, uniquely identified progression events for later synchronization; retries, reloads and duplicate delivery MUST NOT duplicate watering, GP, reward or health effects. | Must |
| FR-063 | The backend MUST authenticate and authorize each synchronization request using Firebase Authentication Google sign-in tokens. Players may play locally as guests; cloud sync requires linking/signing in, which MUST preserve the local profile. | Must |
| FR-064 | The backend MUST derive day keys from the profile's canonical IANA timezone and server time; enforce at most one valid watering and reward grant per account/day; and return accepted, rejected or conflict results. For concurrent valid same-day events, the first server transaction wins. | Must |
| FR-065 | Synchronization MUST run asynchronously with bounded retry/backoff and MUST NOT block or alter an active run's deterministic simulation. | Must |
| FR-066 | The client and backend MUST use versioned request and persisted-event schemas so that supported client versions can be migrated safely. | Must |

## 5. State model and transitions

### 5.1 Run states
`READY → RUNNING_BELOW_GOAL → RUNNING_GOAL_SECURED → RESULT_SUCCESS → WATERING → DAILY_DONE`

Alternate transitions:
- `RUNNING_BELOW_GOAL → RESULT_FAILURE` on hit; `RESULT_FAILURE → READY` for retry.
- `RUNNING_GOAL_SECURED → RESULT_SUCCESS` on bank or hit (with different retained amounts).
- Any active running state `→ SUSPENDED → same running state` on hide/close/resume; snapshot restored without simulation advancement.

### 5.2 Invariants
1. `0 ≤ plantHealth ≤ 3`.
2. `retainedWater ≥ dailyGoal` for a successful run.
3. Before-goal collision implies `retainedWater = 0` and `wateringCompleted = false`.
4. After-goal collision implies `retainedWater = dailyGoal`.
5. Banking after goal implies `retainedWater = waterAtBank`.
6. Below Stage 6, `bonusGP = floor((retainedWater − dailyGoal)/30)` only when watering succeeds; Stage 6 stores no additional GP.
7. At most one watering grant and one reward grant per eligible date/slot.
8. Suspension does not change the simulation state or accumulated water.
9. A missed day never reduces GP or stage.

### 5.3 Daily reconciliation algorithm (normative behavior)
At startup/resume, resolve the current game-day key from the canonical profile IANA timezone. Finalize an elapsed date at 00:15 after its midnight boundary. For each finalized unwatered date since the last reconciliation, apply one health decrement (clamp to zero) and mark that date's unclaimed reward forfeited. Do not penalize the current unfinished date. A run active at midnight is keyed to its start date and can finish through 00:15; a run still active then is abandoned and the date is reconciled as missed if unwatered. No new attempt for the old date may start after midnight. Explicit timezone changes take effect at the next boundary and do not re-key historical events. Online server time is authoritative; offline results are provisional against the last verified time anchor. Reconciliation MUST be idempotent.

## 6. Data requirements

| Entity | Minimum fields | Validation / uniqueness |
|---|---|---|
| PlayerProgress | schemaVersion, currentSpecies, plantStage, stageGP, health, lastReconciledLocalDate | health 0–3; valid stage; nonnegative GP |
| DailyCare | localDate, wateringCompleted, completionTimestamp, baseGPGranted, bonusGPGranted | one successful grant per localDate |
| RewardCalendar | anchorLocalDate, slotDefinitions, slotStates, claimRecords | one grant per eligible date and slot |
| RunSnapshot | runId, dateContext, state, water, goal, simulationTick/time, difficulty, obstacles, spawn/RNG state, selectedLane, heldInputFlag, securedGoal, bankAvailable | coherent simulation state; versioned schema |
| SyncEvent | eventId, account/profile reference, event type, local sequence, day key, run/reward reference, payload, schema version, sync status | immutable event ID; safe duplicate delivery |
| SyncMetadata | account reference, device ID, last accepted server revision, last successful sync, retry state, clock/timezone anchor | no privileged server credentials on client |
| Settings | selectedLanguage, audioPreference (if audio added) | supported enum values |
| ContentCatalog | obstacleId, category, localizedHitText, localizedFact, factSource/status | approved facts for release |

**Storage design:** use a versioned local snapshot, event/outbox journal and last-good backup. Firestore Native mode is the selected Cloud Run datastore; the detailed schema/indexes, migration strategy, quota handling, retention rules, identity-data minimization and deletion/export process remain **TBD**. Persist only the minimum account identifier and progression data required for synchronization; do not persist privileged credentials in the client.

## 7. Non-functional requirements

| ID | Requirement | Verification |
|---|---|---|
| NFR-001 | UI MUST support adaptive portrait and landscape layouts and primary one-finger touch interaction on Android. | Android device/layout testing |
| NFR-002 | UI SHOULD load quickly and run smoothly on low-end Android hardware; numerical load-time, FPS and memory budgets are **TBD**. | Performance profiling on agreed devices |
| NFR-003 | Hindi Devanagari text MUST render legibly without clipping; English alternate copy MUST be complete for baseline screens. | Localization QA |
| NFR-004 | Essential game information (goal, held lane, result, health, reward state) MUST remain understandable without audio. | Silent-mode usability review |
| NFR-005 | Save/recovery MUST be deterministic and idempotent under reload, duplicate events and storage interruption. | Fault-injection and repeated-resume tests |
| NFR-006 | Game MUST remain usable for local gameplay, save/recovery and daily reconciliation without a live backend connection. Local guest play is allowed; cloud sync requires a linked Firebase/Google account. Offline events are provisional until server reconciliation. | Offline-mode test with Cloud Run unavailable; guest/link/reconnect integration tests |
| NFR-007 | Animation SHOULD use the planned paper-cut, stepped-motion aesthetic (~12 visual animation steps/s) without tying gameplay physics correctness to render cadence. | Art/engineering review |
| NFR-008 | Gameplay configuration SHOULD be separated from localized content and stateful persistence logic. | Code review |
| NFR-009 | UI SHOULD offer readable text, clear touch targets and visual state feedback; exact accessibility targets are **TBD**. | Accessibility/usability review |
| NFR-010 | Cultural and educational content MUST be reviewed for accuracy, respectful treatment and asset rights. | Editorial sign-off |
| NFR-011 | Invalid save files MUST fail safely with a documented recovery path and no duplicate grants. | Corruption tests |
| NFR-012 | The build MUST NOT contain ads, paid items or real-money rewards. Local guest play and Google account linking for sync MUST preserve local progress. | Release audit and account-flow integration tests |
| NFR-013 | Cloud Run endpoints MUST enforce authenticated transport, authorization, input validation, replay/idempotency protection and rate limits appropriate to progression-bearing requests. | Security review and API integration tests |

## 8. UI and visual requirements

### 8.1 Required screens
- **Home/Ready:** title, simple controls, Play, plant condition, daily availability and language switch.
- **Gameplay:** three lanes, collection zone, obstacles, matka/water meter, daily target, stage/day, conditional Take the pot button.
- **Result:** retained/spilled amounts, success/failure, collision message/fact when applicable, next action (retry or water).
- **Watering:** pot pours water, plant reacts and stage/health updates are displayed.
- **Daily Done:** watering confirmation, GP/stage status and a plant fact.
- **Seven-day rewards:** Day 1–7 slots and their current states.

### 8.2 Art direction
Layered paper-cut shapes, cream cut edges, offset shadows, kraft-paper base, indigo/blue river, marigold/pink/vermilion/terracotta accents, muted brown/grey pollution objects, Indian riverbank skyline. Body font: **Hind**; display/title font: **Yatra One** (subject to appropriate font licensing and device rendering tests). Stepped stop-motion visual cadence is approximately 12 fps; simulation and input should remain robust independent of artistic frame stepping.

### 8.3 Audio
Audio playback remains undecided and is not required for the first release. The implementation should provide named, no-op-capable audio event hooks for gameplay feedback, but must not include sound assets or enabled playback until approved.

## 9. Error handling and edge cases

| Case | Expected behavior / required decision |
|---|---|
| Hit just before goal | Failure, spill all, retry permitted. |
| Hit exactly when goal is crossed | Resolve collision before adding that tick's water; the hit wins and the goal is not secured on the same tick. |
| Hit after goal | Preserve goal amount, spill extra, successful watering eligible. |
| Bank button tapped repeatedly | One terminal run result and one watering grant. |
| Background while holding | Freeze at saved state; new touch may be required after resume. |
| Browser/app force-killed | Restore latest valid checkpoint; successful active-run writes occur at most 2 simulated seconds apart, plus critical transitions. |
| Resume after several days | Apply each elapsed missed-day penalty once; do not fast-forward obstacles. |
| Complete daily watering, reopen same day | Daily Done state; no second watering/GP grant. |
| Open at midnight with unfinished run | Keep an already-active run on its start-day key for up to 15 minutes; then abandon/reconcile it and allow the new day. No new old-day attempt may start after midnight. |
| Health already zero | Further missed days keep health at zero; growth unchanged. |
| Watering at health two or three | Restore up to health three; never exceed cap. |
| Extra retained water not multiple of 30 | Award only whole bonus GP, via floor. |
| Corrupt/missing storage | Validate; restore last valid state or controlled fallback; no fabricated rewards. |
| Manual clock or timezone change | Profile timezone and server time govern online keys; timezone changes take effect next day; offline results are provisional and validated on sync. |
| Seven-day cycle ends | Advance to Day 1 on the next calendar day; no missed slot is recovered. |
| User changes language mid-run | UI copy updates; simulation state remains unchanged. |
| Cloud Run service is unavailable | Continue local play and durable local event recording; queue synchronization for retry without blocking the game. |
| Sync event is rejected or conflicts | Preserve local evidence, apply the approved conflict policy, and show a recoverable status without silently duplicating or discarding grants. |

## 10. Acceptance tests (baseline)

| Test ID | Scenario | Expected result | Covers |
|---|---|---|---|
| AT-001 | Hold top lane 2 simulated seconds without obstacles | Approximately 15 units at baseline rate, subject to numerical tolerance | FR-008, FR-011, FR-015 |
| AT-002 | Slide held finger from top to bottom | Collection source changes to bottom without lift | FR-009 |
| AT-003 | Lift finger | No additional water accumulates | FR-010 |
| AT-004 | Obstacle enters an unheld lane | No collision | FR-013 |
| AT-005 | At stage 1, collide at 20 water | Failure, 20 spilled, 0 kept, retry available, no plant change | FR-016–018, FR-024 |
| AT-006 | At stage 1, collide at 40 water | 24 kept, 16 spilled, watering succeeds, 0 bonus GP | FR-019–023 |
| AT-007 | At stage 1, bank at 54 water | 54 kept, 0 spilled, watering succeeds, 1 bonus GP | FR-021–023 |
| AT-008 | Fail five times on same date | All five retries allowed, no health or GP penalty | FR-018, FR-034 |
| AT-009 | Complete watering twice via repeated event/reload | Only one GP/health restoration application | FR-005, FR-037, FR-056 |
| AT-010 | Skip three fully elapsed dates starting health 3 | Health becomes 0; stage/GP unchanged | FR-026–029, FR-036 |
| AT-011 | Water plant at health 0 | Health becomes 2 | FR-028 |
| AT-012 | Water plant at health 2 | Health becomes 3 | FR-028 |
| AT-013 | Pause at obstacle position x and water w; resume later | x and w unchanged until gameplay resumes | FR-052–055 |
| AT-014 | Pause while finger is down | Frame restored; new touch needed if held input cannot be restored | FR-055 |
| AT-015 | Miss an eligible reward date | Reward marked forfeited, cannot be reclaimed | FR-040–042 |
| AT-016 | Repeat reward-claim callback | Reward granted once only | FR-042 |
| AT-017 | Switch Hindi/English | Required screens and facts render in selected language | FR-006, NFR-003 |
| AT-018 | Corrupt local snapshot | Safe recovery, no duplicate GP/reward | FR-057, NFR-011 |
| AT-019 | Reach six mango stages through GP thresholds | Appropriate stage visuals/visitors shown | FR-025, FR-030–031 |
| AT-020 | Open app on next local calendar day | Fresh daily challenge; yesterday's watering does not count | FR-032–033 |
| AT-021 | Complete watering offline, then reconnect and retry the same sync event | Local grant remains single; Cloud Run accepts it once and duplicate delivery is a no-op | FR-061–063 |
| AT-022 | Submit two same-account, same-day watering events from separate devices | First valid server transaction is credited; later event receives conflict status, remains in local history, and is explained without double-credit | FR-064 |
| AT-023 | Make the Cloud Run endpoint unavailable during a run | Input, simulation, local save and run resolution continue; sync is queued for later retry | FR-061, FR-065 |
| AT-024 | Complete watering on Day 7 and claim before 00:15 after the date, then open the next profile-timezone date | Day 7 reward is claimable after watering; next date is Day 1; an unclaimed/missed slot is forfeited, not shifted | FR-039–044 |
| AT-025 | Start a run before midnight and resolve it at 00:05; separately keep a run active past 00:15 | First run credits its start date; second expires, grants nothing, and its date is reconciled as missed if unwatered | FR-035, FR-038, FR-059 |
| AT-026 | Force-stop between successful active-run checkpoints | Restore the latest valid checkpoint, normally within 2 simulated seconds; no duplicate progression; require fresh touch | FR-052–058 |
| AT-027 | Obstacle hit shares the tick that would reach the goal | Hit takes precedence; goal stays unsecured and all collected water spills | FR-017, FR-038 |
| AT-028 | Change device timezone or wall clock, then sync from a second device | Historical day keys do not move; online server/profile timezone is canonical; conflicts do not double-credit watering | FR-063–065 |
| AT-029 | Create profile as guest and later link Google sign-in | Existing local plant, day, reward and outbox state is preserved and sync becomes available | FR-061–063 |
| AT-030 | Submit concurrent offline watering events for one account/day | First valid server transaction is credited; losing event is retained locally, shown as conflict, and never silently double-grants progression | FR-062–064 |
| AT-031 | Complete watering that crosses multiple stage thresholds, then continue watering at Stage 6 | Intermediate surplus carries forward; at Stage 6 GP stays capped while health/reward behavior remains available | FR-022–031 |
| AT-032 | Create a new guest profile | Stage 1 mango, 0 GP, health 3; creation date becomes the reward-cycle anchor | FR-025–029, FR-044, FR-063 |

**Release criterion:** All Must requirements that are not explicitly designated as deferred must pass acceptance tests; the Phase 0 correctness baselines are specified in §§2.4 and 5.3. Remaining platform, content, operational and privacy decisions in §11 must be closed before their dependent release work. Specific target devices, performance thresholds and severity policy remain TBD.

## 11. Remaining decisions for subsequent phases

| ID | Decision | Why it matters |
|---|---|---|
| OD-01 | Supported Android versions/devices, distribution channel and measurable performance budgets | Android compatibility and release acceptance |
| OD-02 | Exact Day 1–7 reward items and any growth-boost content/caps | Reward content/economy; resolve before reward implementation |
| OD-03 | Cloud Run region, deployment environments, monitoring and operating budget | Backend deployment readiness |
| OD-04 | Account recovery/linking edge cases, deletion/export, data retention and privacy notice | Account launch and compliance |
| OD-05 | Exact deterministic spawn tables, ramp caps and balance values | Core-game tuning and playtest |
| OD-06 | Accessibility targets, tutorial completion/replay flow and orientation-change UX | UX/content acceptance |
| OD-07 | Fact sourcing, translations, rights and editorial review | Educational-content release gate |
| OD-08 | Analytics/crash-reporting scope and vendors | Privacy/operations review |

## 12. Future enhancements (not baseline acceptance requirements)

- New tree species after mango completion, including neem and species-specific visitors/facts.
- Additional obstacles: turtle, fish shoal, kingfisher, fishing net, sewage patch, sandbars, cattle, whirlpool, monsoon logs, gharial and water hyacinth.
- More complex object behaviors: diving wildlife, lane-switching fish, multi-lane hazards, rows of diyas and stage-based obstacle unlocks.
- Cleaner river as garden grows; rivers themed for other Indian states.
- Soundscape and event audio.

## 13. Traceability and change control

Each requirement ID should map to implementation tasks, unit/integration tests and final QA evidence. Changes to daily goal, reward eligibility, save/resume semantics, collision outcomes, progression formulas or Cloud Run synchronization contracts must update this SRS, the GDD/TDD and associated tests together. Version local-save and synchronization-event schemas, and document migrations before changing persisted data structures.

### Source baseline and limitations
This SRS records the Phase 0 first-release defaults in the linked Phase 0 decision log. Numeric lane values and growth thresholds come from the prototype description and remain tunable; exact reward contents and remaining decisions in §11 are explicitly deferred. This is a requirements baseline, not a claim that the planned Unity game or backend is implemented.
