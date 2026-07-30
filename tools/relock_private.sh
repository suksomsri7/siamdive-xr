#!/usr/bin/env bash
# siamdive-xr ถูกเปิด public ชั่วคราว 30 ก.ค. 2026 เพราะโควตา GitHub Actions หมด
# (private repo = นาทีถูกนับ, public = ฟรีไม่จำกัด) และบัตรตัดไม่ผ่าน
#
# สคริปต์นี้สลับกลับเป็น private ให้อัตโนมัติ ตั้ง cron ไว้ 1 ส.ค. 2026 ตอนที่โควตารีเซ็ต
# รันมือได้ทุกเมื่อ:  bash tools/relock_private.sh
set -euo pipefail

REPO=suksomsri7/siamdive-xr
GH_TOKEN=$(sed -n 's#https://suksomsri7:\([^@]*\)@github.com#\1#p' ~/.git-credentials)
API="https://api.github.com/repos/$REPO"

# ดูก่อนว่ามีใคร fork ไปหรือเปล่า ระหว่างที่เปิด public — ข้อมูลนี้ย้อนไม่ได้ ต้องรู้ไว้
forks=$(curl -s -H "Authorization: Bearer $GH_TOKEN" "$API" |
        python3 -c "import sys,json;print(json.load(sys.stdin).get('forks_count',-1))")

curl -sS -X PATCH -H "Authorization: Bearer $GH_TOKEN" \
     -H "Accept: application/vnd.github+json" "$API" -d '{"private":true}' >/dev/null

now=$(curl -s -H "Authorization: Bearer $GH_TOKEN" "$API" |
      python3 -c "import sys,json;print(json.load(sys.stdin).get('visibility','?'))")

msg="🔒 siamdive-xr กลับเป็น $now แล้ว (fork ที่เกิดระหว่างเปิด public: $forks)"
echo "$msg"
tg "$msg" 2>/dev/null || true

# ทำงานครั้งเดียวพอ — ถอด cron ตัวเองออก
crontab -l 2>/dev/null | grep -v relock_private.sh | crontab - || true
