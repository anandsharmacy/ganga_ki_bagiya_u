# Fix plan: River Run visual audit (VA-01 to VA-13)

## Context
The 13 Oct 2026 audit ([River_Run_Visual_Audit_Report.md](River_Run_Visual_Audit_Report.md)) tested build #2 on the `GangaKiBagiya_API35` emulator. It found 13 defects behind the two reported problems (obstacles overlapping each other, clouds misaligned) plus related ones. They come from five causes:

1. **Screen-relative spacing vs fixed-size art.** Obstacle positions and spacing are fractions of the screen width, tuned for landscape, but the art has a fixed world size (VA-01, VA-10, VA-11).
2. **Thick meshes and tiny depth offsets.** Models are 0.15–1.06 u deep, while layer depths are only 0.01–0.1 u apart, so mesh thickness decides what draws in front (VA-02, VA-04).
3. **Fixed-position sky.** The sky ignores the HUD, the lane band and the screen width (VA-03, VA-13).
4. **Animation export and controller defects.** Action bleed, unreachable clips, one-shots with no exit (VA-06, VA-07).
5. **SVG features the Blender pipeline drops or mishandles.** Opacity, clip-paths, stroke-only paths, plus a hand-typed sRGB colour in the procedural matka (VA-05, VA-08, VA-09, VA-12).

**Outcome wanted:** every hazard readable on its own; the sky inside its own band; the collector and hazards never covered; animations that match their names; the matka recognisable as a pot. All of it identical across portrait phones, as the specs require (GF §4.2, TDD §5.3).

## Decisions needed (defaults apply if no answer)
| # | Decision | Options | Default / recommendation |
|---|---|---|---|
| D1 | How to remove lane overlap | A: keep hazards 1.05 u tall and space them further apart. B: hazards 0.85 u tall plus spacing | **B.** See the numbers in step 3.2 |
| D2 | When a hit registers | Keep the current "centre crosses the pot" rule, or hit on visible overlap (literal SRS FR-013) | **Hit on overlap**, with a per-hazard collision half-width and a forgiveness factor |
| D3 | Portrait layout | Make room for a sky band by fitting the camera to the lane width and measuring the HUD | **As in Phase 4** |
| D4 | Cloud backs | Translucent 3D sheets from the Blender pipeline, or the existing PNG sprites | **3D** (keeps the 13 Oct "fix the Blender models" decision). Switch to sprites if the 4.2 check fails |

## Phase 1 — Animation export and controllers (VA-06, VA-07) · Blender + editor C# · S–M
**File:** `Assets/Blender/scripts/build_all_assets.py`

**1.1 Clean per-action takes.** At the top of `export_upright_fbx`, for each object with `animation_data`:
- remove all its NLA tracks;
- set `animation_data.action = None`;
- reset location, rotation and scale to rest (0 / 0 / 1).

In `create_action`, set `act.use_fake_user = True` so the actions survive with no users. Keep `bake_anim_use_all_actions=True` and `bake_anim_use_nla_strips=False`. Each `object|action` take will then contain only its own action, bound to the child only. The NLA strips are what currently bleed into every take.

**1.2 One looping clip per model.** Merge the loops that are meant to run together into a single `<id>_idle` action whose channel periods divide the loop length, so it loops seamlessly:

