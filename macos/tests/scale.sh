#!/bin/bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
stage="$(mktemp -d "${TMPDIR:-/tmp}/keycat-scale.XXXXXX")"
domain="local.keycat.tests.scale.$(uuidgen)"
cleanup() {
    defaults delete "$domain" >/dev/null 2>&1 || true
    rm -rf "$stage"
}
trap cleanup EXIT
app="$stage/ScaleTest.app"
contents="$app/Contents"
binary="$contents/MacOS/ScaleTest"
mkdir -p "$contents/MacOS"
cp "$root/Info.plist" "$contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $domain" "$contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleExecutable ScaleTest' "$contents/Info.plist"
ditto --norsrc --noextattr "$root/resources" "$contents/Resources"
cp "$root/../assets/"office-colleague{,-work,-sleep,-hover,-click}.png "$contents/Resources/"
for person in hairclip ribbon headband glasses hoodie beret cap boss shiba; do
    mkdir -p "$contents/Resources/characters/$person"
    cp "$root/../assets/characters/$person/"{idle,work,sleep,hover,click}.png "$contents/Resources/characters/$person/"
done
cp "$root/../assets/characters/styles.tsv" "$contents/Resources/characters/styles.tsv"
xcrun --sdk macosx clang -mmacosx-version-min=11.0 -fobjc-arc -fblocks \
    -Werror=unguarded-availability-new -Werror=implicit-function-declaration \
    -I"$root/../core" \
    "$root/tests/scale.m" "$root/app/MacKeys.m" "$root/app/PetView.m" \
    "$root/app/AutoStart.m" "$root/app/Updater.m" "$root/../core/keycat_core.c" \
    -framework Cocoa -framework ApplicationServices -framework CoreGraphics -o "$binary"
codesign --force --sign - "$app"
for scenario in fresh legacy restored low high invalid nan office office-restored people people-restored apple apple-restored; do
    KEYCAT_NO_AUTO_UPDATE=1 "$binary" "$scenario"
done
