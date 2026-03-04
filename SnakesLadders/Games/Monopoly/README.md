# Monopoly Module

โมดูลเกมเศรษฐีที่ใช้งานได้แล้วผ่าน `IGameRoomModule`

ไฟล์หลัก:
- `Domain/MonopolyDefinitions.cs` ค่าคงที่เกม (เงินตั้งต้น, ช่องคุก, ผ่าน GO)
- `Domain/MonopolyModels.cs` โครงข้อมูลห้องเกมเศรษฐี + template ตาราง 40 ช่อง
- `Services/MonopolyGameRoomModule.cs` logic สร้างห้อง/เริ่มเกม/เดินเทิร์น/จบเกม

mechanics พื้นฐานที่มีแล้ว:
- ซื้อทรัพย์สินอัตโนมัติเมื่อเงินพอ
- จ่ายค่าเช่าให้เจ้าของ
- จ่ายภาษีเข้ากองกลาง Free Parking
- Chance / Community Chest แบบสุ่มเหตุการณ์พื้นฐาน
- Go To Jail / ข้ามตาในคุก
- ล้มละลาย + คืนทรัพย์สินเข้าธนาคาร
- จบเกมแบบ Last Standing หรือหมดรอบแล้วตัดสินจากเงิน