| Model | Becomes |
|---|---|
| Sun | One 120-frame loop: rotate 360° plus a pulse with a 60-frame period |
| Boat | One loop: rocking plus sail flutter |
| Diya | One loop: float plus idle |
| Pollution | One loop: flow plus drip |
| Dolphin | Keep `leap` as the loop. Delete `swim` (it's an alternative, not a layer) |
| Croc, flower, matka | Keep their one-shots (`snap`, `bloom`, `collect`) as separate actions |

**1.3 Plastic bag motion stays in the screen plane.** Replace `plastic_tumble` and `plastic_bob` with one `plastic_drift` loop: bob ±0.025 plus a Z sway of ±15°. Keep any X/Y rotation on any model at 10° or less.

**1.4 Controller generator** (`Assets/GangaKiBagiya/Editor/SetupAnimatorControllers.cs`, `CreateControllerForModel`):
- the default state is the single looping clip;
- each one-shot gets its Any State transition with `canTransitionToSelf = false`, plus an exit transition back to the default (`hasExitTime = true`, `exitTime = 1`, `duration = 0.1`);
- log a warning if a model has more than one looping clip, to catch regressions.

**Check:**
- Re-import every FBX in Blender and sample each take. No take may contain channels from another action; `plastic|plastic_drift` X/Y rotation is 0 throughout.
- Parse every `.controller`: exactly one looping state, which is the default; one-shots have exit transitions; zero orphan states.

## Phase 2 — Depth and layering (VA-02, VA-04) · Blender + C# · S
**2.1 Thin sheets** (`normalize_mesh`, `build_all_assets.py:121`). After the X/Y normalisation, compress Z for SVG sheets so the whole layer stack is at most **0.05 u deep at height 1**: multiply z by `min(1, 0.05 / depth)`. That keeps the layer order. Rivers are already 0.03 u. The procedural matka is exempt; its own depth band (2.2) makes its thickness irrelevant.

**2.2 Explicit depth bands** in `Phase1GameController.cs`, defined once and used by:
- `AddRiverLane` (:820)
- `AddModelOrSprite` and `AddSprite` (:841, :857)
- `UpdateObstacleViews` (:955)
- `SetupCollectorMarker` and `UpdateCollectorMarker` (:793, :992)

| Layer | z |
|---|---|
| Rivers | +0.60 |
| Sun / cloud backs / clouds | +0.45 / +0.40 / +0.35 |
| Riverbank scenery | +0.20 |
| Hazards | `−0.10 − 0.60 × progress`. Hazards further along draw in front; lane neighbours end up at least 0.09 u apart, more than a hazard's 0.05 u depth |
| Matka (collector) | −1.20. Always nearest, as GF §8.2 requires |

**2.3 Camera clip planes:** near 0.1, far 20 (the camera is at z = −10), set in `Phase1SceneSetup.BuildScene` and re-applied in `Phase1GameController.Awake`. This gives about 50× the depth precision of the current 0.3 / 1000, which the thinner sheets need.

**Check:** every SVG model's FBX Z span is at most 0.05. In the editor render, a hazard placed overlapping a cloud draws in front of it, and two overlapping hazards layer whole, with no seams.

## Phase 3 — Logical lane, spacing, hit window, spawn margins (VA-01, VA-10, VA-11) · C# + tests · M–L · needs D1, D2
**3.1 Fixed logical lane** (GF §4.2, TDD §5.3).
- Add `Phase1Tuning.logicalLaneLength = 3.6f`. That is the lane width visible on a 9:16 phone at orthographic size 3.2.
- Render positions as `x = (progress − 0.5) × logicalLaneLength` (`Phase1GameController.cs:955, 992`). The simulation keeps its `progress` units, so the deterministic schedule and the existing tests are unchanged.
- **Camera fits the lane width:** `orthographicSize = Mathf.Max(3.2f, logicalLaneLength / (2f × aspect))`, in `Awake` and `BuildScene`. Phones taller than 9:16 show the whole lane and gain height, which Phase 4 uses. Screens wider than the lane (tablets) get side panels or letterboxing outside ±L/2 (GF §8.2), so spawning stays hidden.
- **Touch mapping** (`LaneAreaRect`, `LaneAtScreenY`, :1044) must come from the lane world positions via `Camera.WorldToScreenPoint`, not the fixed 20–80% bands, because the framing now changes per device.

**3.2 Minimum spacing rule (deterministic).**
- Add `hazardHalfWidths[]` (logical units, per hazard sprite index, measured from the models after Phases 2 and 6) and `minClearGap = 0.25f` to `Phase1Tuning`.
- In `SpawnDueObstacles`, a due lane spawns only once its newest obstacle has moved at least `(halfW(prev) + halfW(next) + minClearGap) / logicalLaneLength` past the spawn point. Otherwise retry next tick, without consuming the next hazard sprite. Lane speed is uniform, so spacing set at spawn holds for the obstacle's whole life.

What D1 means in practice (L = 3.6, base speed, 0.25 u clear gap, hazard widths after the Phase 6 fix; the ×1.9 ramp shortens the gaps proportionally but keeps the spacing):

| Option | Hazard height | Centre spacing, average / worst pair | Effective gap top / mid / bottom (average pair) | Hazards on screen per lane |
|---|---|---|---|---|
| Today (overlapping) | 1.05 u | 0.76 / 0.70 / 0.56 u by lane | 1.8 / 2.5 / 3.2 s | 4.7 / 5.1 / 6.5 |
| A | 1.05 u | 1.66 / 2.01 u | 3.9 / 5.9 / 9.5 s | 2.2 |
| **B** | 0.85 u | 1.39 / 1.67 u | 3.3 / 4.9 / 8.0 s | 2.6 |

For B, set the hazard model scale in `UpdateObstacleViews` (`targetHeight`, :921) to 0.85.

**3.3 Hit on overlap** (D2; SRS FR-013; GF §10.3 "documented collision boundary").
- Add `collectorHalfWidth` (the matka's half-width, about 0.33 u at 0.9 scale today; re-measure after Phase 5) and `hitForgiveness` (default 0.8) to `Phase1Tuning`.
- In `Phase1RunSimulation.Step` (:247–252), replace the centre-crossing test with a **swept interval test**. A hazard in the held lane hits when the span it covered this tick, `[prevX, newX] ± halfW × hitForgiveness`, intersects `collectorX ± collectorHalfWidth`. Also hit when the player moves into a lane where a hazard already overlaps the pot.
- The sweep keeps `CollisionIsResolvedBeforeSameTickWaterCollection` (128,000 px/s, one-tick jump) valid, because fast obstacles can't tunnel through the pot.
- Delete the unused `BoxCollider2D`/`Rigidbody2D` on obstacle views and the collector (`PrewarmObstaclePool`, `SetupCollectorMarker`, :932). Nothing reads them, and they document a wrong boundary.

**3.4 Spawn and despawn margins.** Spawn at progress `−(halfW + 0.1) / L` and despawn past `1 + (halfW + 0.1) / L`, so hazards enter and leave fully off-screen. Widen the progress bounds checked in `TryRestoreSnapshot` (currently −0.1 to 1.16) to match.

**Tests** (`Assets/GangaKiBagiya/Tests/EditMode/Phase1RunSimulationTests.cs`):
- add: lane neighbours never closer than the required spacing over a 120 s run at maximum difficulty;
- add: a hit registers at first overlap;
- add: no hit from hazards in other lanes;
- add: moving into an overlapped lane hits;
- update the existing hit tests if their tick counts change.

## Phase 4 — Portrait layout, cloud backs, safe area (VA-03, VA-05, VA-13) · C# + Blender · M · needs D3, D4
**4.1 Layout budget computed at runtime.** Replace the static `LaneWorldY` (:86) and the hard-coded sky and riverbank coordinates (:762–781) with a `LayoutWorld()` step. It runs when the screen size changes and when the HUD's UI Toolkit geometry changes (`GeometryChangedEvent` on the HUD card in `Phase4Ui.BuildHudScreen`, :300). It works top to bottom in world units:
- **Top reserve:** the HUD card's bottom edge plus the safe-area top.
- **Sky band:** what is left after the lanes, clamped to 0.5–1.2 u.
  - Sun 0.8 × band tall; clouds at most 0.6 × band.
  - x as a fraction of the visible width, **clamped so every element stays fully on screen**.
  - Cloud backs become one haze strip across the band.
- **Lanes:** 1.25 u pitch and 1.3 u river art (a 3.8 u block), with the top lane's top edge right under the sky band.
- **Bottom strip:** riverbank scenery if at least 0.45 u remains above the bottom UI (hint and bank button); otherwise hidden. The top riverbank row moves into the bottom strip or is dropped.

On the test AVD (orthographic size 3.6 after 3.1, so 7.2 u tall) the budget is: HUD 1.18 u, sky band about 1.1 u, lanes 3.8 u, bottom UI 1.07 u when "Take the pot" shows. That leaves no room for the bottom riverbank strip, so it is hidden there.

**4.2 Translucent cloud backs (D4).**
- In `clean_svg_content`, record each class's `opacity`/`fill-opacity`.
- Give every translucent fill a unique colour. The importer keys materials by colour, so nudge one channel by 1/255 if a colour appears with two different opacities.
- In `convert_materials_to_pbr`, set Principled `Alpha` and `surface_render_method = 'BLENDED'`.
- **Check in Unity** that the imported material is transparent (`_Surface == 1`). If URP's FBX import keeps it opaque, add a small `AssetPostprocessor.OnPostprocessMaterial` that switches materials with alpha below 1 to Transparent.
- **Fallback:** don't wire the two cloud-back FBXs in `Phase1SceneSetup` (`BackgroundNames` indices 1 and 5), so `AddModelOrSprite` uses the PNG sprites, which already have the painted haze and outline.

**4.3 HUD safe area** (`Phase4Ui`, TDD §4.1 / §9.2). Pad the UI root by `Screen.safeArea` and re-apply on change. If the large-screen caption bar (36 px on the tablet AVD) isn't inside `safeArea`, add the `captionBar` inset from `WindowInsets` through a small `AndroidJavaObject` call.

## Phase 5 — Matka model (VA-08) · Blender · S
Rebuild `create_matka_model` (`build_all_assets.py:715`) with **Y as the pot's axis**. The authoring frame's +Z faces the camera.
- **Body:** a UV sphere, r 0.45, created with `rotation=(90°, 0, 0)` so its poles are on Y, Y-scale 0.9, colour `#C5602D`.
- **Neck:** a cylinder rotated 90° about X, colour `#C5602D`.
- **Rim:** a torus rotated 90° about X, colour `#A34921`.
- **Belly panel:** a flattened ellipse on the +Z surface, colour `#ECDCBA`.
- **Dots:** five small dots in an arc, colour `#F6E8CC`.

Colours are sampled from `final output for the game .png`. Add an `srgb_to_linear()` helper and use it for every hand-typed colour, because Blender colour fields are linear. Keep `animate_matka` as it is. *Optional later:* a designer-made `matka.svg` through the SVG pipeline would match the other assets' style.

## Phase 6 — Asset clean-up (VA-09, VA-12) · Blender · S
**6.1 VA-12.** In `process_curves_to_mesh`, right after the join, delete loose geometry (edit mode, `mesh.delete_loose(use_verts=True, use_edges=True)`) before Solidify and normalisation. Every model then normalises to its visible height. Expect plastic, croc and diya to grow by up to 31%; re-measure `hazardHalfWidths` (3.2) afterwards.

**6.2 VA-09.**
- *Preferred:* the artist releases the clipping masks and intersects the scales with the body in the source files (Illustrator: Pathfinder → Intersect), then re-exports `fish 1.svg` and `fish 2.svg`. No code change.
- *Fallback:* in `clean_svg_content`, drop the children of groups whose class sets `clip-path`. That loses the scale texture, but nothing spills.

## Execution order
1. **Blender assets:** Phase 1, step 2.1, Phase 5, Phase 6. Then regenerate all FBX files **once** with `/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python Assets/Blender/scripts/build_all_assets.py`, run from the project root. Delete the `.blend1` backup it leaves.
2. **Depth:** steps 2.2 and 2.3.
3. **Gameplay:** Phase 3, after D1 and D2.
4. **Layout:** Phase 4, after D3 and D4.
5. **Build and run:** build the APK with `-executeMethod GangaKiBagiya.EditorTools.Phase1SceneSetup.BuildAndroidApk`, which also regenerates the controllers. Then install and run it on `GangaKiBagiya_API35` and on one 9:20 phone AVD.

## Files touched
- `Assets/Blender/scripts/build_all_assets.py` — Phases 1, 2.1, 4.2, 5, 6
- `Assets/Models/**` — regenerated FBX files; `.meta` import settings unchanged. Animator controllers are regenerated by `BuildScene`
- `Assets/GangaKiBagiya/Editor/SetupAnimatorControllers.cs` — 1.4
- `Assets/GangaKiBagiya/Editor/Phase1SceneSetup.cs` — 2.3, 3.1, and 4.2 if the fallback is used
- `Assets/GangaKiBagiya/Scripts/Phase1GameController.cs` — 2.2, 3.1, 3.3 (collider removal), 4.1
- `Assets/GangaKiBagiya/Scripts/Phase1Tuning.cs`, `Phase1RunSimulation.cs` — 3.1–3.4
- `Assets/GangaKiBagiya/Scripts/Phase4Ui.cs` — 4.1 (geometry hook), 4.3
- `Assets/GangaKiBagiya/Tests/EditMode/Phase1RunSimulationTests.cs` — Phase 3 tests

## Verification
**Automated:**
- the Blender re-import checks after Phases 1, 2 and 6;
- Unity EditMode tests: the existing 25 plus the new Phase 3 tests, run with `-runTests -testPlatform EditMode`;
- an editor batch render of `BuildWorld()` at 9:16, 1:2 and 9:20 that asserts, from renderer bounds:
  - no sky element intersects the HUD rectangle or the lane band;
  - no two lane neighbours' bounds overlap;
  - the matka's nearest face is nearer than every other object's.

**On device:** a 10-frame `adb exec-out screencap` burst per device, checked against:

| ID | Pass criterion |
|---|---|
| VA-01 | No two hazards in a lane overlap; at least 0.25 u of water between neighbours on 1:2, 9:16 and 9:20 |
| VA-02 | Where art overlaps (e.g. a leaping dolphin), one object is wholly in front; no seams or stripes |
| VA-03 | Sun and all clouds fully visible inside the sky band: none under the HUD, none over a lane, none cut off at an edge |
| VA-04 | No sky element ever drawn over a hazard; the matka is never covered |
| VA-05 | Cloud backs translucent (the pixel below the HUD is no longer `#24A5ED`) |
| VA-06 | No plastic-colour blocks; the bag's flower colour covers no more pixels than upright bags would |
| VA-07 | The sun rotates *and* pulses; every controller has one loop plus one-shots that exit; the matka returns to idle after collecting |
| VA-08 | The matka reads as a pot; body colour matches `#C5602D` within a small tolerance |
| VA-09 | No scale geometry outside the fish outline (model-vs-sprite render) |
| VA-10 | The hit registers on first visible overlap (EditMode test plus a held-lane check on device) |
| VA-11 | Hazards enter and leave fully off-screen |
| VA-12 | Every model's visible height is 1.00 ± 0.02 at scale 1 |
| VA-13 | HUD title clear of the caption bar on the tablet AVD |

## Risks
- **Overlap-based hits arrive earlier, so runs get harder.** Balance them with D1 and playtest before locking the numbers (GF LD-02).
- **Fitting the camera to the lane makes lanes smaller on tall phones** (80% on 9:20). Re-check touch-target size there.
- **Very thin sheets rely on depth precision.** 2.3 covers this; still verify on one real low-end GPU.
- **Unity's FBX import may not produce transparent materials.** The sprite fallback in 4.2 covers this.
