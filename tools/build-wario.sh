#!/usr/bin/env bash
# WarioRecomp: full Linux build (translation -> compilation -> Play folder), the Linux counterpart of
# tools/build-wario.ps1. Also works in WSL2 (WSLg gives the window and the audio).
#
# Usage:
#   tools/build-wario.sh --disc /path/to/game.iso     import the disc (.iso/.wbfs/.rvz/...) and build
#   tools/build-wario.sh --game-data /path/to/DATA    use an already extracted game (folder with sys/main.dol)
#   tools/build-wario.sh                              reuse the game from Play/UserData/Config.toml
# Options: --jobs N, --skip-translate (only recompile the runtime: the quick rebuild), --lang es|en
#
# Needs: clang, cmake (3.25+), ninja, the .NET 8 SDK and SDL's development libraries; it says what is
# missing and the command to install it. Logs: build-logs/status.txt and build-logs/<step>.log.
# Where they can't be installed (SteamOS on the Steam Deck, other read-only systems) but distrobox is there,
# the build runs by itself in an Ubuntu 24.04 container ("wariorecomp") with everything it needs.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
logs="$root/build-logs"
play="$root/Play"
build="$root/nb-linux"
project="projects/wario/recomp.yml"
orig_args=("$@")
jobs=0; skip_translate=0; game_data=""; disc=""; lang="en"
nod_version="v2.0.0-alpha.10"
main_dol_sha256="053be60dde84c8042f45ed2f4ba80c17efb2d20fedf95a414c2c7e37d336912a"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --jobs) jobs="$2"; shift 2 ;;
        --skip-translate) skip_translate=1; shift ;;
        --game-data) game_data="$2"; shift 2 ;;
        --disc) disc="$2"; shift 2 ;;
        --lang) lang="$2"; shift 2 ;;
        -h|--help) sed -n 2,13p "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

mkdir -p "$logs" "$play/UserData"
[[ -z "${WARIO_IN_BOX:-}" ]] && rm -f "$logs/status.txt"
L() { if [[ "$lang" == "es" ]]; then printf '%s' "$1"; else printf '%s' "$2"; fi; }
status() { local line; line="$(date '+%Y-%m-%d %H:%M:%S') $*"; echo "$line"; echo "$line" >> "$logs/status.txt"; }
fail() { status "ERROR: $*"; exit 1; }
step() {  # step <name> <command...>: runs it with its output in build-logs/<name>.log
    local name="$1"; shift
    status "$name: $(L empezando started)"
    if ! "$@" > "$logs/$name.log" 2>&1; then
        tail -n 40 "$logs/$name.log"
        fail "$name: $(L "fallo, ver build-logs/$name.log" "failed, see build-logs/$name.log")"
    fi
    status "$name: ok"
}

cpus="$(nproc)"
[[ "$jobs" -le 0 ]] && jobs=$(( cpus > 1 ? cpus - 1 : 1 ))
ram_gb=$(( $(awk '/MemTotal/ {print $2}' /proc/meminfo) / 1024 / 1024 ))
# Each translated-code shard needs ~1.5 GB of RAM to compile: limit those jobs by the memory.
shard_jobs=$(( (ram_gb - 4) / 2 )); (( shard_jobs < 1 )) && shard_jobs=1; (( shard_jobs > jobs )) && shard_jobs=$jobs
status "$(L "compilacion iniciada en $root con $jobs procesos ($shard_jobs para codigo traducido, RAM $ram_gb GB)" \
            "build started in $root with $jobs processes ($shard_jobs for translated code, RAM $ram_gb GB)")"

# 1. Tools
missing=()
for tool in clang clang++ cmake ninja; do command -v "$tool" > /dev/null || missing+=("$tool"); done
if [[ $skip_translate -eq 0 ]] && ! command -v dotnet > /dev/null && [[ ! -x "$root/toolchain/translator-linux/Translator.Cli" ]]; then
    missing+=("dotnet (.NET 8 SDK)")
fi
if command -v pkg-config > /dev/null; then
    for pc in x11 xcursor xi xrandr xkbcommon wayland-client egl alsa libpulse libudev dbus-1 sqlite3 libpng freetype2 zlib; do
        pkg-config --exists "$pc" 2> /dev/null || missing+=("$pc (dev)")
    done
else
    missing+=("pkg-config")
