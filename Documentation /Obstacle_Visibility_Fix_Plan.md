# Fix invisible obstacles: Blender FBX models render edge-on

## Context
Gameplay works, but obstacles (and rivers, clouds, sun, matka) show only as thin, saturated bars (magenta/pink/grey/orange) instead of recognizable art. Decision: **fix the Blender 3D models** rather than fall back to the PNG/SVG sprites.

Root cause (verified in code, FBX node data and screenshot bar sizes):
1. **Sheets are exported lying flat.** `Assets/Blender/scripts/build_all_assets.py` builds each model as a flat SVG-derived sheet in Blender's XY plane (Z = thickness). `bpy.ops.export_scene.fbx(...)` (lines ~986 and ~1018) passes no `axis_forward/axis_up/bake_space_transform`, so every FBX root gets `Lcl Rotation (-90,0,0)` and the sheet ends up in Unity's XZ plane. The scene camera is orthographic, rotation 0, at z=-10 (`Assets/Scenes/SampleScene.unity`), so it sees only the extruded edge (e.g. plastic ≈ 0.55 units thick → the ~55 px magenta blocks).
2. **Nothing compensates in Unity.** `Phase1GameController.cs` instantiates the prefabs with only position + uniform scale: `AddModelOrSprite` (~L835), `AddRiverLane` (~L813, scale `worldWidth*1.15, 1.3, 1` → river is ~2 px thin), `UpdateObstacleViews` (~L912), `SetupCollectorMarker` (~L783).
3. **Prefabs override the working sprites.** `Phase1SceneSetup.cs` (L38-67) wires the FBX prefabs, and `if (prefab != null)` skips the sprite branch, so the valid `Assets/obstacles/*.png` sprites are never used.
4. **Secondary (not the cause of the bars):** `build_all_assets.py` strips every `<image>` and `<filter>` from the SVGs (L16-17) and rebuilds materials as flat Principled colours; `Assets/Blender/textures/` is empty. After the rotation fix the models will be visible but flat-colour extrusions without the raster detail of the PNGs. Coplanar curve layers may also z-fight.

Not the cause: URP asset / global settings diffs (Unity auto-regenerated noise), `_Recovery/0.unity`, import scale/compression settings, magenta "missing shader" (colours are real SVG fills).

## Plan

### Step 1 — Export the sheets upright (the real fix)
File: `Assets/Blender/scripts/build_all_assets.py`
- Keep the authoring frame and animations as they are (sun spins about Z, cloud/river drift in XY).
- Right before the two `export_scene.fbx` calls, put the exported content under a single parent Empty rotated 90° about X (child animations stay in the local XY frame), so that after the exporter's own Z-up→Y-up conversion the root rotation is identity and the sheet lies in Unity's XY plane facing the camera. Add `axis_forward='-Z', axis_up='Y'` (Unity convention) explicitly rather than relying on defaults.
- Pick the sign / extra 180° about Y so the art reads un-mirrored from a camera at -Z looking +Z (confirm visually, Step 3).
- Re-run the script headlessly with the installed Blender (`/Applications/Blender.app/Contents/MacOS/Blender -b Assets/Blender/GangaKiBagiya.blend --python Assets/Blender/scripts/build_all_assets.py`; check how `export_all_fbx.py` invokes it first) to regenerate `Assets/Models/{obstacles,background,water}/*.fbx`.
- Verify afterwards by parsing the new FBX root `Lcl Rotation` is (0,0,0) and that vertices span X/Y with small Z.

Fallback if the Blender-side parent trick misbehaves: single Unity-side helper in `Phase1GameController.cs` that sets `transform.localRotation` on instantiated models, applied at the four instantiate sites above. Only use if Step 1 fails.

### Step 2 — Check colour/detail after orientation is right
- Re-import in Unity and look at obstacles in Play mode.
- If flat-colour paper-craft layers look acceptable → done.
- If they look washed out or z-fight: (a) give each curve layer a small incremental Z offset in `process_curves_to_mesh` (L71-87) before solidify, and (b) stop discarding the raster art: keep `<image>` content as a baked texture (UV-mapped, Unlit/Sprite-Lit compatible material under the 2D Renderer). Defer (b) until Step 1's result has been seen — it is the larger job.

### Step 3 — Verify in Unity (simulator)
- Enter Play mode on the Device Simulator, confirm obstacles, rivers, clouds, sun and matka are visible and un-mirrored in all three lanes, matching `Assets/obstacles/*.png` silhouettes.
- Confirm obstacle trigger colliders (`1.05 × 1.05` in `UpdateObstacleViews`) still line up with the visible model.
- Compare against `Documentation /final output for the game .png`.
- Run existing tests in `Assets/GangaKiBagiya/Tests` to confirm nothing regressed.

## Files touched
- `Assets/Blender/scripts/build_all_assets.py` (export orientation; optional layer Z offset)
- `Assets/Models/{obstacles,background,water}/*.fbx` (regenerated; `.meta` import settings unchanged)
- `Assets/GangaKiBagiya/Scripts/Phase1GameController.cs` only if the fallback is needed

## Housekeeping noticed (not part of this fix)
Duplicate FBX pairs with spaces vs underscores (`fish 1.fbx`/`fish_1.fbx`, `medium river`/`medium_river`, `matka` in two folders); the scene uses the space-named ones. Regeneration will rewrite both, so leave as is.
