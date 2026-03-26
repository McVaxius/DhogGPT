# Changelog

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
