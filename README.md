# Snakes & Ladders Live

เกมบันไดงูออนไลน์แบบเรียลไทม์ (หลายผู้เล่น) ด้วย ASP.NET Core + SignalR และ Frontend แบบ Vanilla JS แยกโมดูล

## สถานะล่าสุด (อัปเดต)
โปรเจกต์ตอนนี้รองรับครบทั้ง flow หลักที่คุยไว้:
- Lobby + ห้อง + ระบบ Ready/Not Ready
- โหมดห้อง `Classic / Custom / Chaos`
- กระดานขนาดยาว (สูงสุด 5000 ช่อง) พร้อมการแสดงผลแบบแบ่งหน้า 100 ช่องต่อหน้า
- โฟกัสกล้องตามคนที่ถึงตาอัตโนมัติ
- งู/บันไดข้ามช่วงพร้อม hint ปลายทาง
- ระบบไอเท็มบนกระดาน (สุ่มเกิดและเหยียบแล้วทำงานทันที)
- ผู้เล่นหลุดระหว่างเกมยังอยู่ในกระดานเป็น Offline และ auto-roll ให้แบบเร็ว
- แชตในห้องแบบ Sidebar ด้านขวา เปิดค้างตั้งต้น และมี badge แจ้งเตือนข้อความใหม่
- ผู้ชนะโชว์ overlay และรีเซ็ตกลับไปสถานะรอ Ready รอบใหม่อัตโนมัติ

## 1) Tech Stack
- Backend: ASP.NET Core (`net10.0`)
- Realtime: SignalR (`/hubs/game`)
- Frontend: HTML/CSS + Modular Vanilla JS (`wwwroot/games/snakes-ladders/js`)
- State: In-memory (ไม่มี DB)
- Persistence ฝั่ง client: `localStorage`

## 2) ฟีเจอร์ที่ทำแล้ว

### 2.1 Lobby / Room
- ตั้งชื่อก่อนใช้งาน (popup)
- แสดงห้องที่เปิดรอและกดเข้าห้องได้
- Host สร้างห้องพร้อมกำหนดกติกา
- Resume เข้าห้องเดิมด้วย session เดิมหลังรีโหลด/หลุด
- ก่อนเริ่มเกม: Host = พร้อมเสมอ, คนอื่นต้องพร้อมครบถึงเริ่มได้

### 2.2 กติกาเกมหลัก
- ขนาดกระดานขั้นต่ำ `50`, เพดานระบบ `5000`
- ความหนาแน่นงู/บันได: Low/Medium/High
- Overflow mode:
  - `StayPut`
  - `BackByOverflowX2`
- งู/บันไดสุ่มโดยเซิร์ฟเวอร์ (server authoritative)
- มีงูโซนเส้นชัยอย่างน้อย 2-3 ตัวเพื่อกันเกมง่ายเกินไป

### 2.3 กติกาเสริม (เปิด/ปิดตอนสร้างห้อง)
- Items Enabled
- Checkpoint Shield
- Comeback Boost
- Snake Frenzy
- Mercy Ladder
- Turn Timer
- Round Limit
- Marathon Speedup

หมายเหตุ:
- Lucky reroll และ Fork path ถูกปิดจาก UI ปัจจุบัน (ฝั่ง client ส่ง `useLuckyReroll=false`, `forkChoice=null`)

### 2.8 ระบบไอเท็ม (Current)
- ไอเท็มสุ่มเกิดบนกระดาน (ไม่เกิดช่อง 1-2)
- ผู้เล่นเหยียบแล้วไอเท็มทำงานทันที (ไม่มี inventory/ไม่มีปุ่มกดใช้)
- แสดงไอเท็มในช่องและ hover เพื่อดูความสามารถ
- ไอเท็มหลักที่มีแล้ว: Rocket Boots, Magnet Dice, Snake Repellent, Ladder Hack, Banana Peel, Swap Glove, Anchor, Chaos Button, Snake Row, Bridge to Leader, Global Snake Round

### 2.4 ประสบการณ์กระดานยาว (Paged Board)
- แสดงกระดานคงที่ 10x10 (100 ช่องต่อหน้า) เสมอ
- ช่องที่เห็นเป็นเลข absolute จริง (เช่น 201-300)
- เปลี่ยนหน้าแบบ smooth transition (550ms)
- โฟกัสตามคนที่ถึงตาอัตโนมัติ
- ถ้าผู้เล่นอยู่นอกช่วงที่เห็น: มี beacon ชี้ตำแหน่งและกดกระโดดไปดูได้

