#!/usr/bin/env bash
# รันเทส EditMode ที่เป็น pure logic ด้วย dotnet บนเครื่องนี้ (ไม่มี Unity Editor · CI = ~35 นาที/รอบ)
#
# ครอบ: Core ที่ไม่แตะ UnityEngine + เทสของมัน (ดูลิสต์ใน tools/core-test/core-test.csproj)
# ไม่ครอบ: อะไรที่ต้องมี UnityEngine จริง — อันนั้นยังต้องรอ CI เหมือนเดิม
#
#   bash tools/test.sh              # ทั้งหมด
#   bash tools/test.sh --where "class =~ FishMind"
set -euo pipefail
cd "$(dirname "$0")/.."
BIN=tools/core-test/bin/Release/net8.0/core-test.dll

# 🔴 อย่ากลืน error ของ build. เดิมบรรทัดนี้เป็น `dotnet build … >/dev/null` ซึ่งทิ้ง stdout
# ทั้งก้อน — และ compile error ของ dotnet ออกทาง stdout. ผลคือ build พังแล้ว set -e หยุดสคริปต์
# แบบ "เงียบสนิท": ไม่มีข้อความ ไม่มีสรุปเทส เหมือนรันผ่านแต่ไม่มีอะไรพิมพ์ออกมา
# กติกาของ repo นี้คือ log ต้องพูดตอนที่ "ไม่มีอะไรเกิดขึ้น" ด้วย ไม่งั้นความเงียบแยกไม่ออก
# ระหว่าง "ผ่าน" กับ "ไม่เคยรัน" — gate ที่เงียบตอนพังคือ gate ที่ไม่มีอยู่จริง
if ! dotnet build tools/core-test -c Release -v q --nologo; then
  echo "🔴 test.sh: build ไม่ผ่าน — ยังไม่ได้รันเทสสักตัว" >&2
  exit 1
fi
dotnet "$BIN" --noresult "$@"
