# UI/UX Overhaul and Local Player Profile — Report

**Date:** 14 Oct 2026 · **Project:** Ganga ki Bagiya (Unity 6000.6.4f1, URP 2D, Android, UI Toolkit) · **Test device:** Android emulator `GangaKiBagiya_API35`, 320 × 640 px

## 1. Findings that shaped the work
- The game's UI is code-built UI Toolkit in one file (`Phase4Ui.cs`): no USS, no settings, pause or profile screens, no audio assets. Screens swapped instantly.
- Persistence is JSON files in `persistentDataPath` (game progress) and PlayerPrefs (language).
- Kept: the folk paper-craft identity, the palette, Hind and Yatra One fonts, the Hindi/English table, every gameplay rule and the save format.

## 2. What changed

### Design system
- **`UiTheme`:** colour tokens, spacing and radii; paper-card panels with a thick darker bottom edge for depth; a gradient scrim so the live river world shows through menus; interactive button styling.
- **Button states:** normal, hover, pressed (scale and darken), focused (ring), selected, disabled.
- **Components:** primary and secondary buttons, text input, segmented selector, toggle, avatar badge, modal, toast, progress bar.
- **Contrast:** colours were not changed from the original palette except the muted grey (`#8C8576` → `#6E6858`) for contrast; avatar letters switch between white and ink by luminance.

### Screens
- **Onboarding** (new, first launch only): Welcome → Details → Preview → Welcome message.
- **Main menu** (redesigned): greeting with avatar badge, plant card, one primary Play button, Rewards, and Profile / Settings / How to play.
- **Settings modal** (new): language, UI sounds, Reduce motion, player-profile summary and edit.
- **Pause modal** (new): Resume, How to play, Settings.
- **HUD:** pause button, animated meter, hit flash, goal toast, pot-count pop.
- **Result, done, rewards:** restyled with the shared panel; they remain full screens with crossfades (see section 9).
- No loading screen was added (startup is instant; a fake one would mislead). No Continue / New Game / Quit buttons (no such features exist).

### New and modified files
- **New:** `Scripts/UiTheme.cs`, `UiMotion.cs`, `UiAudio.cs`, `UiSettings.cs`, `PlayerProfile.cs`, `Phase4UiOnboarding.cs`, `Phase4UiMenus.cs`; `Tests/EditMode/PlayerProfileTests.cs`.
- **Modified:** `Phase4Ui.cs` (now `partial`, theme-based helpers, transitions, onboarding gate, new home), `Phase4Localization.cs` (+43 bilingual keys), `Phase1GameController.cs` (`SetPaused` / `IsPaused`; app-focus changes can no longer resume a user-paused game).
- Earlier-session files (Blender export, simulation, layout) are unchanged by this work.

## 3. First-launch profile flow
1. **Gate:** `ResolveScreen()` shows onboarding when no valid profile exists; otherwise the normal flow. Players with existing progress but no profile see it once; their progress files are untouched.
2. **Welcome:** title, intro, **Create your profile**, language switch.
3. **Details:**
   - Name: required, 2–20 characters; letters (Devanagari included), spaces, `.` `'` `-`; errors appear live and on submit.
   - Gender: optional, Male / Female / Other / Prefer not to say; tapping the selected one clears it.
   - Age: optional; empty is allowed, otherwise 5–120.
   - Avatar colour: one of 6.
4. **Preview:** badge, name, and gender and age only when given. **Edit profile** goes back with the fields pre-filled; **Enter game** saves.
5. **Complete:** badge pop, confirm sound, "नमस्ते, {name}! आपकी यात्रा शुरू होती है।" / "Welcome, {name}. Your journey begins now.", then a fade to the menu after about 2 s (tap to skip).
6. **Later launches:** the profile loads and the menu opens directly.
7. **Management:** Menu → Profile, or Settings → Edit profile. The same form is used in a modal with Save / Back.

## 4. Persistence
- `player-profile-v1.json` in `Application.persistentDataPath`: `schemaVersion`, `name`, `gender`, `age`, `avatarIndex`, `createdUtc`, `completed`. No e-mail, phone, password or account data. Names are never logged.
- Writes are atomic (temp file, then replace).
- A missing file means no profile. An unreadable or invalid file is moved to `.corrupt`, a message is shown on the welcome screen, and the player re-creates the profile. Nothing throws.
- UI preferences (UI sounds, Reduce motion) are in PlayerPrefs; language was already there.

