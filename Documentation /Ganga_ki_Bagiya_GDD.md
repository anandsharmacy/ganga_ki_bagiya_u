**गंगा की बगिया**

**GANGA KI BAGIYA**

**GAME DESIGN DOCUMENT**

_Collect clean water. Protect the river. Grow a little every day._

Working title  
Prepared for Dainik Bhaskar Game Lab 2026  
Document status: Design baseline for prototype-to-production planning  
Languages: Hindi first, with English toggle

# Document Control

| **Document**               | Game Design Document (GDD)                                                                                                   |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| **Game title**             | गंगा की बगिया (Ganga ki Bagiya) — working title                                                                              |
| **Genre**                  | Casual one-finger timing game with daily-care plant-growing layer                                                            |
| **Primary platform**       | Android first release; adaptive portrait and landscape; standalone HTML prototype as the behavior reference                   |
| **Target session**         | 1–3 minutes per run; one successful watering per local calendar day                                                          |
| **Audience**               | Hindi-speaking adults around 24–44, particularly mobile-first players in Tier 2 and Tier 3 cities who are not regular gamers |
| **Commercial constraints** | No ads, purchases, real-money mechanics, or unlicensed copyrighted material; first-release account sync is planned for Google Cloud Run |

# 1\. Game Overview

**Genre:** Casual one-finger timing game with a daily-care plant-growing layer

**Platform:** Android first release with adaptive portrait and landscape layouts; iOS and Web are follow-on targets. A standalone HTML build is the playable behavior prototype.

**Session length:** 1-3 minutes per run, one watering per day

**Languages:** Hindi first, English toggle

**One-line pitch:** Collect clean water from a three-lane paper-cut river, dodge what floats by, and carry the water home to a mango plant that grows a little every day.

**Design goals:** The game targets the brief's four pillars: mass appeal, Indian relevance, simple mechanics that are hard to master, and daily engagement through long-term systems.

# 2\. Audience and constraints

- **Players:** Hindi-speaking adults, around 24-44, in Tier 2 and Tier 3 cities, mobile-first, mostly not regular gamers.
- **Familiar ground:** The matka, the Ganga, the mango tree, and festival motifs need no explaining.
- **Constraints from the brief:** No monetization loops, no real-money mechanics, no clones, no complex systems, no insensitive content, and no copyrighted material.
- **Device assumption:** Low-end Android phones, so the game should be light and quick to load.

# 3\. Core gameplay

### Controls

A single finger does everything:

- **Touch a lane** to start collecting water from it.
- **Slide to another lane** while holding to switch.
- **Lift your finger** to stop collecting.

### The river

The river has three horizontal lanes. Obstacles drift from left to right toward a marked **collection zone** on the right side. The pot fills only while the finger is down on a lane.

| **Lane** | **Current** | **Water per second** | **Obstacle speed** | **Gaps between obstacles** |
| -------- | ----------- | -------------------- | ------------------ | -------------------------- |
| Top      | Fast        | 7.5                  | 150 px/s           | Shortest                   |
| Middle   | Medium      | 4.2                  | 100 px/s           | Medium                     |
| Bottom   | Slow        | 2.2                  | 62 px/s            | Longest                    |

_(Values are from the current prototype and meant to be tuned.)_

**The tension:** The fast lane fills the pot about three times faster than the slow one, but its obstacles arrive quickly and close together. Players constantly choose between safe and slow or rewarding and risky.

### Difficulty within a run

The river speeds up the longer a run lasts, by up to about 90% over roughly 80 seconds, and gets slightly faster with each plant stage (about 5% per stage).

### What a hit does

An obstacle hit happens when an obstacle overlaps the collection zone in the lane being held. Obstacles in lanes the player isn't touching are harmless. What a hit costs depends on how far the run had got:

| **Situation**                          | **What happens**                                                            | **Plant watered?**       |
| -------------------------------------- | --------------------------------------------------------------------------- | ------------------------ |
| Hit \*\*before\*\* the goal is reached | The run ends and all water spills                                           | No. The player can retry |
| Hit \*\*after\*\* the goal is reached  | The goal's worth of water is safe, and only the extra above the goal spills | Yes                      |
| Take the pot \*\*after\*\* the goal    | The run ends safely and keeps everything, including the extra               | Yes                      |

A failed run never wilts or harms the plant. Only a missed day does.

### Run flow

1. The player starts a run.
2. They collect water, dodging obstacles.
3. When the **daily goal** is reached, the plant's watering is secured and a "take the pot" button appears.
4. **Bank or gamble:** Banking ends the run and keeps all the water. Continuing can earn bonus growth, but a hit spills the extra.
5. The run ends on a hit or when the player banks.
6. A result card shows what the player kept, what spilled, and a hit message and fact about what they met.
7. If the goal was reached, they water the plant. If not, they can try again.

