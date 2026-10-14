# River Run — Visual & Readability Audit

| | |
|---|---|
| **Date** | 13 Oct 2026 |
| **Build under test** | `Builds/Android/GangaKiBagiya-Phase1.apk`, build #2 (13 Oct 2026 23:26, 44.2 MB). Unity 6000.6.4f1, URP 2D Renderer, package `com.DefaultCompany.ganga_ki_bagiya_u` |
| **Device** | Android Emulator AVD `GangaKiBagiya_API35`: Android 15 (API 35), tablet system image (arm64-v8a), 320 × 640 px at 160 dpi, GPU `swiftshader_indirect`. This is the AVD Antigravity last used |
| **Also reviewed** | Build #1 screenshot reported by the project owner (fig 1); Unity editor batch renders at 1080 × 1920 (9:16); FBX geometry and animation takes; Animator controllers; source code |
| **Source revision** | `6c9d784` plus the uncommitted working tree of 13 Oct 2026 |
| **Specs checked against** | GDD, SRS, TDD, Game Flow & Level Design (GF), `final output for the game .png` |
| **Requested by** | Project owner |

## 1. Summary

Both reported problems reproduce on the current build. Neither is random; each comes from how the scene is laid out.

- **Obstacles overlap (VA-01, VA-02).** Obstacle spacing is defined as a *fraction of the screen width*, tuned against a 1280-px landscape reference, while the obstacle art has a *fixed world size*. On every portrait phone, consecutive obstacles in a lane are 0.45–0.76 world units apart, but each one is 1.08–1.64 units wide. **Every consecutive pair overlaps**, and each lane carries 1.7–2.6× its own width in obstacle art.
- **Clouds are misaligned (VA-03, VA-04, VA-05).** The sky was placed at fixed positions and sizes made for a wider screen. On portrait it sits under the HUD and across the top river lane, and runs off the screen edges. The models are thick 3D slabs, so one background cloud is also drawn *in front of* top-lane hazards.

The audit found 11 more defects. The most visible: the plastic bag's "bob" animation contains a 3D tumble that turns the bag side-on into a flat magenta block (VA-06). That is a regression exposed by the 13 Oct animation-export change.

