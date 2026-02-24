# Snakes & Ladders Live

Real-time multiplayer Snakes and Ladders built with ASP.NET Core + SignalR and a modular vanilla JS frontend.

The project supports:
- Lobby with online users and open rooms
- Room system with host + ready check
- Custom board generation (size, density, rules)
- Animated movement (step-by-step, snake/ladder path slide)
- Turn timer, event feed, and room chat
- Winner overlay and automatic room reset back to ready state

---

## 1) Tech Stack

- Backend: ASP.NET Core (`net10.0`)
- Realtime: SignalR (`/hubs/game`)
- Frontend: Static HTML/CSS + modular vanilla JS in `wwwroot/js`
- In-memory state: room/game state kept in singleton services
- Client persistence: `localStorage` for player name + room session resume

---

## 2) Current Gameplay Rules

### Core
- Board size: minimum `50`, technical cap `5000`
- Density: `Low`, `Medium`, `High`
- Overflow modes:
  - `StayPut`: if roll exceeds final cell, player stays in place
  - `BackByOverflowX2`: move back by `overflow * 2`
- Snakes send player down from head to tail
- Ladders move player up from start to end
- Finish-row pressure: generated board enforces at least 2 snakes on the finish row (up to 3 on larger boards)
- No snake/ladder endpoint chaining in generated board

### Ready / Start
- Host is always ready
- Non-host players must be connected and ready before host can start
- Minimum 2 players required

### Enabled room rule switches (UI)
- Checkpoint Shield
- Comeback Boost
- Snake Frenzy
- Mercy Ladder
- Turn Timer
- Round Limit
- Marathon Speedup

### Disabled in current UI flow
- Lucky reroll
- Fork choice

UI sends:
- `useLuckyReroll: false`
- `forkChoice: null`

### End game behavior
- Winner is announced with overlay animation
- Server immediately resets the room to `Waiting`
- Board is cleared
- Host remains ready
- Other connected players return to `Not Ready`
- Disconnected seats are removed during reset

---

## 3) UX Features

- Snake rendering upgraded with stylized body/head/stripe visuals
- Player tokens rendered on a top overlay layer above cells, snakes, and ladders
- Active turn token glow/pulse
- Dice result popup in center before movement starts
- Movement speed tuned slower than earlier builds
- Snake/ladder movement follows the actual path (not per-cell teleport/step)
- Turn warning countdown appears in final 5 seconds
- Winner popup with animation after game finish

---

## 4) Project Structure

```text
SnakesLadders/
|- SnakesLadders.sln
|- IMPLEMENTATION_PLAN.md
|- README.md
|- compose.yaml
`- SnakesLadders/
   |- Program.cs
   |- Hubs/GameHub.cs
   |- Services/
   |  |- GameRoomService.cs
   |  |- GameEngine.cs
   |  |- BoardGenerator.cs
   |  `- TurnTimerBackgroundService.cs
   |- Domain/
   |  |- GameModels.cs
   |  |- GameOptions.cs
   |  `- GameEnums.cs
   |- Contracts/GameContracts.cs
   `- wwwroot/
      |- index.html
      |- styles.css
      `- js/
         |- sn-main.js
         |- sn-signalr.js
         |- sn-actions.js
         |- sn-render-*.js
         |- sn-room-ui.js
         |- sn-ready-ui.js
         |- sn-board-overlay.js
         |- sn-board-token-layer.js
         |- sn-piece-transit.js
         |- sn-turn-animation.js
         `- sn-board-fx.js
```

---

## 5) Run Locally

## Prerequisites
- .NET SDK 10.0

## Commands

```bash
cd /Users/lilin/Desktop/Alumilive/SnakesLadders
dotnet restore
dotnet run --project SnakesLadders/SnakesLadders.csproj --launch-profile http
```

Open:
- `http://localhost:5248`

Alternative profile:
- `https://localhost:7135`

---

## 6) Docker

Current `compose.yaml` builds and runs the service image but does not publish host ports.

If you want browser access from host, add a port mapping in `compose.yaml`, for example:

```yaml
services:
  snakesladders:
    image: snakesladders
    build:
      context: .
      dockerfile: SnakesLadders/Dockerfile
    ports:
      - "8080:8080"
```

Then run:

```bash
docker compose up --build
```

---

## 7) HTTP Endpoints

- `GET /health`
- `GET /rooms/waiting`
- `GET /lobby/online`
- SignalR hub: `/hubs/game`

---

## 8) SignalR Hub Contract (Current)

### Client -> Server methods
- `CreateRoom(CreateRoomRequest)`
- `JoinRoom(JoinRoomRequest)`
- `ResumeRoom(ResumeRoomRequest)`
- `StartGame(StartGameRequest)`
- `RollDice(RollDiceRequest)`
- `SetReady(SetReadyRequest)`
- `SendChat(SendChatRequest)`
- `LeaveRoom(LeaveRoomRequest)`
- `GetRoom(string roomCode)`
- `SetLobbyName(string displayName)`

### Server -> Client events
- `RoomCreated`
- `RoomJoined`
- `RoomResumed`
- `RoomUpdated`
- `GameStarted`
- `TurnChanged`
- `DiceRolled`
- `GameFinished`
- `ChatReceived`
- `Error`

---

## 9) Session and Persistence

Client stores:
- `snl_profile_name`
- `snl_room_sessions`
- `snl_last_room_code`

Behavior:
- Name is auto-filled on next visit
- Last room/session can auto-resume on reconnect/reload
- Chat history is room-realtime only (not restored on page reload by design)

---

## 10) Development Notes

- Game state is in-memory; server restart clears all rooms.
- Lobby polling runs every 8 seconds only when not in a room.
- Turn timer auto-roll is processed by `TurnTimerBackgroundService`.
- Frontend JS is split into small modules for easier maintenance.

---

## 11) Quick Multiplayer Test

1. Open app in two browser windows.
2. Window A: set name, create room, share code.
3. Window B: join by room code.
4. Window B toggles ready.
5. Window A starts game.
6. Roll dice and validate:
   - dice popup appears before movement
   - token moves on top layer
   - snake/ladder movement follows path
   - winner overlay appears on finish
   - room returns to waiting + ready flow for next round

---

## 12) Troubleshooting

### Warning: `Failed to determine the https port for redirect.`
- Run with `--launch-profile http`, or
- Set `ASPNETCORE_URLS` to include an https URL (the app reads it to configure redirect).

### Room does not appear in lobby list
- Lobby list only shows rooms in `Waiting` status.
- Use refresh button if needed.

### Cannot start game
- Need at least 2 players
- All non-host players must be connected and ready

---

## 13) Deploy to Cloudflare

- See deployment playbook: `DEPLOY_CLOUDFLARE.md`
- Preconfigured domain target: `snakkes.whylin.xyz`
