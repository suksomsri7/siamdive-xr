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
[ -f "$BIN" ] || dotnet build tools/core-test -c Release -v q --nologo >/dev/null
dotnet build tools/core-test -c Release -v q --nologo >/dev/null
dotnet "$BIN" --noresult "$@"
