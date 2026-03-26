# How To Import DhogGPT

## Current Status

`DhogGPT` has a built dev-plugin DLL and is ready for the first in-game load test.

## Dev Plugin Path

`Z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll`

The manifest and icon are already next to that build output.

## Load DhogGPT As A Dev Plugin

1. Launch FFXIV through XIVLauncher.
2. Open Dalamud settings with `/xlsettings`.
3. Go to `Experimental`.
4. Add `Z:\DhogGPT\DhogGPT\bin\x64\Debug\DhogGPT.dll` to `Dev Plugin Locations`.
5. Open the plugin installer with `/xlplugins`.
6. Go to `Dev Tools > Installed Dev Plugins`.
7. Enable `DhogGPT`.
8. Run `/dhoggpt` or `/dgpt` in game to open the main window.
9. Use `/dhoggpt config` if you want the settings window directly.

## First In-Game Checks

- Confirm `DhogGPT` appears in `Installed Dev Plugins`
- Confirm it enables without immediate exceptions in `/xllog`
- Confirm `/dhoggpt` opens the main window
- Confirm `/dhoggpt config` opens the settings window
- Confirm the icon is present
- Confirm disabling and re-enabling the plugin works cleanly

## First Translation Checks

- In settings, leave inbound `From` as `Autodetect` and `To` as `English`
- Enable only one or two inbound channels first, such as `Say` and `Party`
- Have another character or friend send a short non-English message in an enabled channel
- Confirm the original message stays intact and the translated copy appears in `Echo`
- In the main window, use the outgoing composer to preview a short message
- Test `Translate and send` in `Say` before trying `Party`, `FC`, `LS`, `CWLS`, or `Tell`

## Release Repository Installation

DhogGPT is now published via the custom repository entry below, so you can skip the dev-plugin flow once you're ready to use the release build.

1. Launch FFXIV through XIVLauncher and open /xlplugins.
2. In the installer, use the repository management controls to **Add** a new entry at https://raw.githubusercontent.com/McVaxius/DhogGPT/main/repo.json.
3. After the repo list refreshes, locate **DhogGPT** in the Available Plugins list and install it normally.
4. Enable the plugin and confirm /dhoggpt and /dgpt work in-game just like the dev build.
