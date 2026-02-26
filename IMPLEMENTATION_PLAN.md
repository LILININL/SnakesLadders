# Snakes & Ladders Online - Implementation Plan & Progress (.NET)

## 0) สถานะอัปเดตล่าสุด (Done)
เอกสารนี้อัปเดตเป็นสถานะจริงของโค้ดปัจจุบัน

สิ่งที่ส่งมอบแล้ว:
- ระบบ Lobby + Room + Ready/Not Ready + Start Game
- ระบบ Resume ห้องเดิมด้วย session เดิม
- สร้างกระดานจาก server ตามกติกา + ขนาดกระดาน custom
- Overflow mode 2 แบบ: `StayPut` และ `BackByOverflowX2`
- กระดานยาวแบบ Paged Board 100 ช่อง/หน้า
- โฟกัสตามผู้เล่นที่ถึงตาอัตโนมัติ
- Beacon ผู้เล่นนอกช่วง + jump hint ข้ามช่วง
- Animation เดิน/ขึ้นบันได/ลงงูแบบ smooth
- ปุ่มทอยและ token overlay ปรับเลเยอร์แล้ว
- ระบบแชตในห้อง + Sidebar ด้านขวา + badge unread
- รองรับผู้เล่นหลุดกลางเกม (offline seat) + auto-roll เร็ว
- เพิ่ม `AutoRollReason` ใน `TurnResult`
- ปรับ timer worker เป็น 250ms และเพิ่มเวลาเผื่อแอนิเมชันก่อนหมดเทิร์นถัดไป
- จบเกมแล้วขึ้นผู้ชนะ + รีเซ็ตห้องกลับสู่รอ Ready

## 0.1) แพลนวันนี้ (26 กุมภาพันธ์ 2026)
โฟกัสหลักของวันนี้:
- Stabilize `turn trigger` ก่อนเริ่มทำระบบไอเท็ม

รายการงานวันนี้:
- [x] กำหนดแนวทาง: ยึด `RoomSnapshot.CurrentTurnPlayerId` + `RoomSnapshot.TurnCounter` เป็น source หลัก
- [x] เพิ่มกลไกกัน trigger ซ้ำจาก event order (`RoomUpdated` / `TurnChanged`)
- [x] เพิ่ม pending turn trigger ระหว่าง animation และ flush หลัง animation จบ
- [x] reset turn-trigger state ให้ครบตอน bind room / start game / finish game / leave room
- [ ] ทดสอบหลายแท็บ + reconnect + auto-roll เพื่อยืนยันว่าไม่หลุดเทิร์น

งานถัดไปหลังจบหัวข้อนี้:
- ออกแบบและเริ่มลงระบบไอเท็มในด่าน (แบบสุ่มบนกระดานและเหยียบแล้วทำงานทันที)

## 0.2) อัปเดตงานวันนี้ (ล่าสุด)
ความคืบหน้า `turn trigger`:
- [x] ฝั่ง client มีระบบ queue/pending ระหว่าง animation แล้ว
- [x] เพิ่มการกัน trigger ซ้ำด้วย `TurnCounter`
- [x] flush turn trigger ทันทีเมื่อ animation จบ
- [x] reset state ที่เกี่ยวข้องตอน bind room / leave room
- [ ] Manual validation เพิ่มเติม (หลายแท็บ + reconnect + auto-roll)

ความคืบหน้าโหมดห้อง:
- [x] เพิ่ม `Classic` และ `Custom` ในเมนูสร้างห้อง
- [x] `Classic` ปิดกฏเสริมทั้งหมดทั้งฝั่ง client และ server
- [x] `Custom` คงพฤติกรรมเดิม (เลือกกฏเสริมเองได้)
- [x] เพิ่ม `Chaos` profile ฝั่ง create room + normalize ฝั่ง server

ความคืบหน้าระบบไอเท็ม (ล่าสุด):
- [x] เปลี่ยนแนวทางเป็น `spawn บนกระดาน + เหยียบแล้วใช้ทันที` (ไม่มี inventory / ไม่มีปุ่มกดใช้)
- [x] เพิ่ม snapshot ข้อมูลไอเท็มบนกระดาน + กับดัก + jump ชั่วคราว
- [x] เชื่อม UI ให้เห็นไอเท็มในช่อง + hover ดูความสามารถ
- [x] ลงเอฟเฟกต์ไอเท็มครบตามลิสต์ Chaos รอบแรก
- [ ] ปรับบาลานซ์ค่าตัวเลข (spawn rate, ความแรงแต่ละไอเท็ม) จาก playtest

