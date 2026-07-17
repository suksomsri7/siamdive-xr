#!/bin/bash
# Sequential XR-LOD pilot driver. ONE model at a time (2c/3GB VPS — never parallel).
set -u
cd /root/dive3d
OUT=/root/asset-masters/xr_lod
RES=/tmp/xr_results.jsonl
: > "$RES"

# name|source_path
MODELS=(
  "Humpback_whale|/root/asset-masters/marine/Humpback-whale.glb"
  "Black_Manta|/root/asset-masters/marine/Black_Manta.glb"
  "Blacktip_Reef_Shark|/root/asset-masters/marine/Blacktip_Reef_Shark.glb"
  "Silver_Dolphin|/root/asset-masters/marine/Silver_Dolphin.glb"
  "Molamola|/root/asset-masters/marine/Molamola.glb"
  "Golden_Trident|/root/asset-masters/warp_0704/Golden_Trident.glb"
  "Stone_King|/root/asset-masters/warp_0704/Stone_King.glb"
  "Singha_Statue_Underwater|/root/asset-masters/artificial3D/Singha_Statue_Underwater.glb"
  "Eagle_ray|/root/asset-masters/marine/Eagle-ray.glb"
  "Tiger_Shark|/root/asset-masters/marine/Tiger_Shark.glb"
)

i=0
for M in "${MODELS[@]}"; do
  i=$((i+1))
  NAME="${M%%|*}"; SRC="${M##*|}"
  echo ">>> [$i/${#MODELS[@]}] $NAME  ($SRC)"
  if [ ! -f "$SRC" ]; then echo "  !! MISSING SOURCE, skipping"; continue; fi
  LINE=$(node optimize_xr.mjs "$SRC" "$OUT" "$NAME" 2>/tmp/xr_err_$NAME.log | tail -1)
  if [ -n "$LINE" ] && echo "$LINE" | grep -q '"lod0"'; then
    echo "$LINE" >> "$RES"
    echo "  OK: $LINE"
  else
    echo "  FAIL — stderr:"; tail -5 /tmp/xr_err_$NAME.log
  fi
done
echo "=== ALL DONE ($(wc -l < "$RES") models) ==="
