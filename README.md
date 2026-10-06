# DhogGPT

---

**Help fund my AI overlords' coffee addiction so they can keep generating more plugins instead of taking over the world**

[☕ Support development on Ko-fi](https://ko-fi.com/mcvaxius)

[XA and I have created some Plugins and Guides here at -> aethertek.io](https://aethertek.io/)
### Repo URL:
```
https://aethertek.io/x.json
```

---

Dalamud plugin workspace for the FFXIV chat translation plugin `DhogGPT`.

## Current Status

DhogGPT now has a clean `Debug x64` build.

- DLL: `z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll`
- Solution: `z:\DhogGPT\DhogGPT.sln`
- Commands: `/dhoggpt`, `/dgpt`, and `/dog`

## Current Feature Set

- Regular and ultra compact windows use the approved AethertekUI layouts. The header's `C` checkbox changes UI density separately from the existing ultra compact chat mode.
- UI language and accent preferences are shared by the main, detached, settings and guide windows. English, German, French, Spanish, Italian, Russian, Japanese, Korean, Simplified Chinese, Vietnamese, Brazilian Portuguese, Indonesian, Polish, Turkish and Hindi are available; the palette derives surfaces and borders from the selected accent while retaining chat/status meanings and existing custom chat colors.
- Hindi captions and chat/editor text use a scoped Windows text shaper; DTR and game context menus retain supported English for Hindi. Current Debug x64 compilation and focused Hindi/English native checks pass, including the complete Main, ultra, Settings and Guide Hindi matrix. Managed-host/game/GPU/IME acceptance remains pending.
- Managed Segoe UI fonts merge the host's CJK and symbol fonts. Font loading or missing required UI glyphs are reported explicitly. UI language is separate from the chat's From/To languages; original player names, linkshell names, messages, provider data and command tokens retain their identities.
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
