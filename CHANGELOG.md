2026-10-08 - GitHub Actions shared-library repair

- Build against published AethertekUI main so current shared APIs are available. Retain repository-specific read-only SSH deploy keys, which do not expire, and disabled credential persistence. Publish library APIs before consumer changes.

# Changelog

## Unreleased - Hindi font availability and recovery

- Treat the Hindi language-menu caption as optional while retaining mandatory selected/English catalogue checks. Refresh the stable Hindi option's availability with the existing font generation; unavailable captions use disabled ASCII `Hindi (unavailable)` without blocking ordinary languages.
- Keep font-failure status readable in ASCII and offer an explicit Use English action for selected Hindi through the existing configuration save route. Retain atlas roles, merges, dimensions and native control IDs.
- Source integration is complete; compilation, native availability/recovery checks and Linux/Wine acceptance remain pending.

## Unreleased - Original plugin images and UI guidance

- Replace Main's drawn emblem with the existing embedded plugin icon and add original-colour images to regular/ultra compact Main titles, including collapsed windows. Retain existing body geometry, title text, native actions and saved window placement; borrowed host textures keep aspect ratio and a blank reservation while unavailable.
- Clarify shared appearance controls and this plugin's existing automation/setup ownership in the README.
- Source integration is complete; compilation, native image/title/control checks and game acceptance remain pending.

## Unreleased - Button sizing

- Use local Toolbar metrics for ordinary buttons and natural heights for header, ultra compact support and outgoing translation actions. Grow for the active font and icons while retaining existing widths and font roles.
- Current Debug/x64 compilation passes. Final actual-product native checks pass 10195 assertions across 48 focused scenes and 128 pointer activations, with integer exit 0 in all 3 routes. Coverage uses English/Hindi captions, original exercised font roles, both densities, 100/150 percent scale and enlarged text; game/GPU acceptance remains separate.

## Unreleased - Managed CJK font atlas

- Merge one bundled CJK face per font role, selecting the active language's regional forms. Set both managed atlas dimensions to 4096 on every rebuild; preserve font heights, required glyph ranges, symbol merges and host-language coverage. Keep Body chat glyph ranges independent of the interface language.
- Current compilation and guarded production callback/rebuild checks pass, together with bounded native glyph checks for the checked text. Managed-host readiness, complete displayed glyph coverage, language/scale host rebuilds and game/GPU acceptance remain unverified.

## Unreleased - Native titlebar shortcuts

- Add Settings, Guide, Enabled and the existing one-way Turn on ultra compact action to every MainWindow surface, including detached conversations. Reuse the prepared plugin handlers and retain all body/composer controls.
- Keep the original lock first in native ordering and reserve native buttons and the version title before motion. Ultra compact stays a one-way action and rechecks current mode at click time. The unchanged DhogGPT.bat passes Debug/x64 with zero warnings/errors.
- Focused English checks pass 7,067 assertions and 336 installed-host native pointer presses across master/detached windows, regular/simple/ultra modes, both densities, 100%/150% scales and collapsed/expanded owners. Verify original lock identity, retained Settings/Guide handlers, exact saves/DTR updates and one-way Ultra behavior; 240 non-left callbacks stay inert. The 4096x4096 diagnostic atlas completes within 60 seconds/768 MiB. Composer execution, managed icon-font readiness, GPU and game acceptance remain separate.

## Unreleased - Community invite

- Update the existing Discord community action to https://discord.gg/ac6gjDvR8R.

## 2026-10-06 - Actions dependency revision

- Pin the existing AethertekUI checkout to published revision `6c193cf06ac67f954c549cafc2033ac0efdd630a`, which includes the Hindi text host required by this plugin. The preceding Actions run checked out the library before those APIs were published; local compilation alone did not establish runner compatibility.

## 2026-10-06 - Hindi interface source adoption

- Add the complete 311-entry Hindi catalog and append हिन्दी to the existing interface language choices. Scope Windows text shaping over all window and font-status draws, retaining managed font roles and CJK/symbol merges. Measure and paint shaped captions, conversation tabs, summaries and chat; bridge single-line and multiline editing while preserving original native IDs, source values and actions. Keep Hindi DTR and game context-menu labels in supported English.
- Reserve complete shaped checkbox captions inside the native item rectangle, retaining the original-label minimum and nonnegative adjusted inner spacing. The shorter Hindi Enabled caption previously lost four right pixels. Keep unshaped spacing and native labels/IDs unchanged.
- The current Debug x64 source builds with zero warnings/errors. Independent native checks pass 15,174 assertions across Main, ultra, Settings and Guide using inert empty-chat snapshots at both densities, reference/narrow widths and 100/150 percent scales, including 328 original-ID hovers, 48 preference edits and 24 safe guide callbacks. Focused Hindi/English controls, catalogs, glyphs, editing and save checks pass 7,325/7,119. Managed-host/game/GPU/IME acceptance remains pending.

