#!/usr/bin/env bash
# Makes the Linux release (dist/WarioRecomp-<version>-linux.tar.gz): the prebuilt launcher and translator plus the
# source and build scripts. It contains NOTHING from the game: every user builds the port on their own PC from the
# launcher (Install page), which runs tools/build-wario.sh. Run tools/build-launcher.sh first.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$root/version.json" | head -n 1)}"
launcher="$root/launcher-avalonia/bin/out-linux/WarioLauncher"
[[ -x "$launcher" ]] || { echo "Build the launcher first (tools/build-launcher.sh)." >&2; exit 1; }
dist="$root/dist"
stage="$dist/WarioRecomp-$version-linux"
rm -rf "$stage"
mkdir -p "$stage"

# Source folders, without build outputs or downloads.
copy_tree() {  # copy_tree <from> <to>
    mkdir -p "$2"
    (cd "$1" && find . -type d \( -name bin -o -name obj -o -name Dependencies -o -name artifacts -o -name .git -o -name __pycache__ -o -name node_modules \) -prune -o \
        -type f ! -name '*.dol' ! -name '*.iso' ! -name '*.wbfs' ! -name '*.ciso' ! -name '*.rvz' ! -name '*.nand' ! -name '*.zip' -print0) |
        (cd "$1" && xargs -0 -r cp --parents -t "$2")
}
for dir in runtime aurora projects tools launcher-avalonia local-assets screenshots; do
    [[ -d "$root/$dir" ]] && copy_tree "$root/$dir" "$stage/$dir"
done
mkdir -p "$stage/fw"
cp "$root/fw/LICENSE" "$root/fw/THIRD-PARTY-NOTICES.md" "$stage/fw/"
cp "$root/fw/THIRD-PARTY-NOTICES.md" "$stage/THIRD-PARTY-NOTICES.md"
for f in README.md LICENSE version.json; do [[ -f "$root/$f" ]] && cp "$root/$f" "$stage/$f"; done
cp "$launcher" "$stage/WarioLauncher"
chmod +x "$stage/WarioLauncher" "$stage/tools/"*.sh

# Self-contained translator (one file): users don't need the .NET SDK to translate.
echo "Publishing the translator..."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet publish "$root/fw/translator/src/Translator.Cli/Translator.Cli.csproj" -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None \
    -o "$stage/toolchain/translator-linux" > /dev/null
[[ -x "$stage/toolchain/translator-linux/Translator.Cli" ]] || { echo "Could not publish the translator." >&2; exit 1; }

# nodtool to read the disc image.
mkdir -p "$stage/Play/Tools"
curl -fsSL -o "$stage/Play/Tools/nodtool" "https://github.com/encounter/nod/releases/download/v2.0.0-alpha.10/nodtool-linux-x86_64"
chmod +x "$stage/Play/Tools/nodtool"

# Check: nothing from the game inside.
bad="$(find "$stage" -type f \( -name main.dol -o -name WLS.bin -o -name WarioRecomp -o -path '*/generated/*' \) | head -n 5)"
[[ -z "$bad" ]] || { echo "The release would include game files: $bad" >&2; exit 1; }

echo "Compressing..."
tar -C "$dist" -czf "$stage.tar.gz" "$(basename "$stage")"
echo "Release ready: $stage.tar.gz ($(du -h "$stage.tar.gz" | cut -f1))"
# The launcher also goes as its own release file: launchers update themselves from it.
cp -f "$launcher" "$dist/WarioLauncher-linux-x64"
echo "Upload too: $dist/WarioLauncher-linux-x64"
