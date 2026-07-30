#!/usr/bin/env bash
# Ask CI for installable players (APK + Windows). Everyday pushes only run the tests and the
# QC screenshot — see the comment on the `build` job in .github/workflows/build.yml.
#
#   tools/request_build.sh          # build the players from main
#   tools/request_build.sh <ref>    # …from another branch
#
# Prints the run id, which tools/publish_build.sh takes.
set -euo pipefail

REF="${1:-main}"
REPO=suksomsri7/siamdive-xr
GH_TOKEN=$(sed -n 's#https://suksomsri7:\([^@]*\)@github.com#\1#p' ~/.git-credentials)

curl -sS -X POST -H "Authorization: Bearer $GH_TOKEN" \
     -H "Accept: application/vnd.github+json" \
     "https://api.github.com/repos/$REPO/actions/workflows/build.yml/dispatches" \
     -d "{\"ref\":\"$REF\",\"inputs\":{\"players\":\"true\"}}"

echo "dispatched on $REF — waiting for the run id…"
sleep 8
curl -s -H "Authorization: Bearer $GH_TOKEN" \
     "https://api.github.com/repos/$REPO/actions/runs?event=workflow_dispatch&per_page=1" |
  python3 -c "
import json,sys
r = json.load(sys.stdin)['workflow_runs']
if not r: print('no run yet — check https://github.com/$REPO/actions'); raise SystemExit
r = r[0]
print(f\"run {r['id']}  {r['head_sha'][:8]}  {r['status']}\")
print(f\"when it is green:  tools/publish_build.sh {r['id']} <tag>\")
"
