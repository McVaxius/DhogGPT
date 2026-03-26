# DhogGPT

Dalamud plugin workspace for the FFXIV chat translation plugin `DhogGPT`.

## Current Status

DhogGPT now has a clean `Debug x64` build.

- DLL: `z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll`
- Solution: `z:\DhogGPT\DhogGPT.sln`
- Commands: `/dhoggpt` and `/dgpt`

## Current Feature Set

- Real-time inbound translation for selected chat channels, printed back to `Echo`
- Outbound translate-and-send composer for say, party, free company, linkshells, cross-world linkshells, shout, yell, and tell/DM
- `Autodetect` support for source language
- `English` as the default destination language
- Configurable channel toggles and provider endpoints
- First-use guide popup with quick-start actions
- Ko-fi button in the main window
- Standard DTR entry with configurable display modes and glyphs
- Translation queue, duplicate suppression, short-term cache, and recent-history UI

## Current Documents

- Project plan: `z:\xa-xiv-docs\Dhog\DhogGPT\DHOGGPT_PROJECT_PLAN.md`
- Knowledge base: `z:\xa-xiv-docs\Dhog\DhogGPT\DHOGGPT_KNOWLEDGE_BASE.md`
- Import guide: `how to import plugins.md`
- Changelog: `CHANGELOG.md`

## Notes

- The initial icon at `DhogGPT/images/icon.png` is included in the current debug output.
- Non-user-facing research remains in `z:\xa-xiv-docs\Dhog\DhogGPT\`.
- The current translation backend tries a Google-style no-key web endpoint first and then falls back to configurable LibreTranslate-compatible endpoints.
