## Objective
Implement Monopoly "Classic Economy" end-to-end with action-based turns, full economy flows, deterministic events, and UI panels for actions/status while keeping Snakes & Ladders backward-compatible.

## Locked Decisions
- Scope: Full Classic Economy
- Free Parking: Standard No Bonus
- Auction: Realtime Ascending
- Building Rule: Official Strict (even build)
- Trade Flow: Direct Offer/Accept
- Dice: 2 Dice Official (double + triple doubles to jail)
- Decision Timer: Enabled with safe auto defaults
- Building Supply: Official limited supply (houses=32, hotels=12)
- Chance/Community: Deterministic event table (no card UI)
- Action Timing: Only current turn (except targeted trade-response step)
- Progress Format: Checklist + Change Log

## Milestones
- [x] M1 Contracts + Service API + Hub scaffolding
- [x] M2 Monopoly phase machine + jail/dice/landing refactor
- [x] M3 Purchase/decline + auction realtime + timeout defaults
- [x] M4 Build/hotel + supply limits + mortgage/unmortgage
- [x] M5 Trade offer/accept + bankruptcy flows
- [x] M6 Chance/Community deterministic events
- [x] M7 UI action panel + player economy status + auction/trade UI
- [x] M8 Integration hardening + balancing + documentation finalization

## Current Round
- Completed Target 1: M1 API contracts/service/hub wiring with generic action dispatch.
- Completed Target 2: M2-M6 backend Monopoly engine rewrite with phase/action pipeline and classic economy core rules.
- Completed Target 3: M7 frontend action/status/auction-trade panel integration and GameActionApplied handling.
- Completed Target 4: Localized Monopoly board/property naming to Thai province theme and unified currency display/logging to Thai Baht.
- Completed Target 5: Refined in-game UX for action clarity (conditional buttons + recommendations), dual-dice visual rollout, top-layer popup stacking, and larger Monopoly board readability.
- Completed Target 6: Removed repeated avatar image refetch during movement by reusing token/avatar DOM and skipping player-list rerender while animating.
- Completed Target 7: Reduced repeated avatar image requests in waiting room by caching ready UI/player list render output and skipping unnecessary DOM `<img>` rebuild.
- Completed Target 8: Fixed Monopoly board bottom clipping by relaxing forced responsive board height and restoring Monopoly board overflow scrolling.
- Completed Target 9: Added clear ownership symbol on purchased properties (owner marker badge + owner initials chip) so players can instantly identify who owns each asset.
- Completed Target 10: Added clear in-game event message for Chance/Community outcomes so players immediately see what happened after landing on event cells.
- Completed Target 11: Improved purchase-decision clarity when funds are insufficient (explicit shortfall message + recommendation + only valid action shown).