---

## 1) เป้าหมายโปรเจกต์
- เกมบันไดงูออนไลน์แบบ real-time เล่นกับเพื่อนในห้องเดียวกัน
- เซิร์ฟเวอร์เป็นผู้คุมกติกาทั้งหมด (server authoritative)
- รองรับกระดานยาวและการตั้งค่ากติกาแบบยืดหยุ่น

## 2) Requirement ที่ยืนยันและทำแล้ว
- เล่นหลายคนในห้องเดียวกัน
- โฮสต์กำหนดขนาดกระดานเองได้
- ขนาดกระดานขั้นต่ำ 50 ช่อง (technical cap 5000)
- เซิร์ฟเวอร์สุ่มงู/บันไดให้ทั้งห้องใช้ร่วมกัน
- โหมดความหนาแน่นงู/บันได (น้อย/กลาง/เยอะ)
- โหมดทอยเกินเส้นชัยให้เลือก
- ผู้เล่นที่ไม่ใช่โฮสต์ต้อง ready ครบก่อนเริ่ม

## 3) กติกาหลัก (Current)
- เริ่มที่ช่อง 1
- ทอย 1-6 ต่อเทิร์น
- งู: หัว -> หาง
- บันได: ต้น -> ปลาย
- ชนะเมื่อถึงเส้นชัย หรือชนะตาม Round Limit

### Overflow Mode
- `StayPut`
- `BackByOverflowX2`

## 4) Rule Options (Current)
เปิด/ปิดได้จากหน้า Create Room:
- CheckpointShield
- ComebackBoost
- SnakeFrenzy
- MercyLadder
- TurnTimer
- RoundLimit
- MarathonSpeedup

หมายเหตุ:
- LuckyReroll และ ForkPath ยังอยู่ใน domain model แต่ปิดจาก UI ปัจจุบัน

## 5) งานที่ทำเพิ่มด้านกระดานยาว (Paged Board)
- แสดงผลคงที่ 10x10 ต่อหน้า (100 ช่อง)
- เลขช่องในกระดานเป็น absolute ตามช่วงจริง
- เปลี่ยนช่วงหน้าแบบ smooth 550ms
- โฟกัสตามคนที่ถึงตาอัตโนมัติ
- ผู้เล่นนอกช่วงแสดงผ่าน beacon และกดพาไปดูได้
- Jump ข้ามช่วงแสดง hint ก่อนสลับหน้า

ไฟล์หลัก:
- `wwwroot/js/sn-board-page.js`
- `wwwroot/js/sn-board-focus.js`
- `wwwroot/js/sn-board-beacon.js`
- `wwwroot/js/sn-render-game.js`
- `wwwroot/js/sn-turn-animation.js`

## 6) งานที่ทำเพิ่มด้าน Offline/Timer
- ผู้เล่นหลุดกลางเกมคงที่นั่งไว้เป็น Offline
- ถึงเทิร์น offline auto-roll ภายใน ~700ms
- `ProcessExpiredTurns` ใช้ deadline เป็นหลัก
- Worker polling ทุก 250ms
- เพิ่ม `TurnResult.AutoRollReason` (`Disconnected`/`TimerExpired`)
- เพิ่ม animation buffer ให้ turn deadline คนถัดไป เพื่อกันหมดเวลาเพราะแอนิเมชัน

ไฟล์หลัก:
- `Services/GameRoomService.cs`
- `Services/GameEngine.cs`
- `Services/TurnTimerBackgroundService.cs`
- `Domain/GameModels.cs`

## 7) งานที่ทำเพิ่มด้าน Board Balance
ปรับสมดุลบันไดสำหรับกระดานยาว (`>= 200`):
- จำกัดระยะไต่ปกติ (`~28%` ของกระดาน)
- จำกัดระยะไต่ epic (`~35%` ของกระดาน)
- early zone cap / late zone cap
- epic ladder chance ประมาณ 10%

ไฟล์:
- `Services/BoardGenerator.cs`

