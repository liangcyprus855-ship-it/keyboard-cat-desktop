#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
    echo "请在安装了 Xcode 命令行工具的 macOS 上运行此脚本。" >&2
    exit 1
fi

root="$(cd "$(dirname "$0")" && pwd)"
output="$root/build"
mkdir -p "$output"
stage="$(mktemp -d "$output/.keycat-build.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
export COPYFILE_DISABLE=1

plist="$root/Info.plist"
version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$plist")"
identifier="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$plist")"
minimum="$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$plist")"
app_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleName' "$plist").app"
executable="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$plist")"
package_name="键盘小黑猫-${version}-macOS通用安装包.pkg"
app="$stage/payload/$app_name"
contents="$app/Contents"
binary="$contents/MacOS/$executable"

sdk="$(xcrun --sdk macosx --show-sdk-path)"
mkdir -p "$contents/MacOS" "$stage/components"
for arch in arm64 x86_64; do
    xcrun --sdk macosx clang -arch "$arch" -isysroot "$sdk" \
        "-mmacosx-version-min=$minimum" -fobjc-arc -fblocks -O2 -g0 \
        -Werror=unguarded-availability-new -Werror=implicit-function-declaration \
        -I"$root/../core" "$root/app/"*.m "$root/../core/keycat_core.c" \
        -framework Cocoa -framework ApplicationServices \
        -framework CoreGraphics -framework CoreVideo -o "$stage/$arch"
done
xcrun lipo -create "$stage/arm64" "$stage/x86_64" -output "$binary"
xcrun lipo "$binary" -verify_arch arm64 x86_64

cp "$plist" "$contents/Info.plist"
printf 'APPL????' > "$contents/PkgInfo"
ditto --norsrc --noextattr "$root/resources" "$contents/Resources"
cp "$root/../assets/"office-colleague{,-work,-sleep,-hover,-click}.png "$contents/Resources/"
for person in hairclip ribbon headband glasses hoodie beret cap boss shiba; do
    mkdir -p "$contents/Resources/characters/$person"
    cp "$root/../assets/characters/$person/"{idle,work,sleep,hover,click}.png "$contents/Resources/characters/$person/"
done
cp "$root/../assets/characters/styles.tsv" "$contents/Resources/characters/styles.tsv"
installer_options=()
if [[ -n "${KEYCAT_APP_SIGN_IDENTITY:-}" && -n "${KEYCAT_INSTALLER_SIGN_IDENTITY:-}" ]]; then
    codesign --force --options runtime --timestamp --sign "$KEYCAT_APP_SIGN_IDENTITY" "$app"
    installer_options=(--sign "$KEYCAT_INSTALLER_SIGN_IDENTITY")
elif [[ -z "${KEYCAT_APP_SIGN_IDENTITY:-}" && -z "${KEYCAT_INSTALLER_SIGN_IDENTITY:-}" ]]; then
    codesign --force --sign - "$app"
else
    echo "Set both Developer ID identities for a distribution build." >&2
    exit 1
fi
codesign --verify --strict --all-architectures "$app"

pkgbuild --root "$stage/payload" --install-location /Applications \
    --component-plist "$root/installer/components.plist" \
    --identifier "$identifier.pkg" --version "$version" "$stage/components/Keycat.pkg"
productbuild ${installer_options[@]+"${installer_options[@]}"} --distribution "$root/installer/distribution.xml" \
    --resources "$root/installer/resources" --package-path "$stage/components" \
    "$stage/$package_name"

if [[ -n "${KEYCAT_NOTARY_PROFILE:-}" ]]; then
    if [[ -z "${KEYCAT_INSTALLER_SIGN_IDENTITY:-}" ]]; then
        echo "Notarization requires Developer ID signing." >&2
        exit 1
    fi
    xcrun notarytool submit "$stage/$package_name" --keychain-profile "$KEYCAT_NOTARY_PROFILE" --wait
    xcrun stapler staple "$stage/$package_name"
    xcrun stapler validate "$stage/$package_name"
fi

if [[ -e "$output/$app_name" ]]; then
    mv "$output/$app_name" "$stage/previous.app"
fi
mv "$app" "$output/$app_name"
mv -f "$stage/$package_name" "$output/$package_name"
printf '%s\n' "$output/$app_name" "$output/$package_name"
