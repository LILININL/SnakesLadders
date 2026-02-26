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
- ออกแบบและเริ่มลงระบบไอเท็มในด่าน (เริ่มจาก item แบบรับผลทันที ก่อน item แบบกดใช้)

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
- [ ] `Chaos` รอทำพร้อมระบบไอเท็ม

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

## 12) แผนระบบไอเท็ม (Draft)
เป้าหมาย:
- เพิ่มจังหวะพลิกเกมโดยไม่ทำให้ยืดเยื้อหรือสุ่มเกินควบคุม
- รักษาแนวคิด server authoritative เหมือนระบบหลัก

หลักการบาลานซ์:
- ผู้เล่นถือไอเท็มได้สูงสุด 2 ชิ้น
- เทิร์นละใช้ได้สูงสุด 1 ชิ้น
- ไอเท็มโจมตีผู้เล่นอื่นมี cooldown ระดับห้อง 1 เทิร์น (กันโดนรุม)
- แผนที่ 100 ช่อง: spawn โดยเฉลี่ย 6-8 จุด, กระดานยาวขึ้นให้ scale ตามสัดส่วน

ชุดไอเท็มแนะนำ (มันส์ + อ่านง่าย):
- `Rocket Boots`: ใช้ก่อนทอย ได้ +2 ระยะเดินหลังทอย
- `Magnet Dice`: ใช้ก่อนทอย เลือกปรับค่าเต๋า +1 หรือ -1 ได้ครั้งเดียวหลังเห็นผล
- `Snake Repellent`: กันงูครั้งถัดไป (ไม่กันตกช่องจากเอฟเฟกต์ไอเท็มโจมตี)
- `Ladder Hack`: เปลี่ยนบันไดที่เหยียบให้พุ่งเพิ่มอีก 4 ช่อง
- `Banana Peel`: วางกับดัก 1 เทิร์น คนที่ผ่านช่องนี้เสียระยะ -3
- `Swap Glove`: สลับตำแหน่งกับผู้เล่นที่อยู่ติดอันดับเหนือคุณ 1 คน
- `Anchor`: ปักตัวเอง 1 เทิร์น กันการโดนสลับ/ผลักถอย
- `Chaos Button`: สุ่ม global event เล็ก ๆ (เช่นทุกคน +1 หรือทุกคน -1 แบบ clamp)

จัดกลุ่มเพื่อทำทีละเฟส:
- Utility: `Rocket Boots`, `Magnet Dice`, `Snake Repellent`
- Trap/Attack: `Banana Peel`, `Swap Glove`
- High-impact: `Ladder Hack`, `Anchor`, `Chaos Button`

## 13) ลำดับลงมือทำไอเท็ม
- Phase A: Item แบบรับผลทันที (ไม่มี target) -> ง่ายสุดและกระทบ flow น้อย
- Phase B: Item แบบวางกับดัก/ใส่ target (เพิ่ม request/action ใหม่)
- Phase C: Item แบบ global event + balancing + telemetry
