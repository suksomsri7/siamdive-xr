#!/usr/bin/env bash
# Download a finished CI run's player artifacts and publish them for the user.
#
#   tools/publish_build.sh <run-id> <tag>
#
# Puts DiveMap-<tag>-<sha10>.apk / .zip in /var/www/dive3d/dl/ (served at
# dive3d.suksomsri.cloud/dl/) and prints the URLs. The VPS has no `zip`, so the
# Windows artifact is republished as GitHub's own zip and the APK is extracted
# with python's zipfile.
set -euo pipefail

RUN_ID="${1:?usage: publish_build.sh <run-id> <tag>}"
TAG="${2:?usage: publish_build.sh <run-id> <tag>}"
REPO=suksomsri7/siamdive-xr
DL=/var/www/dive3d/dl
GH_TOKEN=$(sed -n 's#https://suksomsri7:\([^@]*\)@github.com#\1#p' ~/.git-credentials)
API="https://api.github.com/repos/$REPO"

sha=$(curl -s -H "Authorization: Bearer $GH_TOKEN" "$API/actions/runs/$RUN_ID" |
      python3 -c "import json,sys;print(json.load(sys.stdin)['head_sha'][:10])")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

curl -s -H "Authorization: Bearer $GH_TOKEN" "$API/actions/runs/$RUN_ID/artifacts" \
  > "$work/arts.json"

get_id() { python3 -c "
import json,sys
name=sys.argv[1]
for a in json.load(open('$work/arts.json'))['artifacts']:
    if a['name']==name: print(a['id']); break
" "$1"; }

apk_id=$(get_id DiveMap-apk)
win_id=$(get_id DiveMap-windows)

if [ -n "${apk_id:-}" ]; then
  curl -sL -H "Authorization: Bearer $GH_TOKEN" -o "$work/apk.zip" \
    "$API/actions/artifacts/$apk_id/zip"
  python3 - "$work/apk.zip" "$work" <<'PY'
import sys, zipfile, os
z = zipfile.ZipFile(sys.argv[1])
apk = [n for n in z.namelist() if n.lower().endswith('.apk')]
if not apk:
    raise SystemExit('no .apk inside DiveMap-apk artifact: ' + str(z.namelist()))
open(os.path.join(sys.argv[2], 'out.apk'), 'wb').write(z.read(apk[0]))
print('extracted', apk[0])
PY
  install -m 644 "$work/out.apk" "$DL/DiveMap-$TAG-$sha.apk"
  echo "https://dive3d.suksomsri.cloud/dl/DiveMap-$TAG-$sha.apk"
fi

if [ -n "${win_id:-}" ]; then
  curl -sL -H "Authorization: Bearer $GH_TOKEN" -o "$DL/DiveMap-win-$TAG-$sha.zip" \
    "$API/actions/artifacts/$win_id/zip"
  echo "https://dive3d.suksomsri.cloud/dl/DiveMap-win-$TAG-$sha.zip"
fi

ls -la "$DL"