## 5. Transitions and animations
- **Screens:** crossfade with a slight slide. The incoming screen is disabled until its entrance finishes, so double taps cannot act on a half-shown screen.
- **Menu:** staggered entrance, once per screen per session.
- **Onboarding pages:** fade and slide, forward or backward, with staggered contents.
- **Modals:** scrim fade and panel scale-in (0.94 → 1), and the matching dismissal.
- **Buttons:** hover, press scale, selected and focus-ring states.
- **HUD:** meter fill transition and colour change at the goal, pot-count pop on each collected unit, hit flash, and a goal toast (all driven by the existing `AudioCue` events).
- **UI sounds:** four short procedural tones (tap, confirm, error, toast), generated in code with no assets.
- **Reduce motion:** all durations become zero and the hit flash is skipped.
- **Design decision:** pages and screens are hidden with `display`, not removed and re-added. A first build detached faded pages; UI Toolkit then left them at opacity 0, so Edit produced a blank screen. This was found in on-device testing and fixed.

## 6. Accessibility
- Touch targets at least 48 px (except the 40 px pause button and 38 px avatar swatches, inside 48 px ring cells).
- New text is at least 14 px, mostly 15–18 (the existing 11 px save-warning label is unchanged); entered text is bold and dark, placeholders muted.
- Focus ring on navigation-driven focus; the first button of each modal takes initial focus.
- Android Back: opens Pause during a run, steps back out of modals.
- Reduce motion and UI-sounds toggles in Settings; a Hindi/English switch on the welcome screen and in Settings.
- Layout keeps the existing `ScaleWithScreenSize` panel and safe-area padding.

## 7. Test results
**Automated:** Unity EditMode, **62 / 62 passed** (the original 28, which includes the localization tests that cover every new string, plus 34 new profile tests).
- **Profile tests:** round trip; missing file; five kinds of corrupt or invalid files moved aside; invalid profile not saved; overwrite; 12 name cases (empty, short, long, digits, markup, Devanagari, spacing); 12 age cases (empty, 4, 5, 120, 121, negative, text, decimal, 1000); optional gender.

**Build:** `Phase1SceneSetup.BuildAndroidApk` succeeds in batch mode, no compile errors or warnings from this code.

**On device (emulator, fresh install unless noted):**

| Scenario | Result |
|---|---|
| First launch shows welcome | Pass (fig 1) |
| Empty name | Error shown (fig 2) |
| Name `Alex99` | Live error shown |
| Age `3` rejected, `12` accepted | Pass (fig 3) |
| Gender left unselected | Pass; no gender line in preview (fig 4) |
| "Prefer not to say" | Preview shows "Gender: Prefer not to say" |
| Preview → Edit → fields pre-filled → Enter | Pass |
| Welcome message then menu | Pass (fig 5, 6) |
| Force-stop and relaunch | Straight to menu with greeting |
| Edit profile in Settings (gender, avatar) | Saved to disk (`gender 2`, `avatarIndex 5`), menu badge updated |
| Language switch in Settings and onboarding | Live update |
| Corrupt profile file (pushed with `adb`) | Moved to `.corrupt`, warning and onboarding, no crash (fig 14) |
| Three simultaneous taps on Create | One transition only (fig 15) |
| Pause during run | World stops (hazard positions unchanged over 3 s), Resume continues |
| Android Back during run | Pauses; a second Back resumes |
| Run → goal reached | Meter fills and turns green, "Take the pot" shown (fig 11) |
| Result → water the plant → done | Pass; progression applied (+1 growth point, 1/2) |

## 8. Not tested
- **Keyboard, controller and mouse hover:** the emulator had no such input.
- **Reduce motion:** the toggle persists in code and was shown, but I did not turn it on and re-check the animations.
- **UI sounds:** generated without errors, but audio was not audible in the test setup.
- **Hindi name entry:** `adb` cannot type Devanagari. It is covered by unit tests only.
- **Goal toast, hit flash and pot-count pop:** implemented and wired; the toast's 1.7 s window was missed by screenshots and the others were not captured.
- **Rewards screen** after the restyle: not opened on the device.
- **Other resolutions and landscape:** only 320 × 640 portrait was run.
- **Real hardware**, and the soft keyboard covering lower fields on small screens (the emulator's IME showed a side toolbar instead).

## 9. Known limitations and deviations
- **Continue is not disabled while the form is invalid.** It stays enabled and, on press, shows the errors and plays the error sound. This gives clearer feedback than a dead button.
- **Result, done and rewards stay full screens** with crossfades, not scale-in modals.
- **Hit flash and shake:** only a flash was built, no shake.
- **No volume, music or graphics settings:** no such systems exist in the game.
- **The first text-field focus on a fresh Android tablet image** opens the system stylus tutorial. This is the OS's behaviour and was handled in my test script, not by the app.
- **Menu layout:** the lower half of the main menu is empty on tall screens.

## 10. Screenshots
`Documentation /ui_2026-10-14/`: `01_welcome`, `02_form_empty_error`, `03_form_age_error`, `04_preview`, `05_welcome_message`, `06_main_menu`, `07_settings`, `08_profile_edit_modal`, `09_hud`, `10_pause`, `11_hud_goal_secured`, `12_result`, `13_done`, `14_corrupt_profile_recovery`, `15_rapid_triple_tap_result`.