## 2026-10-06 - Window appearance and transparency

- Move colour, compact mode and UI language into Window appearance settings, with independent compact/language visibility in regular and ultra headers and a main transparency toggle. Preserve saved focused/background opacity values, use 100%/50% defaults for new configurations, and persist automatic unfocused fade with a 10-second delay. Replace the former window-background alpha path with one complete-window opacity application after native motion restore; retain the separate composer opacity preference, focus observations, detached conversations and chat behavior. Translate new labels in all fourteen catalogs. The unchanged local launcher builds successfully with zero warnings and errors. Native appearance/persistence checks and game acceptance remain pending.

## 2026-10-05 - Rounded window chrome and native minimize (source adoption)

- Adopt per-instance rounded chrome and animated native minimize/restore for Main, Config, detached Main windows and font status. Round FirstUseGuide's chrome while retaining its NoCollapse; preserve control identities, layout, saved geometry and actions.
- Compilation, native interaction and game acceptance for this source adoption remain pending verification.

## 2026-10-05 - Window layout corrections

- Enlarge regular and ultra chat typography using a lexical window font scale that restores the caller's scale. Size passive headings from their actual glyph ink and preserve the compact summary, message editor and field geometry without changing font sources, atlas roles or native control identities. Refine the compact title, subtitle and Status proportions together with their reserved heading height, keeping the English reference windows free of vertical overflow.
- Place ultra-mode compact, colour and interface-language preferences in a measured top header. Retain the approved chat-language field width, current native title/version, original control IDs and existing preference callbacks.
- Keep native conversation-tab arrows and close controls visible. Reserve the larger original/translated caption width and replace only the original caption glyph ink, clipping translations to the native scrolling region while preserving custom translucent tab fills and native identities.
- Reserve a leading inset in native combo previews so negative-bearing glyphs remain visible, with original identities, values and callbacks preserved.
- Wrap guide bullets and reflow complete guide action buttons at narrow widths. Draw the main counter in its existing Caption font role.
- Verify the current offline window and tab-navigation matrix across fourteen locales: 186086 checks, including 560 native tab presses and 2301 scroll checks. Retain original IDs, preference saves and composer focus. Remaining reference refinements and managed-host/game acceptance stay open.

## 2026-10-03 - Additional UI languages

- Add Vietnamese, Brazilian Portuguese, Indonesian, Polish and Turkish to every DhogGPT window, guide and authored status using the existing UI-language setting and save path. Preserve all prior locale choices and chat-translation language settings.
- Include the native language names and required accented glyphs in the existing managed font checks. Hindi remains unavailable because the native rendering route does not shape Devanagari correctly.

## 2026-10-03 - UI feedback

- Restore the current assembly version in the main and detached title bars while retaining their native window identities.
- Measure single-line checkbox/action labels, complete combo previews and readable text, multiline, opacity and colour editors. Retain pending native widths, raw values and translated-label layout bounds; expose horizontal scrolling when required.
- Keep the compact checkbox's hidden native identity and square interaction, with its painted caption reserved in the layout.
- Current Debug/x64 compilation passed with zero warnings/errors. Native field, title, checkbox interaction, embedded-catalog and offline glyph checks passed across nine languages, both densities, two scales and three widths. Complete-window comparison and game visual acceptance remain pending.

## 2026-10-03 - AethertekUI adoption

- Rebuild the regular translator and ultra compact chat layouts from the approved references, with native vector branding, measured controls, responsive language groups and a scrolling regular body.
- Add shared compact density, nine UI languages and a relative colour theme through existing configuration/save paths. Preserve configuration/version, chat languages, custom message palettes, real-channel colours, logs, provider selection and payload-aware sending.
- Retain existing native control/window IDs, detached conversations, channel visibility/pinning, positions and opacity. Localize settings, guide, tooltips, DTR and plugin-authored popup/status text while preserving external names and message bodies.
- Use managed Segoe UI font roles with host CJK/symbol merges and explicit load/glyph errors. Chat/composer CJK coverage is independent of the selected UI language; host fonts remain undistributed.
- Local build/resource/package checks are separate from mcvaxius's game visual acceptance. No tests or live clients are run in this adoption pass.


## 2026-10-02 - Build and release repair

- Pin GitHub builds to SDK 10.0.201 and pass the downloaded Dalamud library path. Restore and build plugin projects with matching configuration, platform and runtime; stop on restore failure.
- Keep build tokens read-only and release writes in a separate job. Use packaged manifest versions for untagged releases.
- Local launchers build the plugin directly in the pinned environment and return its exit status.