## Change Log
- 2026-03-04 17:00 ICT | Initialized progress tracker and round targets. | Files: `SnakesLadders/Games/Monopoly/IMPLEMENTATION_PROGRESS.md` | Risks: large cross-cutting changes pending.
- 2026-03-04 18:25 ICT | Completed full action-pipeline backend + Monopoly phase/economy engine + frontend action/status panels and board cleanup. | Files: `Contracts/GameContracts.cs`, `Services/Abstractions/Interfaces.cs`, `Services/GameRoomService/*`, `Hubs/GameHub.cs`, `Services/Background/TurnTimerBackgroundService.cs`, `Games/Monopoly/Domain/*`, `Games/Monopoly/Services/MonopolyGameRoomModule.cs`, `wwwroot/index.html`, `wwwroot/games/monopoly/js/*`, `wwwroot/games/snakes-ladders/js/*`, `wwwroot/games/snakes-ladders/styles/*` | Risks: auction flow is timer-driven turn-bid (ascending) not free-click concurrent bidding; requires gameplay tuning pass.
- 2026-03-04 19:05 ICT | Refactor UX per feedback: moved Monopoly action to center-board popup, simplified property management into step flow, converted action/status labels to Thai, and enforced 45-second timeout for manage phases before auto end-turn. | Files: `Services/GameRoomService/GameRoomService.Internal.cs`, `wwwroot/index.html`, `wwwroot/games/monopoly/js/monopoly-action-panel.js`, `wwwroot/games/monopoly/js/monopoly-status-panel.js`, `wwwroot/games/monopoly/js/monopoly-board-renderer.js`, `wwwroot/games/snakes-ladders/js/core/sn-state.js`, `wwwroot/games/snakes-ladders/js/app/sn-main.js`, `wwwroot/games/snakes-ladders/js/app/sn-actions.js`, `wwwroot/games/snakes-ladders/styles/02-room-ui.css`, `wwwroot/games/snakes-ladders/styles/05-responsive-theme-start.css` | Risks: popup flow still needs real-player UX tuning for very small mobile screens.
- 2026-03-04 19:35 ICT | Re-themed Monopoly board names to Thailand provinces/landmarks and changed all Monopoly currency output to Thai Baht (`฿`) across board, player list, helper formatter, and backend action/event logs. | Files: `Games/Monopoly/Domain/MonopolyModels.cs`, `Games/Monopoly/Services/MonopolyGameRoomModule.cs`, `wwwroot/games/monopoly/js/monopoly-helpers.js`, `wwwroot/games/monopoly/js/monopoly-board-renderer.js`, `wwwroot/games/monopoly/js/monopoly-action-panel.js`, `wwwroot/games/monopoly/js/monopoly-status-panel.js`, `wwwroot/games/snakes-ladders/js/render/sn-render-game.js` | Risks: numeric economy scale still follows existing game balance values (only currency notation/theme changed).
- 2026-03-04 20:05 ICT | Improved Monopoly UX consistency with current theme: action popup restyled + elevated to top layer, manage menu now shows only valid actions with context guidance, dice roll FX supports two dice (no text card in dual-dice mode), action popup suppressed during animation so movement resolves before next event action, and board cells/fonts enlarged for readability. | Files: `wwwroot/games/monopoly/js/monopoly-action-panel.js`, `wwwroot/games/snakes-ladders/js/board/sn-turn-animation.js`, `wwwroot/games/snakes-ladders/js/board/sn-board-fx.js`, `wwwroot/games/snakes-ladders/styles/02-room-ui.css`, `wwwroot/games/snakes-ladders/styles/04-overlays-chat-base.css`, `wwwroot/games/snakes-ladders/styles/05-responsive-theme-start.css`, `wwwroot/games/snakes-ladders/styles/06-theme-board.css` | Risks: front-end validation for “ทำได้ตอนนี้” mirrors backend rules closely but still remains UI-side guidance (server remains source of truth).
- 2026-03-04 20:30 ICT | Fixed repeated avatar GET requests while pieces move (both games): board token layer now reuses token/image elements, transit token no longer recreates avatar image each segment, and player list rendering is paused during animation to avoid rebuilding avatar `<img>` every frame. | Files: `wwwroot/games/snakes-ladders/js/board/sn-board-token-layer.js`, `wwwroot/games/snakes-ladders/js/board/sn-piece-transit.js`, `wwwroot/games/snakes-ladders/js/render/sn-render-main.js` | Risks: player panel now updates after animation completes (intentional UX/perf tradeoff).
- 2026-03-04 20:45 ICT | Fixed repeated avatar GET requests in waiting room before start: ready list and avatar picker now use signature-based render cache (skip `innerHTML` rebuild when data unchanged), and room player list uses HTML cache guard to avoid recreating avatar `<img>` on frequent render cycles. | Files: `wwwroot/games/snakes-ladders/js/ui/sn-ready-ui.js`, `wwwroot/games/snakes-ladders/js/render/sn-render-game.js` | Risks: cache signature must include all visual fields; if new UI fields are added later, update signature accordingly.
- 2026-03-04 21:00 ICT | Fixed Monopoly board clipping at bottom: Monopoly board now keeps its own scroll behavior and responsive landscape rules no longer force hard `min-height` that compressed/cut lower board details. | Files: `wwwroot/games/snakes-ladders/styles/02-room-ui.css`, `wwwroot/games/snakes-ladders/styles/05-responsive-theme-start.css`, `wwwroot/games/snakes-ladders/styles/07-board-paging-beacons.css` | Risks: on very short landscape screens, Monopoly board may require internal scroll to view all cells (intentional over clipping).
- 2026-03-04 21:10 ICT | Added ownership marker UX on Monopoly board: purchased cells now show a compact owner badge (top-right) and owner-marker chip beside owner name so players can quickly see “ใครซื้อไว้” without reading long labels. | Files: `wwwroot/games/monopoly/js/monopoly-board-renderer.js`, `wwwroot/games/snakes-ladders/styles/02-room-ui.css`, `wwwroot/games/snakes-ladders/styles/05-responsive-theme-start.css` | Risks: marker abbreviations use 1-2 chars; with many similar names players should still rely on hover tooltip for full owner name.
- 2026-03-04 21:15 ICT | Added explicit event feedback when landing on Chance/Community: action popup now shows “เหตุการณ์ที่เกิดขึ้น” card from latest turn logs, and realtime headline selection prefers event outcome lines (instead of dice-only lines) for clearer player feedback. | Files: `wwwroot/games/monopoly/js/monopoly-action-panel.js`, `wwwroot/games/snakes-ladders/js/net/sn-signalr.js`, `wwwroot/games/snakes-ladders/styles/02-room-ui.css`, `wwwroot/games/snakes-ladders/styles/05-responsive-theme-start.css` | Risks: event card currently appears only to the decision owner (intentional to avoid non-turn popup noise).
- 2026-03-04 21:20 ICT | Improved insufficient-funds UX on property purchase: backend landing log now states shortfall and recommendation, purchase popup now shows price vs current cash, highlights shortfall, and hides buy button when cash is not enough (showing only actionable “ไม่ซื้อ/เปิดประมูล”). | Files: `Games/Monopoly/Services/MonopolyGameRoomModule.cs`, `wwwroot/games/monopoly/js/monopoly-action-panel.js` | Risks: players still need one extra click to decline purchase before auction (not auto-skip by design in this round).

## Known Issues
- Auction is ascending but controlled by timed bid turns (`PendingDecisionPlayerId`) instead of free concurrent click race.
- Trade input currently uses comma-separated cell ids (no visual property picker yet).
- Gameplay balancing values (rent/building/event impact) need live tuning pass with players.

## Next Round Plan
1. Run full multiplayer playtest scenarios against the 15 acceptance cases and capture regressions.
2. Refine auction UX to optional free-bid mode if needed.
3. Optimize mobile popup flow (compact step pages + larger hit targets on small screens).
