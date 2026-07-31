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
#   APPLE_DIST_P12_BASE64 / APPLE_DIST_P12_PASSWORD   the distribution certificate + its key
#
# The API key handles everything that can be re-issued (profiles): it can be revoked from a web
# page in ten seconds, a leaked .p12 cannot. The certificate is the one thing the key CANNOT
# create for us — see the keychain section below for why it has to be carried in by hand.
set -euo pipefail

XCODE_DIR="${XCODE_DIR:-build/iOS}"
SCHEME="${SCHEME:-Unity-iPhone}"
ARCHIVE="$PWD/build/DiveMap.xcarchive"
EXPORT_DIR="$PWD/build/ipa"

for v in ASC_KEY_ID ASC_ISSUER_ID ASC_KEY_P8 APPLE_TEAM_ID APPLE_DIST_P12_BASE64 APPLE_DIST_P12_PASSWORD; do
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

echo "── keychain ──────────────────────────────────────────────"
# Why the certificate is carried in instead of created on the runner:
#
# -allowProvisioningUpdates lets Xcode mint a PROFILE through the API key, but a profile is only
# a pointer — it has to name a certificate the team already owns, and Apple caps a team at TWO
# iOS Distribution certificates. This team is already at 2/2 (they belong to SIAMDIVE, Coach and
# SHARK, all built through EAS). So "let Xcode create one" is not available at any price short of
# revoking a certificate that three shipping apps sign with. A fresh runner starts with an empty
# keychain, so the existing certificate has to be imported here or the archive dies with
# "No signing certificate 'iOS Distribution' found" — a different sentence for the same wall.
KEYCHAIN="$HOME/Library/Keychains/divemap-ci.keychain-db"
KC_PASS="$(uuidgen)"
P12="$(mktemp -t distp12)"

cleanup() {
  rm -f "$P12"
  security delete-keychain "$KEYCHAIN" 2>/dev/null || true
}
trap cleanup EXIT

printf '%s' "$APPLE_DIST_P12_BASE64" | base64 --decode > "$P12"

security create-keychain -p "$KC_PASS" "$KEYCHAIN"
# Default is a 5-minute auto-lock. An IL2CPP archive takes longer than that, and a keychain that
# locks halfway through fails at the signing step with an error that says nothing about locking.
security set-keychain-settings -lut 21600 "$KEYCHAIN"
security unlock-keychain -p "$KC_PASS" "$KEYCHAIN"
security default-keychain -s "$KEYCHAIN"
security list-keychains -d user -s "$KEYCHAIN" $(security list-keychains -d user | tr -d '"')

security import "$P12" -k "$KEYCHAIN" -P "$APPLE_DIST_P12_PASSWORD" \
                -T /usr/bin/codesign -T /usr/bin/security -f pkcs12
# Without this, codesign is treated as an untrusted app asking for the key and macOS raises a
# password dialog. On a headless runner nobody answers it and the job sits there until timeout.
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$KC_PASS" "$KEYCHAIN" >/dev/null

echo "identities now visible to codesign:"
security find-identity -v -p codesigning "$KEYCHAIN"

# Read the identity out of the certificate instead of hard-coding a name. Apple renamed this class
# from "iPhone Distribution" to "Apple Distribution" partway through; which one is on the cert
# depends on the year it was issued, and guessing wrong reads as "certificate not found" even
# though it is sitting right there in the keychain. Same rule as the Xcode project path: find it.
SIGN_IDENTITY=$(security find-identity -v -p codesigning "$KEYCHAIN" \
                | sed -nE 's/.*"((Apple|iPhone) Distribution):.*/\1/p' | head -1)
if [ -z "$SIGN_IDENTITY" ]; then
  echo "::error::the .p12 imported but holds no distribution identity — see the list above." >&2
  echo "  A development certificate cannot sign a TestFlight build." >&2
  exit 1
fi
echo "signing as: $SIGN_IDENTITY"

echo "── archiving ─────────────────────────────────────────────"
# -allowProvisioningUpdates lets Xcode create/renew the profile through the API key, which is
# what makes this work without a certificate committed anywhere.
#
# CODE_SIGN_IDENTITY is not optional here. Unity generates the project with automatic signing
# turned OFF (CIBuild.BuildIos), so every target carries CODE_SIGN_IDENTITY = "iPhone Developer".
# Handing xcodebuild only CODE_SIGN_STYLE=Automatic flips the STYLE and leaves the IDENTITY alone,
# so Xcode dutifully asks Apple for a *development* profile — and those are minted from the team's
# list of registered devices, which a CI-only team does not have:
#
#   error: Your team has no devices from which to generate a provisioning profile
#   error: No profiles for 'com.siamdive.divemap' were found ... iOS App Development
#
# The message names devices, so it reads like "go register your iPhone" — it is not. TestFlight
# never wants a development profile; it wants App Store distribution, which needs no devices at
# all. Naming the identity is what asks for the right kind. The empty specifier clears any profile
# name Unity may have written, which would otherwise pin Xcode to a profile that does not exist.
# Command-line build settings apply to all three targets (Unity-iPhone, UnityFramework,
# GameAssembly) — setting them per target in the pbxproj would be undone by the next Unity build.
xcodebuild -project "$XCODE_DIR/Unity-iPhone.xcodeproj" \
           -scheme "$SCHEME" \
           -configuration Release \
           -archivePath "$ARCHIVE" \
           -destination 'generic/platform=iOS' \
           -allowProvisioningUpdates \
           -authenticationKeyPath ~/.appstoreconnect/private_keys/"AuthKey_${ASC_KEY_ID}.p8" \
           -authenticationKeyID "$ASC_KEY_ID" \
           -authenticationKeyIssuerID "$ASC_ISSUER_ID" \
           OTHER_CODE_SIGN_FLAGS="--keychain $KEYCHAIN" \
           DEVELOPMENT_TEAM="$APPLE_TEAM_ID" \
           CODE_SIGN_STYLE=Automatic \
           CODE_SIGN_IDENTITY="$SIGN_IDENTITY" \
           PROVISIONING_PROFILE_SPECIFIER="" \
           PROVISIONING_PROFILE="" \
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
