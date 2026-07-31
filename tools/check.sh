#!/usr/bin/env bash
# ตรวจ C# ในเครื่องก่อน push — กัน CI แดงเพราะ compile error บรรทัดเดียว (~15 นาที/รอบ)
#
# ตรวจอะไร:  syntax ทุกไฟล์ (Roslyn parser) · ตัวแปร local ชื่อชนกัน (CS0136/CS0128)
#            · using ที่ชี้ไป namespace ที่ไม่มีจริง
# ตรวจไม่ได้: type/method ที่ไม่มีจริง (ไม่มี UnityEngine.dll บนเครื่องนี้) — อันนั้นต้องรอ CI
set -euo pipefail
cd "$(dirname "$0")/.."
BIN=tools/csharp-check/bin/Release/net8.0/csharp-check.dll
[ -f "$BIN" ] || dotnet build tools/csharp-check -c Release -v q --nologo >/dev/null
dotnet "$BIN" "${1:-DiveMap/Assets}"
