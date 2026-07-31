#!/usr/bin/env bash
# Archive the Xcode project Unity generated, sign it, and send it to TestFlight.
#
# Runs on the macOS runner only (see the `ios` job in .github/workflows/build.yml).
# Everything it needs comes from the environment so nothing secret is ever written into a
# command line, where it would show up in `ps` and in the runner's own diagnostics:
#
#   ASC_KEY_ID      App Store Connect API key id      (e.g. U9DGP7TVFK)
#   ASC_ISSUER_ID   the account's issuer uuid
#   ASC_KEY_P8      the .p8 file's CONTENTS
#   APPLE_TEAM_ID   the developer team               (e.g. 3DD2VCN6JQ)
#
# Signing is done with the App Store Connect API key rather than certificates checked into a
# repo: the key can be revoked from a web page in ten seconds, a leaked .p12 cannot.
set -euo pipefail

XCODE_DIR="${XCODE_DIR:-build/iOS}"
SCHEME="${SCHEME:-Unity-iPhone}"
ARCHIVE="$PWD/build/DiveMap.xcarchive"
EXPORT_DIR="$PWD/build/ipa"

for v in ASC_KEY_ID ASC_ISSUER_ID ASC_KEY_P8 APPLE_TEAM_ID; do
  if [ -z "${!v:-}" ]; then
    echo "::error::$v is not set — add it under Settings → Secrets → Actions." >&2
    echo "  Nothing about the build is wrong; it simply has no way to sign or upload." >&2
    exit 1
  fi
done

if [ ! -d "$XCODE_DIR" ]; then
  echo "::error::no Xcode project at $XCODE_DIR — the Unity step did not produce one." >&2
  exit 1
fi

# The API key has to be a file on disk in the place Apple's tools look for it.
mkdir -p ~/.appstoreconnect/private_keys
printf '%s' "$ASC_KEY_P8" > ~/.appstoreconnect/private_keys/"AuthKey_${ASC_KEY_ID}.p8"
chmod 600 ~/.appstoreconnect/private_keys/"AuthKey_${ASC_KEY_ID}.p8"

echo "── archiving ─────────────────────────────────────────────"
# -allowProvisioningUpdates lets Xcode create/renew the profile through the API key, which is
# what makes this work without a certificate committed anywhere.
xcodebuild -project "$XCODE_DIR/Unity-iPhone.xcodeproj" \
           -scheme "$SCHEME" \
           -configuration Release \
           -archivePath "$ARCHIVE" \
           -destination 'generic/platform=iOS' \
           -allowProvisioningUpdates \
           -authenticationKeyPath ~/.appstoreconnect/private_keys/"AuthKey_${ASC_KEY_ID}.p8" \
           -authenticationKeyID "$ASC_KEY_ID" \
           -authenticationKeyIssuerID "$ASC_ISSUER_ID" \
           DEVELOPMENT_TEAM="$APPLE_TEAM_ID" \
           CODE_SIGN_STYLE=Automatic \
           archive

echo "── exporting ─────────────────────────────────────────────"
cat > /tmp/export.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>method</key><string>app-store-connect</string>
  <key>teamID</key><string>${APPLE_TEAM_ID}</string>
  <key>uploadSymbols</key><true/>
  <key>signingStyle</key><string>automatic</string>
  <key>destination</key><string>export</string>
</dict>
</plist>
PLIST

xcodebuild -exportArchive \
           -archivePath "$ARCHIVE" \
           -exportPath "$EXPORT_DIR" \
           -exportOptionsPlist /tmp/export.plist \
           -allowProvisioningUpdates \
           -authenticationKeyPath ~/.appstoreconnect/private_keys/"AuthKey_${ASC_KEY_ID}.p8" \
           -authenticationKeyID "$ASC_KEY_ID" \
           -authenticationKeyIssuerID "$ASC_ISSUER_ID"

IPA=$(find "$EXPORT_DIR" -name '*.ipa' | head -1)
if [ -z "$IPA" ]; then
  echo "::error::the export produced no .ipa" >&2
  exit 1
fi
echo "built: $IPA ($(du -h "$IPA" | cut -f1))"

echo "── uploading to TestFlight ───────────────────────────────"
# Validate first. A rejected upload after a 45-minute build is worth catching one step earlier,
# with a message that names the problem instead of a generic failure.
xcrun altool --validate-app -f "$IPA" -t ios \
             --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"

xcrun altool --upload-app -f "$IPA" -t ios \
             --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"

echo
echo "✅ sent to TestFlight. Apple processes the build for a few minutes before it appears;"
echo "   it then shows up in the TestFlight app on any device signed in to the same Apple ID."
