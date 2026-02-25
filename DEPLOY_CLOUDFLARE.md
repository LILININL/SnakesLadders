# Cloudflare Deploy (whylin.xyz)

เอกสารนี้ตั้งค่าตามโดเมนของคุณแล้ว:

- Root domain: `whylin.xyz`
- App subdomain: `snakkes.whylin.xyz`

## 1) Prerequisites

1. โดเมน `whylin.xyz` ต้องอยู่บน Cloudflare (nameserver ชี้เข้า Cloudflare แล้ว)
2. มีเครื่องรันแอป .NET (VPS/Cloud VM/เครื่องที่เปิดทิ้ง)
3. โปรเจกต์รันได้ที่ origin เช่น `http://127.0.0.1:8080`

## 2) Run app on origin

แก้ `compose.yaml` ให้เปิดพอร์ต:

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

รัน:

```bash
docker compose up -d --build
curl http://127.0.0.1:8080/health
```

## 3) Install cloudflared

macOS (Homebrew):

```bash
brew install cloudflared
```

Ubuntu/Debian:

```bash
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg | sudo gpg --dearmor -o /usr/share/keyrings/cloudflare-main.gpg
echo 'deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main' | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt-get update && sudo apt-get install -y cloudflared
```

## 4) Create tunnel + bind DNS

```bash
cloudflared tunnel login
cloudflared tunnel create snakesladders
cloudflared tunnel route dns snakesladders snakkes.whylin.xyz
```

หลัง `create` จะได้ `TUNNEL_UUID`

## 5) Configure tunnel

สร้าง `~/.cloudflared/config.yml`:

```yaml
tunnel: <TUNNEL_UUID>
credentials-file: /home/<user>/.cloudflared/<TUNNEL_UUID>.json

ingress:
  - hostname: snakkes.whylin.xyz
    service: http://127.0.0.1:8080
  - service: http_status:404
```

ถ้าเป็น macOS ให้ path เป็น:

- `/Users/<user>/.cloudflared/<TUNNEL_UUID>.json`

## 6) Start tunnel

ทดสอบรัน foreground:

```bash
cloudflared tunnel run snakesladders
```

จากอีก terminal:

```bash
curl https://snakkes.whylin.xyz/health
```

ถ้าได้ `{"status":"ok"...}` แปลว่าใช้งานได้

## 7) Run as service (recommended)

Linux:

```bash
sudo cloudflared service install
sudo systemctl enable --now cloudflared
sudo systemctl status cloudflared
```

## 8) Cloudflare dashboard checks

1. DNS record `snakkes.whylin.xyz` ต้องเป็น `Proxied` (เมฆสีส้ม)
2. SSL/TLS mode แนะนำ `Full` หรือ `Full (strict)`
3. WebSocket ต้องเปิด (ปกติเปิดอยู่แล้ว) สำหรับ SignalR

## 9) Wrangler note

ถ้าจะใช้ `wrangler` เพิ่มเติม ให้ใช้ Node ตามที่คุณกำหนด:

```bash
source ~/.nvm/nvm.sh
nvm use 24.9.0
```

## 10) Common Tunnel Errors

### Error 1033 (Cloudflare Tunnel error: unable to resolve)

ความหมาย: host ชี้ไป tunnel แล้ว แต่ฝั่ง Cloudflare ยังไม่เจอ connector ที่ active

เช็กตามนี้:

```bash
cloudflared tunnel list
cloudflared tunnel info snakesladders
```

ถ้าไม่มี active connector ให้รัน tunnel:

```bash
cloudflared tunnel run snakesladders
```

### Error 502 จาก `snakkes.whylin.xyz`

ความหมาย: tunnel ต่อ edge ได้แล้ว แต่ต่อ origin service ไม่ผ่าน หรือ tunnel หยุดทำงาน

เช็กตามนี้:

```bash
curl http://127.0.0.1:8080/health
cloudflared tunnel info snakesladders
```

ถ้า local health ผ่าน แต่โดเมนยัง 502 ให้เช็กไฟล์ `~/.cloudflared/config.yml`:

```yaml
tunnel: a67f8170-8e76-4190-b7db-5b07762c6061
credentials-file: /Users/lilin/.cloudflared/a67f8170-8e76-4190-b7db-5b07762c6061.json
ingress:
  - hostname: snakkes.whylin.xyz
    service: http://127.0.0.1:8080
  - service: http_status:404
```

และ bind DNS ซ้ำแบบปลอดภัย:

```bash
cloudflared tunnel route dns snakesladders snakkes.whylin.xyz
```
