# Snakes & Ladders Online - Implementation Plan (.NET)

## 1) เป้าหมายโปรเจกต์
- ทำเกมบันไดงูแบบออนไลน์ เล่นพร้อมเพื่อนในห้องเดียวกันแบบ real-time
- ใช้ `.NET` ฝั่งเซิร์ฟเวอร์เป็นตัวคุมเกมทั้งหมด (server authoritative) เพื่อกันโกง
- รองรับกระดานหลายขนาด (เช่น 100, 200, 300, ... 1000 ช่อง)

## 2) Requirement ที่ยืนยันแล้ว
- เล่นหลายคนในห้องเดียวกัน
- โฮสต์กำหนดขนาดกระดานเองได้ (custom size)
- ขนาดกระดานต้องไม่ต่ำกว่า 50 ช่อง
- เซิร์ฟเวอร์สุ่มตำแหน่งงู/บันไดให้ทั้งห้องใช้ร่วมกัน
- งูและบันไดทำงานตามกติกาทั่วไป:
  - ลงที่ต้นบันได -> ขึ้นปลายบันได
  - ลงที่หัวงู -> ลงหางงู
  - ลงที่หางงูแล้วไม่ย้อนขึ้นหัวงู
- มีโหมดความหนาแน่นงู (งูน้อย/กลาง/งูเยอะ) ตามขนาดกระดาน
- มีกติกาโหมดทอยเกินช่องสุดท้ายให้เลือกเปิด/ปิด

## 3) กติกาหลักของเกม (Game Rules)
- จุดเริ่มทุกคนอยู่ช่อง 1
- เทิร์นละ 1 ผู้เล่น ทอยเต๋า 1-6
- ชนะเมื่อถึงช่องสุดท้ายพอดี หรือเมื่อจบการคำนวณแล้วอยู่ช่องสุดท้าย

### 3.0 กติกาเพิ่มความสนุก (เปิด/ปิดได้)
กำหนดทุกข้อเป็น toggle ใน `RuleOptions`:
- `CheckpointShield`: ทุก 50 ช่อง รับโล่ 1 อัน (กันผลงูได้ 1 ครั้ง)
- `ComebackBoost`: ผู้เล่นที่อยู่อันดับท้ายสุดได้บูสต์เต๋า `+1` (เพดานไม่เกิน 6)
- `LuckyReroll`: ผู้เล่นมีสิทธิ์กด reroll จำกัดครั้งต่อเกม (ค่าเริ่มต้น 2)
- `ForkPath`: ช่องพิเศษที่ให้เลือกเส้นทาง `Safe` หรือ `Risky`
- `SnakeFrenzy`: ทุก N เทิร์น (ค่าเริ่มต้น 5) มีงูชั่วคราวเพิ่ม 1 ตัวในเทิร์นนั้น
- `MercyLadder`: ถ้าผู้เล่นโดนงู 2 เทิร์นติด เทิร์นถัดไปได้บันไดช่วย
- `TurnTimer`: จำกัดเวลาเทิร์น (เช่น 15 วินาที) หมดเวลาระบบทอยให้อัตโนมัติ
- `RoundLimit`: จำกัดจำนวนรอบสูงสุด ถ้าเกินให้ตัดสินจากตำแหน่งใกล้เส้นชัยสุด
- `MarathonSpeedup`: กระดานยาว (เช่น 300+ หรือ 500+) เพิ่มสัดส่วนบันได

### 3.1 กติกาทอยเกินช่องสุดท้าย (Overflow Mode)
กำหนดเป็น enum ที่โฮสต์เลือกตอนสร้างห้อง:

1. `StayPut` (ค่าเริ่มต้น)
- ถ้าทอยเกินช่องสุดท้าย -> อยู่ตำแหน่งเดิม
2.  `BackByOverflowX2` (โหมดที่ขอเพิ่ม)
- คำนวณ `overflow = (currentPosition + dice) - lastCell`
- ถ้า `overflow > 0` ให้ถอยหลัง `overflow * 2` จากตำแหน่งปัจจุบัน
- สูตร: `newPosition = max(1, currentPosition - (overflow * 2))`

