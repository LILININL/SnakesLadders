# Games Folder Convention

แต่ละเกมให้แยกโฟลเดอร์ของตัวเองดังนี้

```text
SnakesLadders/
`- Games/
   `- <GameName>/
      |- Domain/
      `- Services/
```

- `Domain/` เก็บ model/enum/options เฉพาะเกมนั้น
- `Services/` เก็บ game engine / board generator logic ของเกมนั้น
- สร้างโมดูลเกมให้ implement `IGameRoomModule` เพื่อให้ service กลางเรียกใช้งานได้

ฝั่งเว็บให้แยกไฟล์ตาม `gameKey` ที่ `wwwroot/games/<game-key>/...` เช่น:

```text
wwwroot/games/snakes-ladders/
|- assets/
|- styles/
`- js/
```

ตอนนี้ `gameKey` ที่รองรับในระบบคือ `snakes-ladders` (กำหนดใน `Domain/GameCatalog.cs`)
