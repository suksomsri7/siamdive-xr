#!/usr/bin/env bash
# Put a real app icon into the asset catalog Unity generated.
#
# Apple rejects the upload outright without a 1024×1024 icon, and the rejection arrives from
# altool AFTER a full IL2CPP build, an archive and a sign — the most expensive place to learn it:
#   Validation failed (409) Missing app icon. Include a large app icon as a 1024 by 1024 pixel
#   PNG in the asset catalog of apps built for iOS or iPadOS.
#
# Unity writes AppIcon.appiconset with its own placeholder icons and no 1024 entry at all
# (Player Settings has no icons configured — m_BuildTargetPlatformIcons is empty), so this fills
# every slot from one source image and adds the marketing entry Apple asks for. Doing it here
# rather than through PlayerSettings keeps it out of Unity's iOS-module APIs, which are absent on
# the Linux image the tests run on.
#
# The source PNG has NO alpha channel on purpose. Apple refuses icons that carry one — even a
# fully opaque one — with "can't be transparent nor contain an alpha channel", so branding/ holds
# an RGB file and sips preserves that on every resize.
set -euo pipefail

SRC="${1:-branding/AppIcon1024.png}"
CATALOG="${2:?usage: ios_app_icon.sh <source.png> <AppIcon.appiconset dir>}"

[ -f "$SRC" ] || { echo "::error::no icon source at $SRC" >&2; exit 1; }
[ -d "$CATALOG" ] || { echo "::error::no asset catalog at $CATALOG — Unity did not generate one" >&2; exit 1; }

python3 - "$SRC" "$CATALOG" <<'PY'
import json, os, subprocess, sys

src, catalog = sys.argv[1], sys.argv[2]
contents = os.path.join(catalog, "Contents.json")
data = json.load(open(contents))
images = data.get("images", [])

def pixels(entry):
    side = float(entry["size"].split("x")[0])
    return int(round(side * float(entry.get("scale", "1x").rstrip("x"))))

# Every slot gets the same artwork, downscaled. sips ships with macOS, so nothing is installed.
for entry in images:
    name = entry.get("filename")
    if not name:
        continue
    px = pixels(entry)
    subprocess.run(["sips", "-z", str(px), str(px), src, "--out", os.path.join(catalog, name)],
                   check=True, stdout=subprocess.DEVNULL)

# The one Apple blocks the upload over. Unity never emits it because it is a store asset rather
# than something the device displays.
MARKETING = "Icon-Marketing-1024.png"
if not any(i.get("idiom") == "ios-marketing" for i in images):
    images.append({"filename": MARKETING, "idiom": "ios-marketing", "scale": "1x", "size": "1024x1024"})
    data["images"] = images
    json.dump(data, open(contents, "w"), indent=2)
marketing = next(i for i in images if i.get("idiom") == "ios-marketing")
subprocess.run(["cp", src, os.path.join(catalog, marketing.get("filename", MARKETING))], check=True)

print(f"icons written: {len(images)} entries, marketing = {marketing.get('filename')}")
PY

# Verify rather than trust: a silently missing icon costs a whole round to discover.
python3 - "$CATALOG" <<'PY'
import json, os, sys
catalog = sys.argv[1]
images = json.load(open(os.path.join(catalog, "Contents.json")))["images"]
missing = [i["filename"] for i in images
           if i.get("filename") and not os.path.exists(os.path.join(catalog, i["filename"]))]
big = [i for i in images if i.get("idiom") == "ios-marketing"]
if missing or not big:
    print(f"::error::asset catalog incomplete — missing files {missing}, marketing entry: {bool(big)}")
    raise SystemExit(1)
print("asset catalog verified:", ", ".join(sorted(os.listdir(catalog))[:4]), "...")
PY