### 2.5 Animation / UI
- เดินทีละช่อง (ไม่วาร์ป)
- ขึ้นบันได/ลงงูด้วย path animation
- ถ้าข้ามช่วงหน้า: โชว์ jump hint ก่อนแล้วค่อยสลับหน้า
- ตัวผู้เล่นอยู่ layer บนสุดและเด่นขึ้น
- ปุ่มทอยอยู่ layer บนสุด (ไม่โดน token ทับ)
- Countdown 5 วิสุดท้ายขึ้นแจ้งเตือน
- แสดงผลแต้มเต๋ากลางจอก่อนเริ่มเดิน

### 2.6 Chat
- แชตในห้องแบบ Sidebar ด้านขวา (เปิด/ปิดได้)
- เข้า room แล้วเปิดแชตค้างเป็นค่าเริ่มต้น
- มี badge แจ้งเตือนข้อความใหม่สีแดงบนปุ่มแชต
- ถ้าเปิดแชตอยู่ จะล้าง unread อัตโนมัติ
- ไม่โหลดแชตเก่าหลังรีโหลด (ตาม requirement)

### 2.7 Offline / Timer / Auto-roll
- ผู้เล่นหลุดระหว่างเกม: คงที่นั่งไว้เป็น Offline (โทนเทา)
- ถึงตาผู้เล่น Offline: auto-roll เร็ว (`~700ms`)
- Worker ประมวลผล deadline ทุก `250ms`
- `TurnResult` มี `AutoRollReason` = `Disconnected` หรือ `TimerExpired`
- มี buffer เวลาแอนิเมชันก่อนจับเวลาหมดเทิร์นของคนถัดไป เพื่อกันโดน auto-roll ทั้งที่ยังไม่ทันกด

## 3) โครงสร้างโปรเจกต์
```text
SnakesLadders/
|- SnakesLadders.sln
|- IMPLEMENTATION_PLAN.md
|- README.md
|- DEPLOY_CLOUDFLARE.md
|- compose.yaml
`- SnakesLadders/
   |- Program.cs
   |- Hubs/GameHub.cs
   |- Contracts/GameContracts.cs
   |- Domain/GameCatalog.cs
   |- Games/
   |  `- SnakesLadders/
   |     |- Domain/
   |     |  |- GameEnums.cs
   |     |  |- GameModels.cs
   |     |  `- GameOptions.cs
   |     `- Services/
   |        |- Abstractions/SnakesLaddersEngineInterfaces.cs
   |        |- Board/BoardGenerator.cs
   |        |- GameEngine/*.cs
   |        `- SnakesLaddersGameRoomModule.cs
   |- Services/
   |  |- Abstractions/Interfaces.cs
   |  |- Background/TurnTimerBackgroundService.cs
   |  `- GameRoomService/*.cs
   `- wwwroot/
      |- index.html
      |- styles.css
      `- games/
         `- snakes-ladders/
            |- assets/
            |- styles/
            `- js/
```

## 4) Run Local
Prerequisites:
- .NET SDK 10
- (แนะนำตอนตรวจ JS) `nvm use 24.9.0`

Commands:
```bash
cd /Users/lilin/Desktop/Alumilive/SnakesLadders
dotnet restore
dotnet run --project SnakesLadders/SnakesLadders.csproj --launch-profile http
```

Open:
- `http://localhost:5248`

## 5) Docker
ตัวอย่าง run:
```bash
docker compose up -d --build
```

ถ้าต้องการเปิดจาก host ให้ map port ใน `compose.yaml` เช่น `8080:8080`

## 6) HTTP Endpoints
- `GET /health`
- `GET /games`
- `GET /rooms/waiting`
- `GET /lobby/online`
- SignalR hub: `/hubs/game`

## 7) SignalR Contract
Client -> Server:
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

Server -> Client:
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

## 8) Client Storage
- `snl_profile_name`
- `snl_room_sessions`
- `snl_last_room_code`
- `snl_focus_mode` (ตอนนี้บังคับใช้งาน turn focus)

## 9) Deployment
- ดูคู่มือ Cloudflare: `DEPLOY_CLOUDFLARE.md`
- โดเมนที่ตั้งค่าไว้: `snakkes.whylin.xyz`

## 10) หมายเหตุสำคัญ
- ระบบเป็น in-memory, restart แล้วห้องหาย
- Build artifacts (`bin/obj`) อาจเปลี่ยนทุกครั้งที่ build
- ถ้าหน้าเว็บไม่อัปเดต ให้ hard refresh หลัง bump `?v=`