| ID | Finding | Severity | Priority | Evidence |
|---|---|---|---|---|
| [VA-01](#va-01) | Obstacles in the same lane always overlap *(reported)* | S2 Major | P1 | Device + analysis |
| [VA-02](#va-02) | Overlapping obstacles cut through each other | S3 Minor | P1 | Device |
| [VA-03](#va-03) | Sun and clouds misaligned on portrait screens *(reported)* | S3 Minor | P1 | Device + analysis |
| [VA-04](#va-04) | A background cloud is drawn in front of hazards | S2 Major | P1 | Device + depth analysis |
| [VA-05](#va-05) | Cloud backs render as opaque, saturated blue | S3 Minor | P2 | Device + source |
| [VA-06](#va-06) | Plastic bag turns side-on into a magenta block | S2 Major | P1 | Device + FBX analysis |
| [VA-07](#va-07) | Animation pipeline: action bleed, 6 unreachable clips, one-shots with no exit | S3 Minor | P2 | FBX + controller analysis |
| [VA-08](#va-08) | Matka (collection zone) reads as a peach disc | S3 Minor | P2 | Device + source |
| [VA-09](#va-09) | Fish scales spill outside the body outline | S4 Trivial | P3 | Editor render |
| [VA-10](#va-10) | Visual contact with the matka does not match the moment of the hit | S2 Major | P1 | Analysis |
| [VA-11](#va-11) | Obstacles pop in and out at the screen edges | S3 Minor | P2 | Analysis |
| [VA-12](#va-12) | Some models render 7–24% shorter than their target height | S4 Trivial | P3 | Unity vs FBX bounds |
| [VA-13](#va-13) | HUD sits under the system caption bar on large screens | S4 Trivial | P3 | Device (window insets) |

**Totals:** 0 Critical, 4 Major, 6 Minor, 3 Trivial. The fixes are planned in [River_Run_Visual_Fix_Plan.md](River_Run_Visual_Fix_Plan.md).

**Severity:** S1 Critical blocks play or loses data. S2 Major harms gameplay readability or fairness, or misrepresents a core element, in every run. S3 Minor is a visible defect that doesn't affect play. S4 Trivial is cosmetic or limited to some devices.
**Priority:** P1 fix before the next playtest. P2 fix this milestone. P3 backlog.

**Units:** "u" is a Unity world unit. The camera is orthographic with size 3.2, so the screen is always 6.4 u tall and 100 px = 1 u on the test device. Screen width W is 3.20 u on the test AVD (1:2), 3.60 u on 9:16 phones and 2.88 u on 9:20 phones.

---

## 2. Findings

### <a id="va-01"></a>VA-01 — Obstacles in the same lane always overlap
**S2 Major · P1 · Reported by the project owner · Reproduces 100% (every run, every lane, every portrait aspect ratio)**

**Steps:** install build #2 and launch it; tap **खेलें / Play**; don't touch the screen; watch the three lanes for 10 s.

**Expected:** each hazard reads as a separate object with water between neighbours (GF §5.2: *"Recognizable silhouettes in the small mobile viewport"*).
**Actual:** 4–7 hazards pile up in each lane, and neighbours overlap by a third to a half of their width (fig 2–4).

**Measurements** at base difficulty. The speed ramp doesn't change spacing, because the spawn gap shrinks by the same factor the speed grows.

| Lane | Speed (ref px/s) | Spawn gap | Centre spacing (fraction of W) | Test AVD (W 3.20) | 9:16 (W 3.60) | 9:20 (W 2.88) |
|---|---:|---:|---:|---:|---:|---:|
| Top | 150 | 1.8 s | 0.211 | 0.68 u | 0.76 u | 0.61 u |
| Middle | 100 | 2.5 s | 0.195 | 0.62 u | 0.70 u | 0.56 u |
| Bottom | 62 | 3.2 s | 0.155 | 0.50 u | 0.56 u | 0.45 u |

- **Hazard widths on screen** (Unity renderer bounds at the 1.05 runtime scale): boat 1.08, diya 1.09, plastic 1.34, pollution 1.36, dolphin 1.64 u. Two neighbours stop touching only when their centres are at least (wA + wB) / 2 apart, which is 1.08 u for the narrowest possible pair.
- **Overlap:** the narrowest pair overlaps by 37–54% of its width on the test AVD, 30–48% on 9:16 and 44–58% on 9:20.
- **Coverage:** the art in each lane adds up to 1.9–2.6× the lane's visible width on the test AVD, and 1.7–2.3× on 9:16.

**Root cause.** An obstacle's position is `progress`, a fraction of the *screen* width: `x = width × (progress − 0.5)` (`Phase1GameController.cs:955`). Spacing works out to speed × gap / 1280 (`Phase1Tuning.cs:23–28`, `Phase1RunSimulation.cs:247`). The art, though, is sized in fixed world units (1.05 u tall). The tuning table was written against a 1280-px *landscape* reference. On a 16:9 landscape screen (W = 11.4 u) the same table gives 1.8–2.4 u spacing and nothing overlaps. On portrait the screen is 3–4× narrower, so the spacing shrinks and the art doesn't. The specs ask for the opposite: *"Translate speeds into a fixed logical coordinate system so portrait/landscape and different resolutions have equivalent simulation difficulty"* (GF §4.2; TDD §5.3).

**Contributing factor.** The dolphin's rest pose is tilted, and the leap adds up to 35° roll and +0.09 u rise. That makes it 1.43 u tall, taller than the 1.25 u lane pitch (`LaneWorldY`, `Phase1GameController.cs:86`), so it also crosses into the neighbouring lanes (fig 3).

### <a id="va-02"></a>VA-02 — Overlapping obstacles cut through each other
**S3 Minor · P1 (ships with VA-01) · Reproduces 100% whenever two hazards overlap**

**Expected:** where art overlaps, one object sits cleanly in front of the other.
**Actual:** parts of one obstacle poke through the other. Tilting obstacles produce jagged seams and striped slivers (fig 5, 6).

**Root cause.** Every hazard is placed at the same depth, z = −0.2 (`Phase1GameController.cs:955`). Each model is a stack of extruded layers 0.25–0.45 u deep. Two overlapping hazards therefore occupy the same depth range, and the depth buffer interleaves their layers instead of drawing one whole object over the other. Some idle animations tilt the slabs out of the screen plane (the boat's ±3° pitch; the plastic bag in VA-06), so the intersection moves every frame.

### <a id="va-03"></a>VA-03 — Sun and clouds misaligned on portrait screens
**S3 Minor · P1 (reported) · Reproduces 100%**

**Expected:** a sky band visible between the HUD and the river. Nothing covers the lanes or runs off-screen (GF §8.2 "no HUD overlap"; GF §4.5 *"Avoid visual ambiguity from overlapping foreground parallax layers"*; reference art).
**Actual:** the sun is never visible. The clouds sit across the top lane, partly under the HUD card, and are cut off at the screen edges. The two cloud backs merge into one blue band that looks like a misaligned river edge (fig 2–6, 8).

**Measurements.** The HUD card's bottom edge is at 105 px of 640, which is world y = 2.15 (pixel-sampled). The top lane's water starts at y = 1.90. That leaves a sky band only **0.25 u (about 4% of the screen)** tall.

| Element | Placement in `BuildWorld` | Vertical extent | Hidden under HUD | Reaches into top lane | Cut off at screen edge (AVD / 9:16) |
|---|---|---|---:|---:|---|
| Sun | x −0.32 W, y 2.45, height 0.75 | 2.08–2.83 | 90% | — | 0% / 0%, and behind Cloud Back 2 |
| Cloud Back 2 | −0.16 W, 2.20, 1.60 | 1.40–3.00 | 53% | 0.50 u | 4% / 0% |
| Cloud Back 1 | 0.30 W, 2.30, 1.50 | 1.55–3.05 | 60% | 0.35 u | 26% / 23% |
| Cloud 1 | 0.38 W, 1.95, 0.85 | 1.52–2.38 | 26% | 0.38 u | 23% / 20% |
| Cloud 2 | −0.42 W, 1.95, 0.85 | 1.52–2.38 | 26% | 0.38 u | 30% / 27% |
| Cloud 3 | 0.02 W, 2.45, 0.70 | 2.10–2.80 | 93% | — | 0% / 0% |

The top row of riverbank scenery (croc, fish 1 and flower at y 1.95, height 0.45) sits in the same strip and is hidden behind the clouds and HUD. The cloud drift animations move the clouds a further ±0.21–0.29 u.

**Root cause.** `BuildWorld` (`Phase1GameController.cs:762–767, 775`) positions the sky at hard-coded world y values with absolute heights. x is a fraction of the width, but the widths are absolute, so narrow screens clip the clouds. Nothing accounts for the HUD's rectangle (`Phase4Ui.cs:300–326`) or the lane band. Because the orthographic size is fixed, the vertical problem is the same on every portrait device. The horizontal clipping gets worse as screens get narrower.

### <a id="va-04"></a>VA-04 — A background cloud is drawn in front of hazards
**S2 Major · P1 · Reproduces 100% for top-lane hazards in the left two-thirds of the screen**

**Expected:** *"Camera/parallax must never obscure an incoming hazard or the collection zone"* (GF §8.2).
**Actual:** the tops of top-lane hazards (boat sails, dolphin backs, the pollution box) are cut off by the opaque Cloud Back 2 (fig 5, 6). On the right side, Cloud Back 1 covers the tops of dolphins and pollution pipes.

**Depth analysis.** The code means the order to be sky (z −0.01 / −0.02), then riverbank (−0.12), then hazards (−0.2), then matka (−0.3): layers 0.01–0.1 u apart. But each model is 0.15–1.06 u deep, so mesh thickness decides what is drawn in front, not that order. Smaller z is nearer the camera:

| Object | Placed at z | Mesh depth in game | Nearest face z |
|---|---:|---:|---:|
| Plastic bag while side-on (VA-06) | −0.20 | 1.39 | **−0.89** |
| **Cloud Back 2** | −0.01 | 1.06 | **−0.54** |
| Matka (collector) | −0.30 | 0.44 | −0.52 |
| Plastic bag | −0.20 | 0.45 | −0.43 |
| Diya / boat | −0.20 | 0.31 / 0.30 | −0.36 / −0.35 |
| Cloud Back 1 | −0.01 | 0.65 | −0.34 |
| Dolphin / pollution | −0.20 | 0.27 / 0.25 | −0.33 / −0.33 |
| Riverbank scenery | −0.12 | 0.15–0.35 | −0.19 to −0.30 |
| Clouds 1–3 / sun | −0.02 / 0.00 | 0.28–0.29 / 0.23 | −0.16 / −0.11 |
| Rivers | +0.05 | 0.02–0.03 | +0.04 |

Cloud Back 2 is nearer the camera than every hazard. Cloud Back 1 is nearer than dolphins and pollution pipes. The matka is not covered by clouds, because it sits to the right of Cloud Back 2 and in front of Cloud Back 1. A side-on plastic bag (VA-06), however, comes nearer than the matka and covers it as it passes.

**Root cause.** `normalize_mesh` scales Z by the same factor as X and Y (`build_all_assets.py:159`). A sheet authored "0.03 thick" therefore ends up 0.24–0.79 of its own height deep, which is then multiplied by the in-game scale (1.6 for Cloud Back 2). The placement depths (`Phase1GameController.cs:793, 820, 841, 955`) are an order of magnitude smaller than the meshes are deep.

### <a id="va-05"></a>VA-05 — Cloud backs render as opaque, saturated blue
**S3 Minor · P2 · Reproduces 100%**

**Expected:** a faint blue haze, as in `Assets/background/clod back 2.png` and `cloud back 1.png`.
**Actual:** solid `#24A5ED` blocks. The pixel directly below the HUD samples as exactly `#24a5ed`. Being close to the fast-river blue, the sky reads as part of the river (fig 2–6).

**Root cause.** The source SVGs draw the cloud backs with `fill:#24a5ed; opacity:.26` and a `#0e90c9` outline stroke. The pipeline keeps only the fill:
- `clean_svg_content` inlines fills only (`build_all_assets.py:11`).
- Blender's SVG importer ignores opacity and strokes.
- `convert_materials_to_pbr` builds opaque materials (`build_all_assets.py:65`), and Unity imports them as opaque URP Lit materials.

### <a id="va-06"></a>VA-06 — Plastic bag turns side-on into a magenta block
**S2 Major · P1 · Seen in every inspected frame: each bag goes side-on for part of its ~3 s loop**

**Expected:** the plastic-bag hazard bobs gently and stays recognisable (GF §5.1: "Readable dirty-water hazard").
**Actual:** the bag rotates in 3D. Side-on, it shows as a block about 45 × 55 px in its flower colour, or as striped slivers (fig 3–6). The sampled colour is `#DC45E5`; the SVG's flower fill is `#DD45E5`. This rules out Unity's missing-shader magenta (`#FF00FF`). Across a 10-frame burst, that colour covers 3,300–6,800 px per frame, far more than the small upright flower prints alone would.

**Root cause.** Verified by re-importing `plastic.fbx` in Blender. The controller's default state, `plastic|plastic_bob`, should only move the bag up and down. But the baked take also contains the *tumble's* rotation, which reaches X 84°, Y 42°, Z 84° half-way through. Blender's FBX "all actions" bake evaluates the object's NLA strips while baking each action, and `push_action_to_nla` (`build_all_assets.py:173–178`) leaves every action on an active NLA track. So every per-action take is a blend of all of that object's actions.

**History.** The earlier export also wrote clean per-strip takes, but those carried root-transform curves that overrode the game's positions and scales. That is why rivers, clouds and scenery collapsed to the centre on build #1 (fig 1). The 13 Oct change `bake_anim_use_nla_strips=False` (`build_all_assets.py:964`) removed the per-strip takes, which left the blended takes as the only clips and exposed this bug.

**Also:** the tumble itself is authored as full 360° turns about X and Y (`animate_plastic`, `build_all_assets.py:561`) on a 0.43 u-deep mesh. Even a clean tumble would show the edge and the mirrored back for much of its loop. It is currently unreachable (VA-07).

### <a id="va-07"></a>VA-07 — Animation pipeline defects
**S3 Minor · P2**

1. **Action bleed in every multi-action model** (same cause as VA-06), verified by re-importing the FBX takes. For example, `sun|sun_pulse` spins the sun using rotation from `sun_rotate` (113° half-way through), and `boat|boat_sail_flutter` carries the idle animation's bob offset.
2. **Six clips can never play:** `sun_rotate`, `boat_sail_flutter`, `dolphine_swim`, `plastic_tumble`, `pollution_drip`, `diya_idle`. `SetupAnimatorControllers.CreateControllerForModel` (`SetupAnimatorControllers.cs:115–140`) makes the first looping clip the default state and adds the others on the same single layer with no transitions.
3. **One-shots:**
   - `Matka|matka_collect` has an Any State transition on `Collect` but no exit, so after the first collected unit the matka never returns to its idle wobble.
   - `Collect` fires on every whole unit of water (`Phase1GameController.cs:1027`), every 0.13–0.45 s while a lane is held (7.5 / 4.2 / 2.2 units/s). That restarts the ~1 s squash before it can finish. This comes from analysis; the jitter hasn't been captured on video.
   - `croc_snap` (`Snap`) and `flower_bloom` (`Bloom`) never fire. The croc and flower are only riverbank scenery, not lane hazards (`Phase1Tuning.cs:31`), and no code ever sets `Bloom`.

### <a id="va-08"></a>VA-08 — Matka (collection zone) reads as a peach disc
**S3 Minor · P2 · Reproduces 100%**

**Expected:** a terracotta pot as in the reference art: rounded body `#C5602D`, cream belly panel `#ECDCBA`, light dots `#F6E8CC`, dark rim `#A34921` (sampled from `final output for the game .png`).
**Actual:** a pale peach circle with a smaller circle and a ring on top, like a snowman (fig 2–4, 8).

**Root causes:**
1. **Geometry.** `create_matka_model` (`build_all_assets.py:715–726`) builds the body, neck and rim from cylinders and a torus on Blender's default Z axis. After the upright export, Z points at the camera, so you see the cylinders' end caps (discs) rather than their sides.
2. **Colour.** The Base Color `(0.93, 0.56, 0.23)` (`build_all_assets.py:743`) is an sRGB terracotta typed into Blender's *linear* colour field. It displays as `#F7C584` (peach) instead of the intended `#ED8F3B`.
3. **Detail.** The belly panel, dots and rim colour from the target art are missing.

### <a id="va-09"></a>VA-09 — Fish scales spill outside the body outline
**S4 Trivial · P3 · Reproduces 100%, riverbank scenery only (fish are not lane hazards)**

In `fish 1.svg` and `fish 2.svg`, 29 scale shapes are clipped to the body with `clip-path: url(#clippath)`. Blender's SVG importer ignores clip-paths, so the full scale circles render as a yellow blob over the body outline (fig 7, rows 3–4).

### <a id="va-10"></a>VA-10 — Visual contact with the matka does not match the moment of the hit
**S2 Major · P1 · Reproduces 100% (analysis)**

**Expected:** a hit happens *when an obstacle overlaps the collection zone in the held lane* (SRS FR-013; GDD "What a hit does"). Each obstacle needs a *documented collision boundary* (GF §10.3).
**Actual:** a hit registers only at the single instant the obstacle's **centre** crosses `collectorProgress` = 0.82 (`Phase1RunSimulation.cs:247–252`). The art overlaps the pot well before that:

| Hazard | Centre distance at first visual contact | Seconds of visible overlap before the hit (top / middle / bottom lane, base speed) |
|---|---|---|
| Boat (narrowest) | 0.87 u (0.27 W on the AVD) | 2.3 s / 3.5 s / 5.6 s |
| Dolphin (widest) | 1.15 u (0.36 W on the AVD) | 3.1 s / 4.6 s / 7.4 s |

On 9:16 the range is 2.1–6.6 s. A player can keep holding while a boat visibly sits on the pot and lose nothing, then lose the run seconds later. Or they can let go at the last moment and escape a hit that has visibly already happened. The 1.05 × 1.05 `BoxCollider2D` triggers on obstacles (`Phase1GameController.cs:932`) are never read (there are no `OnTrigger*` handlers), so they don't define a boundary either.

### <a id="va-11"></a>VA-11 — Obstacles pop in and out at the screen edges
**S3 Minor · P2 · Reproduces 100% (analysis)**

Obstacles spawn at progress −0.04 and are removed past 1.15 (`Phase1RunSimulation.cs:286, 306`). Those margins are fractions of the screen width and ignore the art's width.

- **Test AVD:** a boat appears with 0.41 u (38%) already on screen, and a dolphin with 0.69 u (42%). A dolphin is removed with 0.34 u (21%) still visible.
- **9:16:** pop-in is 37–41%; pop-out is up to 17%.

### <a id="va-12"></a>VA-12 — Some models render 7–24% shorter than their target height
**S4 Trivial · P3 · Reproduces 100%**

`normalize_mesh` scales each model to height 1 using *all* vertices, including edge-only geometry left behind by stroke-only (`fill: none`) SVG paths. Unity discards loose edges on import, so the visible mesh is shorter. Visible height as a fraction of the intended height: croc 0.78, plastic 0.76, diya 0.91, fish 1 0.93 (Unity renderer bounds compared with the FBX vertex span). Introduced by the 13 Oct `fill: none` change in `clean_svg_content`.

### <a id="va-13"></a>VA-13 — HUD sits under the system caption bar on large screens
**S4 Trivial · P3 · Tablet and large-screen layouts**

On the tablet AVD, Android reports a visible `captionBar` inset (frame 0,0 – 320,36, top). Its app-handle pill overlaps the end of the HUD title (fig 1, 2). `Phase4Ui` never reads `Screen.safeArea` or the window insets, although TDD §4.1 and §9.2 require safe areas to be handled.

---

## 3. Ruled out or not reproduced

| Observation | Result |
|---|---|
| Pot filled with no touch during the build #1 session | **Not reproduced.** On build #2 the pot stayed at 0.0 / 24 for over 10 minutes with no input. The input code releases the lane correctly (`Phase1GameController.cs:630–634`). Most likely someone was playing in that session. |
| Magenta blocks are a missing shader | **Ruled out.** The colour is the plastic bag's own fill (VA-06). |
| Touch bands don't line up with the drawn lanes | **Ruled out.** The touch bands are 20–40–60–80% of screen height (`LaneAreaRect`, `Phase1GameController.cs:1044`) and match the lanes, because the orthographic size is fixed. This will need revisiting if the camera framing changes. |
| Display cutout overlapping the UI | **Ruled out.** No cutout is active on the AVD; the overlap is the caption bar (VA-13). |
| Rivers, clouds and scenery collapsed to the centre (fig 1) | **Build #1 only.** Fixed in build #2 by removing the root-transform animation curves. |

## 4. Method

- **Device:** clean install (`adb install -r`, app data cleared), cold launch, run started from the home screen. Screenshots taken with `adb exec-out screencap`: a 10-frame burst at about 0.9 s intervals. Colours were read directly from the PNG pixels.
- **Editor:** Unity 6000.6.4f1 in batch mode. `Phase1GameController.BuildWorld()` was invoked through reflection and rendered at 1080 × 1920, along with a model-versus-sprite sheet.
- **Assets:**
  - FBX files parsed with Blender 5.2.2's `io_scene_fbx` parser, and the takes re-imported and sampled.
  - Animator controllers parsed from their YAML.
  - In-game depth and size computed as FBX span × in-game scale.
- **Numbers:** every number above is recomputed from the code constants (orthographic size 3.2, `LaneWorldY`, `Phase1Tuning`) plus the measured geometry, not read off screenshots.

## 5. Evidence

| Fig | File | What it shows |
|---|---|---|
| 1 | ![](audit_2026-10-13/fig01_reported_build1.png) `fig01_reported_build1.png` | Owner's report, build #1: overlapping obstacles, clouds over the top lane, scenery collapsed into the lanes (build #1 only), caption-bar pill over the HUD title |
| 2 | ![](audit_2026-10-13/fig02_build2_frame_a.png) `fig02_build2_frame_a.png` | Build #2: piled-up hazards in every lane, opaque blue cloud band under the HUD, magenta block at bottom left (side-on bag) |
| 3 | ![](audit_2026-10-13/fig03_build2_frame_b.png) `fig03_build2_frame_b.png` | Build #2: side-on plastic bag as a magenta block (top lane), dolphin crossing lanes, matka as a disc |
| 4 | ![](audit_2026-10-13/fig04_build2_frame_c.png) `fig04_build2_frame_c.png` | Build #2: magenta blocks in the middle and bottom lanes; hazards overlapping the matka |
| 5 | ![](audit_2026-10-13/fig05_top_lane_crop_b.png) `fig05_top_lane_crop_b.png` | Fig 3 top lane at 2×: cloud cutting off the boat sail and pollution box; side-on bag |
| 6 | ![](audit_2026-10-13/fig06_top_lane_crop_c.png) `fig06_top_lane_crop_c.png` | Fig 4 top lane at 2×: cloud cutting off sails and the dolphin; striped bag slivers; boats and dolphins interpenetrating |
| 7 | ![](audit_2026-10-13/fig07_model_vs_sprite.png) `fig07_model_vs_sprite.png` | Editor: each model (left) beside its PNG (right). Rows: boat \| croc; dirt \| diya; dolphin \| fish 1; fish 2 \| flower; plastic \| pollution. Fish scales spill outside the body |
| 8 | ![](audit_2026-10-13/fig08_editor_world_9x16.png) `fig08_editor_world_9x16.png` | Editor, 9:16, rest pose, no HUD: sky across the top lane, sun behind a cloud back, matka as a disc. Obstacles here were placed one per slot by the audit script, not by the game's spawner |