## 8) งานที่ทำเพิ่มด้าน UI/UX
- งู/บันได render ชัดขึ้น
- token layer อยู่บนสุดเหนือช่อง/งู/บันได
- ปุ่มทอยยก z-index ให้ไม่โดน token ทับ
- แสดงผลแต้มเต๋ากลางจอก่อนเดิน
- แจ้งเตือน 5 วิสุดท้าย
- ผู้ชนะขึ้น overlay กลางจอ
- แชตเป็น sidebar ขวา + เปิดค้างตั้งต้น + unread badge

ไฟล์หลัก:
- `wwwroot/styles.css`
- `wwwroot/index.html`
- `wwwroot/js/sn-room-ui.js`
- `wwwroot/js/sn-render-chat.js`

## 9) สถานะตามเฟส
- Phase 1 Core Engine: ✅ Done
- Phase 2 Realtime Room: ✅ Done
- Phase 3 Basic Client: ✅ Done
- Phase 4 UX/Hardening รอบที่ทำแล้ว: ✅ Done (ในขอบเขตที่คุย)

## 10) Test / Validation ที่รันล่าสุด
- `dotnet build SnakesLadders.sln` ผ่าน
- syntax check JS (`node --check`) ผ่าน
- ทดสอบ flow หลักแบบหลายแท็บได้ในรอบพัฒนา

## 11) งานค้าง / ต่อได้ทันที
- เพิ่มหน้าเลือกตัวละคร (avatar/gif/webp/lottie) ก่อนเข้าห้อง
- ปรับระบบ asset ตัวละครให้โหลดเบา (แนะนำ webp sprite/lottie มากกว่า gif)
- ถ้าต้องการ scale จริงจัง: ย้าย state จาก in-memory ไป Redis/DB

## 12) แผนระบบไอเท็ม (Current)
เป้าหมาย:
- เพิ่มจังหวะพลิกเกมโดยไม่ทำให้ยืดเยื้อหรือสุ่มเกินควบคุม
- รักษาแนวคิด server authoritative เหมือนระบบหลัก

หลักการบาลานซ์:
- ไม่มีระบบถือไอเท็ม: ไอเท็มสุ่มขึ้นบนกระดานและทำงานทันทีเมื่อเหยียบ
- ห้ามเกิดช่องต้นเกม (1-2) และหลบช่องพิเศษอื่น (งู/บันได/fork/ตำแหน่งผู้เล่น)
- ไอเท็ม/กับดัก/งูชั่วคราวต้องอยู่ใน snapshot เพื่อให้ client แสดงผลตรงกับ server

ชุดไอเท็มที่ลงไว้แล้ว:
- `Rocket Boots` (+2 ช่องทันที)
- `Magnet Dice` (สุ่ม +1/-1 ทันที)
- `Snake Repellent` (กันงูครั้งถัดไป)
- `Ladder Hack` (ขึ้นบันไดครั้งถัดไปแล้วพุ่งเพิ่ม)
- `Banana Peel` (วางกับดักให้คนอื่นเหยียบแล้วถอย)
- `Swap Glove` (สลับกับคนที่อยู่อันดับเหนือกว่า)
- `Anchor` (กันโดนสลับ/ผลักถอยช่วงสั้น)
- `Chaos Button` (สุ่ม event ทั้งห้อง)
- `Snake Row` (เสกงูเป็นแถว มีช่องรอด)
- `Bridge to Leader` (พุ่งไปตำแหน่งผู้นำ)
- `Global Snake Round` (เพิ่มงูชั่วคราวทั้งห้อง 1 รอบ)

หมายเหตุ:
- ชุดนี้เป็น implementation รอบแรก (functional baseline)
- ค่าบาลานซ์ (แรง/โอกาส/ระยะเวลาผล) ยังต้องจูนจาก playtest

## 13) งานถัดไปของไอเท็ม
- จูน spawn density และอัตราเกิดไอเท็มแต่ละชนิดตามจำนวนผู้เล่น
- ทำ balancing pass สำหรับ `Chaos Button / Snake Row / Global Snake Round`
- เก็บ telemetry พื้นฐาน (จำนวนไอเท็มที่ถูกเหยียบ / อัตราพลิกเกม / ระยะเวลาเกมเฉลี่ย)
