#!/usr/bin/env bash
# Builds the launcher (launcher-avalonia, Avalonia on .NET 10) for Linux as one self-contained executable
# (no .NET to install) into launcher-avalonia/bin/out-linux and copies it to Play/WarioLauncher.
# Needs the .NET 10 SDK. Log: build-logs/launcher.log
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$root/build-logs" "$root/Play"
log="$root/build-logs/launcher.log"
command -v dotnet > /dev/null || { echo "dotnet not found: install the .NET 10 SDK" >&2; exit 1; }
dotnet --list-sdks | grep -qE '^(1[0-9]|[2-9][0-9])\.' || { echo "The launcher needs the .NET 10 SDK" >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
arch=x64; [[ "$(uname -m)" == "aarch64" ]] && arch=arm64
out="$root/launcher-avalonia/bin/out-linux"
if ! dotnet publish "$root/launcher-avalonia/WarioLauncher.csproj" -c Release -r "linux-$arch" --self-contained true \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
        -o "$out" > "$log" 2>&1; then
    tail -n 30 "$log"
    echo "The launcher did not build, see build-logs/launcher.log" >&2
    exit 1
fi
cp -f "$out/WarioLauncher" "$root/Play/WarioLauncher"
chmod +x "$root/Play/WarioLauncher"
for b in launcher_banner.jpg launcher_banner.png; do
    [[ -f "$root/local-assets/$b" ]] && { mkdir -p "$root/Play/UserData"; cp -f "$root/local-assets/$b" "$root/Play/UserData/$b"; }
done
echo "Launcher ready: $root/Play/WarioLauncher"
