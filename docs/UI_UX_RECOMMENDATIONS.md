# DhogGPT UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

## Product goal

Read translated conversations and send translated messages with minimal friction while keeping language, channel, logging, and provider state clear.

## Reviewed surfaces

- `DhogGPT/Windows/MainWindow.cs`
- `DhogGPT/Windows/ConfigWindow.cs`
- `DhogGPT/Windows/FirstUseGuideWindow.cs`

## What is already working

- Regular and ultra-compact chat surfaces support conversation tabs and an in-window composer.
- Incoming channel selection, provider fallback, translation status, Krangle, DTR, and per-character window state are configurable.
- The first-use guide explains the main mode and endpoint fallback.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Standardize language labels. | Use `I write in` and `Translate to` everywhere; avoid switching between Me/Them and From/To, especially when incoming and outgoing directions differ. |
| P0 | Keep composer state beside the message. | Show Translating, Ready to send, Sent, Retry, and the exact failure inline with the draft so status is not detached in another panel. |
| P0 | Simplify conversation-tab management. | Use an overflow menu for hidden/recent DMs, combine/release, pin, and close; show unread counts and preserve the active composer destination visibly. |
| P1 | Retire deprecated-mode language from normal UI. | Present only Regular and Ultra compact. Put migration notes for Compact/Super Compact in release notes or a one-time migration notice. |
| P1 | Move provider endpoints to Expert settings. | Everyday settings should show provider health and Retry; timeout, endpoint list, logs, history limits, and diagnostics belong under Advanced. |
| P1 | Make logging and privacy choices explicit on first use. | State which messages are stored, where, per-account/character scope, how slash commands are handled, and how Krangle affects display versus stored data. |
| P2 | Teach compact keyboard focus in context. | Show a one-time hint near the composer for `/` and Enter shortcuts and explain that they require the window to be open. |

## Suggested information hierarchy

1. Conversation and destination
2. Language direction
3. Composer and send state
4. Conversation overflow
5. Provider/logging diagnostics

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.