ตัวอย่าง (lastCell=100):
- อยู่ 98 ทอย 5 -> overflow=3 -> ถอย 6 -> ไป 92

> หมายเหตุ: สูตรนี้เป็นตามคำขอ "ถอยหลังเท่าจำนวนที่เกิน * 2" โดยตีความว่าถอยจากตำแหน่งปัจจุบัน หากต้องการตีความแบบเด้งจากเส้นชัย (bounce) ค่อยเพิ่ม mode แยกได้ภายหลัง

## 4) การสุ่มด่าน (Server Board Generation)
### 4.1 ตัวเลือกด่าน
- `BoardSizePreset` (optional): `100`, `200`, `300`, `500`, `1000`
- `BoardSizeCustom` (optional): host กรอกเลขเองได้
- เงื่อนไข:
  - ต้องระบุขนาดกระดานออกมาสุดท้ายเป็นค่าเดียว (`BoardSize`)
  - `BoardSize >= 50`
  - ไม่บังคับ limit ฝั่ง UX แต่มี technical cap ฝั่งระบบ (ค่าเริ่มต้น 5000) เพื่อความเสถียร
- `DensityMode`:
  - `Low`
  - `Medium`
  - `High`
- `OverflowMode`:
  - `StayPut`
  - `BackByOverflowX2`
- `Seed` (optional) สำหรับ replay/debug
- `RuleOptions` (toggle รายข้อทั้งหมดใน 3.0)

### 4.2 จำนวนงู/บันไดตามโหมด (base ต่อ 100 ช่อง)
- `Low`: งู 4, บันได 5
- `Medium`: งู 6, บันได 7
- `High`: งู 8, บันได 9

สูตรคำนวณจำนวนจริง:
- `factor = ceil(BoardSize / 100.0)`
- `snakeCount = baseSnake * factor`
- `ladderCount = baseLadder * factor`

### 4.3 เงื่อนไขสุ่มที่ต้องบังคับ
- ห้ามใช้งู/บันไดที่เกี่ยวข้องกับช่อง 1 และช่องสุดท้าย
- งู: `head > tail`
- บันได: `start < end`
- ห้ามมีจุดเริ่มซ้ำ (`from` ซ้ำกันไม่ได้)
- ห้ามปลายงู/ปลายบันไดชนจุดเริ่มของรายการอื่น (ลด chain ที่ซับซ้อน)
- ถ้าสุ่มแล้วติดเงื่อนไขซ้ำ ให้สุ่มใหม่จนกว่าจะครบ หรือครบ max attempts

### 4.4 Validation ตอนสร้างห้อง
- ถ้า `BoardSize < 50` ให้ reject พร้อมข้อความ error ชัดเจน
- ถ้า `BoardSize > TechnicalCap` ให้ reject พร้อมข้อความและค่า cap ปัจจุบัน
- ถ้าไม่ได้ส่งขนาดกระดานมา ให้ fallback เป็น `100`

## 5) สถาปัตยกรรมที่แนะนำ
- Backend: `ASP.NET Core` + `SignalR`
- In-memory game state (MVP)
- โครงแยกชัดเจน:
  - `Domain`: กติกา/เอนจินเกม
  - `Application`: use-cases เช่น create room, join, roll dice
  - `Transport`: SignalR Hub + DTO

## 6) ข้อมูลหลัก (Data Model)
- `RoomState`
  - `RoomCode`
  - `HostConnectionId`
  - `Players`
  - `GameStatus` (`Waiting`, `Started`, `Finished`)
  - `BoardConfig`
  - `BoardJumps` (งู/บันได)
  - `CurrentTurnPlayerId`
- `PlayerState`
  - `PlayerId`
  - `DisplayName`
  - `Position`
  - `Connected`
- `Jump`
  - `From`
  - `To`
  - `Type` (`Snake`, `Ladder`)

## 7) SignalR Contract (MVP)
Client -> Server:
- `CreateRoom(CreateRoomRequest)`
- `JoinRoom(JoinRoomRequest)`
- `StartGame()`
- `RollDice()`
- `LeaveRoom()`

