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

## Change Log
- 2026-03-04 17:00 ICT | Initialized progress tracker and round targets. | Files: `SnakesLadders/Games/Monopoly/IMPLEMENTATION_PROGRESS.md` | Risks: large cross-cutting changes pending.
- 2026-03-04 18:25 ICT | Completed full action-pipeline backend + Monopoly phase/economy engine + frontend action/status panels and board cleanup. | Files: `Contracts/GameContracts.cs`, `Services/Abstractions/Interfaces.cs`, `Services/GameRoomService/*`, `Hubs/GameHub.cs`, `Services/Background/TurnTimerBackgroundService.cs`, `Games/Monopoly/Domain/*`, `Games/Monopoly/Services/MonopolyGameRoomModule.cs`, `wwwroot/index.html`, `wwwroot/games/monopoly/js/*`, `wwwroot/games/snakes-ladders/js/*`, `wwwroot/games/snakes-ladders/styles/*` | Risks: auction flow is timer-driven turn-bid (ascending) not free-click concurrent bidding; requires gameplay tuning pass.

## Known Issues
- Auction is ascending but controlled by timed bid turns (`PendingDecisionPlayerId`) instead of free concurrent click race.
- Trade input currently uses comma-separated cell ids (no visual property picker yet).
- Gameplay balancing values (rent/building/event impact) need live tuning pass with players.

## Next Round Plan
1. Run full multiplayer playtest scenarios against the 15 acceptance cases and capture regressions.
2. Refine auction UX to optional free-bid mode if needed.
3. Improve trade UI with selectable property chips and validation hints.