# 4\. Water goal and growth points

- **Daily goal:** 24 + 8 × (stage − 1) units of water. Early days are easy and later stages ask for more.
- **Bonus:** Below Stage 6, every 30 retained units beyond the goal adds one extra growth point. Bonus water only counts if it's banked or survives to the end of the run. Stage 6 is capped: further successful waterings grant no GP until another species is approved.
- **Example:** At stage 1 the goal is 24. A player who holds 40 when they hit keeps 24 and loses 16. A player who holds 40 and taps "take the pot" keeps all 40.
- **Pot display:** A paper-cut matka with an oval window that fills with water, and a star appears when the goal is met.

# 5\. Obstacles

Each obstacle has its own hit message and a short fact, so a hit becomes a learning moment instead of a plain punishment.

| **Obstacle**                 | **Category** | **Hit message**            | **What it teaches (facts to be verified)**                                            |
| ---------------------------- | ------------ | -------------------------- | ------------------------------------------------------------------------------------- |
| Plastic bag                  | Pollution    | You scooped up dirty water | Plastic lasts for years in rivers and harms aquatic life                              |
| Foam and oil                 | Pollution    | You scooped up dirty water | Untreated drain and factory water pollutes rivers                                     |
| Boat                         | People       | Give the boat way          | Boats are a livelihood for people along the Ganga                                     |
| Ganga dolphin                | Wildlife     | You scared the dolphin     | National aquatic animal, nearly blind, uses sound to navigate                         |
| Golden fish (golden mahseer) | Wildlife     | You scared the golden fish | Lives in clear, fast Himalayan rivers, and its decline signals a river in poor health |
| Floating diya with marigolds | Culture      | Let the diya float by      | A respectful "let it pass" moment, with no mockery of worship                         |
| Lotus                        | Nature       | Leave the lotus be         | India's national flower (better suited as a bonus than an obstacle)                   |

**About the golden fish:** It appears less often than the others, so it feels special. Its gold colour tempts players to chase it like a reward, which makes it a deliberate trap, and the first hit explains why. Later it could dart between lanes.

**Planned obstacles**

River turtle, fish shoal, kingfisher, fisherman's net, sewage patch, sandbars, cattle wading near the bank, whirlpools, and monsoon logs. Gharial and water hyacinth would be rarer extras.

**Planned behaviours (beyond drifting)**

- Dolphins and kingfishers surface or dive after a visible warning (ripple or shadow).
- Turtles and fish shoals move between lanes.
- Nets and sewage patches span two lanes and force a lane change.
- Diyas arrive in rows that create a hold-lift-hold rhythm.
- Obstacles unlock gradually as the plant grows, so early days are easy to read.

# 6\. The plant and the garden

### Growth

The player has one plant, a mango tree, that grows through six stages:

| **Stage** | **Name**            | **Look**                                              | **Points needed** |
| --------- | ------------------- | ----------------------------------------------------- | ----------------- |
| 1         | Seed (बीज)          | Seed half-buried in soil                              | 2                 |
| 2         | Sprout (अंकुर)      | Thin stem with two copper-red young leaves            | 2                 |
| 3         | Seedling (पौधा)     | Taller stem, green leaves, copper tips                | 2                 |
| 4         | Sapling (छोटा पेड़) | Trunk with leaf pairs, a butterfly arrives            | 3                 |
| 5         | Blossom (बौर)       | Canopy with flower clusters, bees arrive              | 3                 |
| 6         | Fruiting (फल)       | Full canopy, hanging mangoes, a parrot and a squirrel | Final             |

Each successful watering adds one growth point, plus any bonus from banked extra water. Surplus points carry across intermediate stage thresholds in the same watering. Stage 6 is the first-release cap: later watering still restores health and may claim the daily reward, but adds no GP until a new species is approved. A new profile starts at Stage 1, 0 GP, and health 3. The mango stages follow the real tree: new mango leaves are coppery red, flower clusters are called _bor_ or _manjari_, and the mango is India's national fruit.

### Daily care

- The plant can be watered **once per profile game-day**. A canonical IANA timezone is captured from the device when the profile is created; the game day resets at midnight in that timezone. Explicit timezone changes take effect at the next day boundary and do not rewrite previous dates.
- A player has **unlimited attempts** to complete the daily water-collection challenge. A failed run does not use up the day's watering opportunity and does not reduce plant health or growth.
- To complete the day's care, the player must reach the daily water goal and finish the run through the existing goal-secured flow. The watering sequence then applies the day's growth and health effects. The player must collect water again for the next day's watering.
- If watering is incomplete by 00:15 after the profile date ends, that date counts as missed (with the rollover rule below for a run already active at midnight). Plant health decreases by one level for each missed date, down to level 0. Leaves droop and colours fade, but growth progress is preserved.
- Watering restores two health levels, up to the maximum health level of 3.
- The plant **never fully dies**, and missed days never erase accumulated growth progress.
- Each run belongs to its start-day key. A run already active at midnight may resolve until 00:15 in the profile timezone; no new attempt for the old date may start after midnight. A new-day run waits while the prior run remains active. One still unresolved at 00:15 is abandoned and its date is reconciled as missed if unwatered.