## 2026-10-01

- Build only the plugin project in GitHub Actions so test and regression projects do not block production artifacts.

## 2026-03-25 - First-Use UX and DTR Pass

### Added

- Added a first-use guide popup window with quick actions to open the main window and settings
- Added standard DTR bar support with toggle-on-click behavior
- Added DTR bar configuration options for visibility, display mode, and glyphs
- Added a Ko-fi button to the main window
- Added dynamic version display to the main window

### Validation

- Built `Debug x64` successfully from `z:\DhogGPT\DhogGPT.sln`
- Build result: `0 warnings`, `0 errors`
- Confirmed output file: `z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll`
- Release build was intentionally not run in this pass

### Test Request For This Update

- Load `z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll`
- Confirm the first-use guide appears on first load and can reopen from the main window or config window
- Confirm the Ko-fi button opens the support page
- Confirm the DTR entry appears, respects the config settings, and toggles DhogGPT on or off when clicked

## 2026-03-25 - Planning Bootstrap

### Added

- Created the initial DhogGPT planning workspace documents
- Created the project plan at `z:\xa-xiv-docs\Dhog\DhogGPT\DHOGGPT_PROJECT_PLAN.md`
- Created the knowledge base at `z:\xa-xiv-docs\Dhog\DhogGPT\DHOGGPT_KNOWLEDGE_BASE.md`
- Created the end-user import guide in `how to import plugins.md`
- Created the project `README.md`
- Reserved the existing `DhogGPT/images/icon.png` for the initial plugin setup pass
- Created the local `backups\` folder for future file backups before edits

### Notes

- No plugin code has been written yet
- No debug or release build was run because this update is documentation-only
- No syntax, memory-leak, or release-function checks were applicable yet because there is no code to compile

### Test Request For This Update

- Confirm the plan in `z:\xa-xiv-docs\Dhog\DhogGPT\DHOGGPT_PROJECT_PLAN.md` matches your intended feature set
- Confirm the docs location under `z:\xa-xiv-docs\Dhog\DhogGPT\` is acceptable in place of the unavailable `d:\temp\xa-xiv-docs\Dhog\`
- Confirm `DhogGPT/images/icon.png` is the icon you want us to keep for the first working build
- Confirm the future dev-plugin path in `how to import plugins.md` matches how you want to load the first in-game test build

## 2026-03-25 - Initial Working Plugin Build

### Added

- Created `DhogGPT.sln` and the `DhogGPT` plugin project
- Added `Plugin.cs`, `Configuration.cs`, plugin manifest, and language data
- Implemented a main window and config window
- Implemented inbound chat capture for party, FC, linkshells, cross-world linkshells, say, shout, yell, and tell
- Implemented inbound translation output to `Echo` with duplicate suppression
- Implemented an outbound translate-and-send composer with channel selection
- Implemented a queued translation pipeline with short-term caching and session-health tracking
- Implemented a no-key web translation provider path with Google-style web translation first and LibreTranslate-compatible fallback endpoints second
- Preserved and copied the existing `DhogGPT/images/icon.png` into the debug output

### Validation

- Built `Debug x64` successfully from both the project and the solution
- Build result: `0 warnings`, `0 errors`
- Confirmed output files exist at:
  - `z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll`
  - `z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.json`
  - `z:\DhogGPT\DhogGPT\bin\x64\Debug\images\icon.png`
  - `z:\DhogGPT\DhogGPT\bin\x64\Debug\Data\languages.json`
- Confirmed the Google-style no-key translation endpoint responds from this machine
- Confirmed the previous default public LibreTranslate endpoints are unreliable here, so the runtime now uses them only as fallback
- Outgoing chat send path cleans up the native `Utf8String` after use
- Translation worker and provider objects are disposed on plugin unload
- Release build was intentionally not run in this pass

### Notes

- The current prototype uses a Google-style no-key web endpoint first and only falls back to the configured LibreTranslate-compatible endpoints if needed
- Inbound translation currently prints translated copies to `Echo` rather than replacing original chat lines
- Outbound translation is intentionally explicit through the plugin UI instead of silently intercepting everything typed into chat

### Test Request For This Update

- Add `Z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll` as a dev plugin and enable it
- Run `/dhoggpt` and `/dhoggpt config` to confirm both windows open
- Check `/xllog` immediately after load and after unload for exceptions
- In settings, enable only `Say` and `Party` first and keep inbound `From` on `Autodetect`
- Have another character or friend send one short non-English message in an enabled channel and confirm the translation appears in `Echo`
- In the main window, test `Preview translation` with a short message
- Test `Translate and send` in `Say`
- If that works, repeat with `Party`
- Leave `Tell`, `LS`, and `CWLS` for the second pass unless you already have safe targets ready
