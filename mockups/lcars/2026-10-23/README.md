# DhogGPT LCARS concepts

Approved LCARS design concepts. Implementation is selected for the repositories in Z:\myrepos.txt, with the rollout tracked in Z:\!todo\2026-10-22GOATFUCK0002.md. These images are design references, not runtime or test evidence.

- [Main: regular and compact](01-main-regular-compact.png)
- [Settings, Mini and supporting surfaces](02-settings-mini-and-auxiliary.png)

The second sheet depicts selected existing supporting surfaces. Other source windows retain their established layout and actions under the proposed shared appearance; this is not a claim that every scroll position or conditional state has a separate image. Browser companions retain browser controls. Main/Mini windows are distinct from density variants. Generated text and examples are design material, not runtime evidence.

## Source scope

Source commit: `50755a4a07f1194290532381efa9fbd91770e29e`. Existing dirty paths were inspected by count (0); none are included or changed by this mockup task.

## Source-verified control contract

- `DhogGPT/Windows/MainWindow.cs:230` branches between regular translator and true Ultra compact chat. The regular mockup's density variants share Header → Status → Composer; Ultra compact is a distinct mode, not a smaller screenshot.
- `MainWindow.cs:287` defines Guide, Settings, Status to chat, Ko-fi, Discord, Enabled, DTR Bar, Turn on ultra compact and Krangle. `:1008` composer uses Channel, From, To, Message, Preview translation, Translate and send, Clear; Translated preview is read-only.
- `MainWindow.cs:349` gives Ultra compact Me/Them language choices and K/S/Ko-fi shortcuts. Conversation tabs select destination; its bottom composer has no separate channel dropdown or Send button. `FirstUseGuideWindow.cs:51` explicitly documents that behavior.
- `ConfigWindow.cs:80` defines the exact order **Window appearance, Everyday, Channels, Providers, Advanced**. `:145` owns Everyday toggles; `:293` owns channel translation checkboxes. Providers/endpoint text uses placeholders; no credentials or live endpoint data.
- `FirstUseGuideWindow.cs:49` is a normal nonmodal Welcome To DhogGPT window with Open main window, Open settings, Got it. Parent stays interactive, with no blocking backdrop.

Selected supporting surfaces are native Settings, actual Ultra compact mode and first-use guide. Detached conversations, hidden-channel/recent-DM menus, provider editors and vanilla-chat service retain source layouts and gates. CharacterWindowState is model code, not a separate visible window. No actual outgoing message, translation success or account data is represented.

## Existing UI source inventory

- `DhogGPT/Models/CharacterWindowState.cs`
- `DhogGPT/Services/Chat/VanillaChatWindowService.cs`
- `DhogGPT/Windows/ConfigWindow.cs`
- `DhogGPT/Windows/FirstUseGuideWindow.cs`
- `DhogGPT/Windows/MainWindow.cs`

## Shared appearance contract

Auto and Picker retain their order and values; LCARS is the third option. Preserve native IDs, original actions, saved scope and semantic status colours. The proposed LCARS UI tones default off; the speaker and Settings preference are the same value, with no added volume or pack selector. Krangle obfuscates identifying text. Actual adoption waits for the FrenRider pilot.

Exact generation prompts are in [PROMPTS.md](PROMPTS.md).