### Daily progression and seven-day rewards

- The game uses the profile's **canonical calendar date** to determine the current day and whether a daily watering has been completed. The daily cycle changes at midnight in the pinned profile timezone, not 24 hours after the previous watering. Server time is authoritative online; offline calendar events remain provisional against the last verified server-time anchor.
- A new local day provides a fresh water-collection challenge. The player must fill the pot to that day's goal and water the plant again; yesterday's completed watering does not count for the new day.
- Include a **fixed seven-day reward calendar**. It displays the rewards for Days 1–7 and indicates which rewards are upcoming, available, or claimed.
- The reward calendar follows calendar-day progression. A missed day's reward is **forfeited** and cannot be claimed later as a make-up reward. Missing a day does not shift the calendar to preserve that reward.
- The calendar starts on the profile's first eligible game day, repeats from Day 1 after Day 7, and makes each date's reward claimable after successful watering through 00:15 after that date. An unclaimed or missed reward then forfeits without shifting the calendar. Exact reward items remain to be defined before reward implementation.
- Daily rewards must remain in-game rewards that fit the plant-growing and learning experience. The game remains free of ads, purchases, and real-money mechanics; account-based progression sync through Google Cloud Run is required for the first release.
- Reward claims must be recorded persistently and must not be granted more than once for the same calendar day.

### Interrupted runs and session persistence

- If the player backgrounds, closes, or leaves the game during an active run, gameplay must freeze and resume at the same **last committed logical tick** when the player returns. Elapsed real-world time must not advance obstacle movement, water collection, run difficulty, or other simulation state while suspended.
- Persist enough state to restore the active run accurately, including water collected, obstacle positions and movement state, lane/input state where recoverable, run timer and difficulty, whether the daily goal has been reached, and whether the player can take the pot.
- Checkpoint at least every 2 simulated seconds, on critical state transitions, and before background/pause. On graceful resume, restore the same committed tick and require a fresh touch if the previous input cannot be safely restored. After forced termination, restore the latest valid checkpoint; unsaved per-frame state is not guaranteed.
- Save daily status, plant stage, growth points, health, current local date, reward-calendar state, reward claims, and active-run state on the device. Handle interrupted or invalid saves without granting duplicate rewards or unnecessarily discarding valid progress.

### Long-term progression

Additional tree species such as neem are future scope. Finishing the mango's Stage 6 does not automatically unlock another species in the first release.

# 7\. Learning layer

Learning happens through play, not lectures:

- Every hit shows a short message that names the lesson ("You scared the dolphin") and a one-line fact.
- A short plant fact appears with each watering.
- Facts should be checked by an expert source before release, such as Bhaskar's own research desk.

# 8\. Visual style

**Paper-cut, stop-motion, Indian folk.**

- The world is built from flat layered paper shapes with cream cut-edges and hard offset shadows, with a subtle paper grain over the whole scene.
- Animation steps at about 12 frames per second instead of smooth motion. Wobbles, drifting clouds, rising smoke, and moving water all happen in jumpy frames.
- **Skyline:** A factory with a chimney and a pipe dripping into the river, a temple and ghat steps, a tree, a mountain, and drifting clouds.
- **Palette:** Marigold, rani pink, vermilion, and terracotta accents on a kraft-paper base, with indigo and blue lanes for the river, darkest for the fastest. Pollution objects use muted brown and grey.
- **Type:** Hind for body text and Yatra One for titles and the title sign.

# 9\. Screens

1. **Home or ready screen:** Short instructions and a Play button. A thirsty-plant note appears if the plant is wilting.
2. **Gameplay:** The river, the pot, the garden, a water meter, and the day and stage readout. A "take the pot" button appears once the goal is met.
3. **Result card:** The hit message and fact, plus what was kept and what spilled.
4. **Watering sequence:** The pot slides over the plant, tilts, and pours, and the plant grows in stepped frames.
5. **Daily done card:** Confirms the watering, shows growth progress, and shares a plant fact.
6. **Seven-day reward calendar:** Shows the current day in the fixed seven-day cycle, claimed rewards, upcoming rewards, and any missed rewards that have been forfeited.

# 10\. Audio (playback undecided)

