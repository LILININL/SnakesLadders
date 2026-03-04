# Games Folder Convention

แนะนำให้แยกแต่ละเกมเป็นโมดูลของตัวเองใต้ `Games/<GameName>/`:

```text
SnakesLadders/
`- Games/
   `- <GameName>/
      |- Domain/
      `- Services/
```

- `Domain/` เก็บ model/constants ที่เฉพาะเกมนั้น
- `Services/` เก็บ game module/engine ของเกมนั้น
- โมดูลเกมต้อง implement `IGameRoomModule` เพื่อให้ service กลางเรียกใช้งานได้

หมายเหตุ:
- model/contract ที่ใช้ร่วมทุกเกมจะอยู่ใน `SnakesLadders/Domain` และ `SnakesLadders/Contracts`
- ฝั่งเว็บยังใช้หน้า lobby/room กลางร่วมกัน และเก็บ assets เฉพาะเกมที่ `wwwroot/games/<game-key>/...`

สถานะ `gameKey` ปัจจุบัน:
- `snakes-ladders` (เปิดใช้งาน)
- `monopoly` (เปิดใช้งาน)