fi
apt_packages=(build-essential clang cmake ninja-build pkg-config git curl ca-certificates libx11-dev libxcursor-dev
    libxi-dev libxrandr-dev libxss-dev libxkbcommon-dev libwayland-dev wayland-protocols libegl-dev libdrm-dev
    libgbm-dev libasound2-dev libpulse-dev libudev-dev libdbus-1-dev libxtst-dev libxfixes-dev libpipewire-0.3-dev
    libvulkan-dev libsqlite3-dev libpng-dev libfreetype-dev zlib1g-dev)
[[ -x "$root/toolchain/translator-linux/Translator.Cli" ]] || apt_packages+=(dotnet-sdk-8.0)

# Steam Deck / read-only systems: build in a distrobox container instead (its files are in the same paths).
box="wariorecomp"
if [[ ${#missing[@]} -gt 0 && -z "${WARIO_IN_BOX:-}" ]] && command -v distrobox > /dev/null; then
    status "$(L "faltan en este sistema: ${missing[*]}" "missing on this system: ${missing[*]}")"
    status "$(L "se compila dentro de un contenedor distrobox ($box, Ubuntu 24.04); la primera vez descarga ~1 GB" \
                "building inside a distrobox container ($box, Ubuntu 24.04); the first time it downloads ~1 GB")"
    if ! distrobox list 2> /dev/null | grep -qw "$box"; then
        step box-create distrobox create --yes --name "$box" --image docker.io/library/ubuntu:24.04
    fi
    step box-packages distrobox enter "$box" -- sudo env DEBIAN_FRONTEND=noninteractive \
        sh -c "apt-get update && apt-get install -y ${apt_packages[*]} mesa-vulkan-drivers libvulkan1"
    set +e
    distrobox enter "$box" -- env WARIO_IN_BOX=1 bash "$root/tools/build-wario.sh" "${orig_args[@]}"
    exit $?
fi
if [[ ${#missing[@]} -gt 0 ]]; then
    echo
    echo "$(L "Faltan: " "Missing: ")${missing[*]}"
    echo "$(L "En Ubuntu/Debian se instalan con:" "On Ubuntu/Debian install them with:")"
    echo "  sudo apt install ${apt_packages[*]}"
    echo "$(L "(Steam Deck u otro sistema de solo lectura: instala distrobox y se compila solo en un contenedor)" \
              "(Steam Deck or another read-only system: with distrobox installed the build uses a container by itself)")"
    fail "$(L "faltan: " "missing: ")${missing[*]}"
fi
status "$(L "herramientas listas" "tools ready")"

# 2. Game: the disc is extracted with nodtool (encounter/nod, MIT) to <disc folder>/DATA
cfg="$play/UserData/Config.toml"
if [[ -n "$disc" ]]; then
    [[ -f "$disc" ]] || fail "$(L "no existe el disco $disc" "disc not found: $disc")"
    nod="$play/Tools/nodtool"
    if [[ ! -x "$nod" ]]; then
        mkdir -p "$play/Tools"
        status "$(L "nodtool: descargando" "nodtool: downloading") $nod_version"
        curl -fL --retry 3 -o "$nod" "https://github.com/encounter/nod/releases/download/$nod_version/nodtool-linux-x86_64" \
            || fail "$(L "no se pudo descargar nodtool" "could not download nodtool")"
        chmod +x "$nod"
    fi
    game_data="$(cd "$(dirname "$disc")" && pwd)/DATA"
    if [[ ! -f "$game_data/sys/main.dol" ]]; then
        step extract "$nod" extract "$disc" "$game_data" -q
    fi
fi
if [[ -z "$game_data" && -f "$cfg" ]]; then
    game_data="$(sed -n 's/^[[:space:]]*dvd_root[[:space:]]*=[[:space:]]*"\(.*\)"/\1/p' "$cfg" | head -n 1)"
fi
dol="$root/game/main.dol"
if [[ ! -f "$dol" ]]; then
    [[ -n "$game_data" && -f "$game_data/sys/main.dol" ]] || fail "$(L "no se encuentra el juego: usa --disc o --game-data" "game not found: use --disc or --game-data")"
    mkdir -p "$root/game"; cp "$game_data/sys/main.dol" "$dol"
fi
[[ "$(sha256sum "$dol" | cut -d' ' -f1)" == "$main_dol_sha256" ]] || fail "$(L "main.dol no es RWLE01 (SHA-256 distinto)" "main.dol is not RWLE01 (SHA-256 mismatch)")"
if [[ -n "$game_data" ]]; then
    game_data="$(cd "$game_data" && pwd)"
    if [[ ! -f "$cfg" ]]; then
        printf '[paths]\ndvd_root = "%s"\n' "$game_data" > "$cfg"
    elif grep -q '^[[:space:]]*dvd_root' "$cfg"; then
        sed -i "s|^[[:space:]]*dvd_root[[:space:]]*=.*|dvd_root = \"$game_data\"|" "$cfg"
    fi
fi

# 3. Translation (a translation made on Windows has Windows paths in its shard list: always redo the shard list)
tr=()
translator() {  # sets tr=(command...) to run the translator, building it the first time
    [[ ${#tr[@]} -gt 0 ]] && return
    if [[ -x "$root/toolchain/translator-linux/Translator.Cli" ]]; then
        tr=("$root/toolchain/translator-linux/Translator.Cli")
        return
    fi
    local dll="$root/fw/translator/src/Translator.Cli/bin/Release/net8.0/Translator.Cli.dll"
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_ROLL_FORWARD=Major
    if [[ ! -f "$dll" ]]; then
        step translator-build dotnet build "$root/fw/translator/src/Translator.Cli/Translator.Cli.csproj" -c Release
    fi
    tr=(dotnet "$dll")
}
cd "$root"
if [[ $skip_translate -eq 0 ]]; then
    translator
    step translate "${tr[@]}" translate-recursive 0x80006124 --project "$project" \
        --outdir generated/functions --output-metadata generated/base_translation_output.json \
        --production-source-bundle generated/base_translation_sources.bin \
        --no-function-files --prune-stale --threads "$jobs"
    step data-init "${tr[@]}" generate-data-init --project "$project"
fi
if [[ $skip_translate -eq 0 ]] || grep -q '"[A-Za-z]:/' generated/build_shards/shards.cmake 2> /dev/null; then
    translator
    step shards "${tr[@]}" emit-build-shards --project "$project" \
        --base-metadata "$root/generated/base_translation_output.json" --base-functions-dir "$root/generated/functions" \
        --native-source-dir "$root/runtime/src" --out "$root/generated/build_shards"
fi

# 4. Configure and build (same fixed flags as Windows; aurora's dependencies are downloaded the first time)
step configure cmake -S "$root/runtime" -B "$build" -G Ninja \
    -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_PROCESSOR=x86_64 \
    -DAURORA_DAWN_PROVIDER=package -DAURORA_SDL3_PROVIDER=vendor -DCMAKE_POLICY_DEFAULT_CMP0168=NEW \
    -DMKW_TRANSLATED_COMPILE_JOBS="$shard_jobs"
step compile cmake --build "$build" --target WiiCompiled --parallel "$jobs"
status "$(L "compilado:" "built:") $build/WiiCompiled"

# 5. Play folder: the program and the files it needs next to it
if [[ -n "${WARIO_IN_BOX:-}" ]]; then
    cp -f "$build/WiiCompiled" "$play/WarioRecomp.bin"
    cat > "$play/WarioRecomp" << EOS
#!/bin/sh
# Built in the distrobox container "$box": runs on the system, or in the container if a library is missing here.
d="\$(dirname "\$(readlink -f "\$0")")"
if ldd "\$d/WarioRecomp.bin" 2> /dev/null | grep -q "not found" && command -v distrobox > /dev/null; then
    exec distrobox enter $box -- "\$d/WarioRecomp.bin" "\$@"
fi
exec "\$d/WarioRecomp.bin" "\$@"
EOS
    chmod +x "$play/WarioRecomp"
else
    rm -f "$play/WarioRecomp.bin"
    cp -f "$build/WiiCompiled" "$play/WarioRecomp"
fi
for f in dsp_coef.bin cacert.pem initial_pipeline_cache.db; do
    [[ -f "$build/$f" ]] && cp -f "$build/$f" "$play/"
done
rm -rf "$play/wii_bootstrap"; cp -r "$build/wii_bootstrap" "$play/"
echo "WarioRecomp portable" > "$play/portable.txt"
version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$root/version.json" | head -n 1)"
[[ -n "$version" ]] && printf '%s' "$version" > "$play/built_version.txt"
status "$(L "copiado a Play/WarioRecomp" "copied to Play/WarioRecomp")"
status "$(L "LISTO: ejecuta Play/WarioRecomp" "DONE: run Play/WarioRecomp")"