Potential cues include river ambience, a splash when water is collected, a soft clay-pot tap, a gentle growth sound, and distinct obstacle-hit sounds. Audio playback is undecided and is not part of the first release. The Unity implementation should provide audio event hooks only; do not add sound assets or enable playback until approved.

# 11\. Technical notes

- Light download and smooth performance on low-end Android.
- Hindi and English text, with Devanagari rendering tested early.
- Progress saved on the device, including the current local calendar date, daily watering status, plant stage, growth points, health, seven-day reward-calendar progress, claimed/forfeited rewards, and active-run snapshot.
- The daily reset uses local midnight. Date changes and missed-day health effects must be processed consistently when the game is reopened, including after multiple missed days.
- Active gameplay freezes while the app is suspended and resumes from the exact saved simulation state; real-world elapsed time does not advance the run.
- Save/resume and reward-claim logic must be idempotent so a reload cannot duplicate watering, growth, or rewards.
- All speeds, rates, goals, gaps, and difficulty multipliers should live in one tuning file.

### Asset organisation

All game art lives under `Assets/` in three folders. Each image exists as an `.svg` source and a `.png` export.

| **Folder**           | **Contents**                                                                                                          |
| -------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `Assets/background/` | Sky elements: `sun`, `cloud 1`, `cloud 2`, `cloud 3`, `cloud back 1`, `clod back 2` (sic; filename kept as in the project) |
| `Assets/obstacles/`  | Obstacles and floating objects: `boat`, `croc`, `dirt`, `diya`, `dolphine` (sic), `fish 1`, `fish 2`, `flower`, `plastic`, `pollution` |
| `Assets/water/`      | River lanes: `fast river` (top lane), `medium river` (middle lane), `slow river` (bottom lane)                         |

Notes:
- `croc`, `dirt`, `fish 1`, `fish 2` and `flower` are present as art but are not yet mapped to the obstacle table in Section 5 (for example, `croc` could serve the planned gharial). `dirt` and `pollution` may both cover the foam/oil and sewage obstacles.
- The filenames `clod back 2` and `dolphine` contain typos; rename them (and update references) before production.
- Other assets, such as the skyline (factory, temple, ghat, mountain), matka, mango stages and visitors, UI, and fonts, are not yet in the repository and will be added in later phases. Audio assets may be added only if playback is approved after the first release.

# 12\. Prototype status

**Playable HTML prototype:** The supplied standalone HTML implements the three-lane river, pointer hold/slide/release input, six obstacles (plastic bag, foam/oil, boat, Ganga dolphin, diya/marigolds, lotus) with localized hit facts, the stage-based daily goal, goal-secured continuation and bank action, six mango stages, basic health/wilting, watering animation, plant facts, and Hindi/English text. Demo controls manually advance/skip days and stages.

**Known prototype gaps:** It has no durable save, local-midnight reconciliation, seven-day reward calendar, account flow, Cloud Run sync, or suspended-run restoration. Its six obstacle types do not include the golden mahseer. After a hit beyond the secured goal, its current result/growth calculation uses all collected water rather than retaining only the goal amount; fix this when implementing the production rules in Unity.

**Unity implementation:** The Unity repository is the greenfield production client. The HTML prototype is a behavior and visual reference, not the production runtime. Local-midnight resets, unlimited retries, the fixed seven-day reward calendar, exact-state run recovery, and first-release account sync through Google Cloud Run require production implementation and QA.

**Not in the first release:** Ads, purchases, real-money mechanics, additional tree species, advanced obstacle behaviors, a river that gets cleaner as the garden grows, regional rivers, iOS/Web platform releases, and audio playback (decision pending). Account progression sync through Google Cloud Run is in first-release scope.

If you'd like, I can turn this into a Word document or PDF you can send.

# Design Baseline and Open Production Work

This GDD records the current design intent and the agreed daily progression decisions. It does not assert that every specified rule is already implemented in the prototype.

- Verify daily reset at midnight in the pinned profile timezone, including multiple missed days, explicit timezone changes, and the selected 15-minute run rollover grace.
- Verify unlimited retries and ensure failed runs do not reduce plant health or growth.
- Implement and test the fixed seven-day reward calendar; missed rewards are forfeited and do not shift forward.
- Implement and test same-logical-tick restoration after graceful suspension and latest-checkpoint recovery after forced termination. Real-world elapsed time must not advance the run simulation.
- Ensure local save operations are idempotent and cannot duplicate watering, growth points, or reward claims.
- Verify all educational facts with an authoritative reviewer before release.
- Tune gameplay values on target low-end Android devices and document measurable performance acceptance criteria in the SRS/QA plan.
- Use the selected Phase 0 rules in the [decision log](./Phase_0_Audit_and_Decisions.md); resolve remaining reward-content and release decisions before their dependent phases.