Server -> Client:
- `RoomCreated(RoomSnapshot)`
- `PlayerJoined(PlayerState)`
- `GameStarted(GameSnapshot)`
- `DiceRolled(TurnResult)`
- `TurnChanged(playerId)`
- `GameFinished(winner)`
- `Error(message)`

## 8) ลำดับการคำนวณต่อ 1 เทิร์น (สำคัญ)
1. ตรวจสิทธิ์ว่าเป็นเทิร์นผู้เล่นนี้จริง
2. apply โบนัสก่อนทอย (เช่น `ComebackBoost`)
3. สุ่มเต๋า (server) และ apply `LuckyReroll` ถ้ามีการใช้สิทธิ์
4. คำนวณตำแหน่งใหม่ตาม `OverflowMode`
5. ถ้าตกช่อง `ForkPath` ให้ resolve ทาง `Safe/Risky`
6. ตรวจว่าตกงูหรือบันไดหรือไม่ แล้ว apply jump (พร้อม logic โล่)
7. ประเมิน `MercyLadder` / `CheckpointShield` / `SnakeFrenzy`
8. เช็คชนะหรือชน `RoundLimit`
9. สลับเทิร์นถ้ายังไม่จบ
10. broadcast snapshot ให้ทุกคนในห้อง

## 9) แผนลงมือทำ (Phase Plan)
### Phase 1 - Core engine
- ทำ enum/config/model ทั้งหมด
- ทำ `BoardGenerator`
- ทำ `GameEngine.ApplyRoll(...)`
- ทำ `RuleEngine` ครบ toggle ใน 3.0
- เขียน unit tests สำหรับกติกาหลักทั้งหมด

### Phase 2 - Real-time room
- เพิ่ม SignalR Hub
- เพิ่ม room manager (in-memory)
- ต่อ flow สร้างห้อง/เข้าห้อง/เริ่ม/ทอย/จบ
- เพิ่ม auto-roll จาก `TurnTimer` (background service)

### Phase 3 - Basic client
- UI ง่ายๆ: lobby + board + turn + roll button
- แสดงงู/บันได และตำแหน่งผู้เล่น

### Phase 4 - Hardening
- reconnect / disconnect handling
- turn timeout
- audit log และ telemetry

## 10) Test Cases ที่ต้องมี
- ทอยปกติไม่เจองู/บันได
- ทอยแล้วขึ้นบันได
- ทอยแล้วโดนหัวงู
- ลงหางงูแล้วไม่ย้อน
- Overflow แบบ `StayPut`
- Overflow แบบ `BackByOverflowX2`
- `CheckpointShield` กันงูได้ถูกต้อง
- `ComebackBoost` มีผลเฉพาะอันดับท้ายสุด
- `LuckyReroll` ใช้สิทธิ์แล้วจำนวนครั้งลดลง
- `ForkPath` เลือก `Safe`/`Risky` แล้วได้ผลต่างกัน
- `SnakeFrenzy` สร้างงูชั่วคราวตาม interval
- `MercyLadder` ทำงานหลังโดนงูติดกัน 2 เทิร์น
- `TurnTimer` หมดเวลาแล้วระบบทอยแทน
- `RoundLimit` ตัดสินผู้ชนะได้ถูกต้อง
- `MarathonSpeedup` เพิ่มบันไดเมื่อกระดานยาวตามเงื่อนไข
- ชนะเกมพอดีช่องสุดท้าย
- หลายผู้เล่นสลับเทิร์นถูกต้อง
- คนไม่ใช่เทิร์นกดทอยแล้วโดน reject
- สร้างห้องด้วย `BoardSize < 50` ต้องโดน reject

## 11) ขอบเขต MVP รอบแรก
- ทำ online room + core rules + กติกาเสริมแบบ toggle ครบ
- ยังไม่ทำระบบ login จริง, matchmaking สาธารณะ, ranking

## 12) จุดที่ต้องคอนเฟิร์มก่อนเริ่มโค้ด
1. `ForkPath` ตอน MVP ให้ผู้เล่นเลือกเองทันทีในคำสั่งทอย หรือ fallback เป็น `Safe`
2. `MarathonSpeedup` ใช้ threshold เท่าไร (แนะนำ 300+)
