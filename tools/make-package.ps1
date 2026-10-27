# Makes the WarioRecomp release (dist\WarioRecomp-<version>.zip): the prebuilt launcher plus the source
# and build tools. It contains NOTHING from the game (no main.dol, no disc data, no translated code,
# no WarioRecomp.exe): every user builds the port on their own PC from the launcher (Install page).
param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$launcherExe = Join-Path $root 'launcher\bin\out\WarioLauncher.exe'
if (-not (Test-Path -LiteralPath $launcherExe)) { throw 'Build the launcher first (Build-Launcher.cmd).' }
if (-not $Version) {
    $vj = Join-Path $root 'version.json'
    $Version = if (Test-Path -LiteralPath $vj) { [regex]::Match((Get-Content -Raw -LiteralPath $vj), '"version"\s*:\s*"([^"]+)"').Groups[1].Value } else { Get-Date -Format 'yyyy.MM.dd' }
}
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist ("WarioRecomp-" + $Version)
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# Source folders, without build outputs or downloads.
$skipDirs = @('bin', 'obj', 'Dependencies', 'artifacts', '.git', '__pycache__', 'node_modules')
function Copy-Tree([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Get-ChildItem -LiteralPath $from -Force | ForEach-Object {
        if ($_.PSIsContainer) {
            if ($skipDirs -notcontains $_.Name -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                Copy-Tree $_.FullName (Join-Path $to $_.Name)
            }
        } elseif ($_.Extension -notin @('.dol', '.iso', '.wbfs', '.ciso', '.rvz', '.nand', '.zip')) {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $to $_.Name)
        }
    }
}
foreach ($dir in @('runtime', 'aurora', 'projects', 'tools', 'launcher', 'local-assets', 'screenshots')) {
    $src = Join-Path $root $dir
    if (Test-Path -LiteralPath $src) { Copy-Tree $src (Join-Path $stage $dir) }
}
# WiiCompiled: only the parts the build uses (the translator and the dependency scripts).
foreach ($p in @('translator', 'Launcher\WiiCompiled.Setup.Common')) { Copy-Tree (Join-Path $root "fw\$p") (Join-Path $stage "fw\$p") }
foreach ($f in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'Launcher\Prepare-Dependencies.ps1', 'Launcher\Prepare-PortableTools.ps1', 'Launcher\NativeBuildFlags.ps1', 'Launcher\Directory.Build.props')) {
    $dst = Join-Path $stage "fw\$f"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root "fw\$f") -Destination $dst
}
# The build .cmd files are for developers: in a release everything is done from the launcher
# (Install page), so they are not included.
foreach ($f in @('README.md', 'LICENSE', 'version.json')) {
    $src = Join-Path $root $f
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $stage $f) }
}
Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $stage 'WarioLauncher.exe')
$cfg = $launcherExe + '.config'
if (Test-Path -LiteralPath $cfg) { Copy-Item -LiteralPath $cfg -Destination (Join-Path $stage 'WarioLauncher.exe.config') }

# ---- Bundled build tools (like the WiiCompiled installer), so installing downloads nothing.
$toolchain = Join-Path $root 'toolchain'
$stageTools = Join-Path $stage 'toolchain'
foreach ($t in @('llvm-mingw', 'CMake', 'Ninja')) {
    $src = Join-Path $toolchain $t
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing toolchain\${t}: run Build-WarioRecomp.cmd once to download it." }
    Write-Host "Copying $t..."
    Copy-Item -LiteralPath $src -Destination (Join-Path $stageTools $t) -Recurse -Force
}
# Parts the build never uses: debugger, Python and the CMake GUI.
foreach ($unused in @((Join-Path $stageTools 'llvm-mingw\python'), (Join-Path $stageTools 'CMake\bin\cmake-gui.exe'),
                      (Join-Path $stageTools 'CMake\doc'), (Join-Path $stageTools 'CMake\man'))) {
    if (Test-Path -LiteralPath $unused) { Remove-Item -LiteralPath $unused -Recurse -Force }
}
Get-ChildItem -LiteralPath (Join-Path $stageTools 'llvm-mingw\bin') -File |
    Where-Object { $_.Name -like 'lldb*' -or $_.Name -like 'liblldb*' } | Remove-Item -Force
# x86_64 compiler only: llvm-mingw also ships i686, armv7 and aarch64.
foreach ($arch in @('i686-w64-mingw32', 'armv7-w64-mingw32', 'aarch64-w64-mingw32', 'arm64ec-w64-mingw32')) {
    $d = Join-Path $stageTools ('llvm-mingw\' + $arch)
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
}
# Visual C++ runtime for CMake and Ninja (in case the PC doesn't have it).
foreach ($dll in @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll')) {
    $src = Join-Path $env:SystemRoot ('System32\' + $dll)
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $stageTools 'CMake\bin') -Force
        Copy-Item -LiteralPath $src -Destination (Join-Path $stageTools 'Ninja') -Force
    }
}

# Self-contained translator (a single .exe): users don't need the .NET 8 SDK.
Write-Host 'Publishing the translator...'
$dotnet = Join-Path $toolchain 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet publish (Join-Path $root 'fw\translator\src\Translator.Cli\Translator.Cli.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None `
    -o (Join-Path $stageTools 'translator') | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $stageTools 'translator\Translator.Cli.exe'))) { throw 'Could not publish the translator.' }

# WiiCompiled's dependency trees (its installer's list, minus native_prebuilt: the port always builds aurora from source).
$deps = Join-Path $root 'fw\Dependencies'
foreach ($name in @('abseil-cpp','cppwinrt','dawn_prebuilt','fmt','freetype','imgui','libusb','png','SDL','sqlite3','tracy','xxhash','zlib','zstd')) {
    $src = Join-Path $deps $name
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $stage ('fw\Dependencies\' + $name)) -Recurse -Force }
    else { Write-Warning "Missing fw\Dependencies\$name (the install will download it)." }
}

# nodtool to read the disc image.
$nod = Join-Path $root 'Play\Tools\nodtool.exe'
$stageNod = Join-Path $stage 'Play\Tools\nodtool.exe'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $stageNod) | Out-Null
if (Test-Path -LiteralPath $nod) { Copy-Item -LiteralPath $nod -Destination $stageNod }
else {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/encounter/nod/releases/download/v2.0.0-alpha.10/nodtool-windows-x86_64.exe' -OutFile $stageNod
}
Copy-Item -LiteralPath (Join-Path $root 'fw\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $stage 'THIRD-PARTY-NOTICES.md') -ErrorAction SilentlyContinue

# Check: nothing from the game inside.
$bad = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -in @('main.dol', 'WLS.bin', 'WarioRecomp.exe') -or $_.FullName -match '\\generated\\' }
if ($bad) { throw ('The release would include game files: ' + ($bad.FullName -join ', ')) }

$zip = $stage + '.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Write-Host 'Compressing...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("Release ready: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
# The launcher also goes as its own release file: launchers update themselves from it
# (https://github.com/<repo>/releases/download/v<launcher>/WarioLauncher.exe, "launcher" in version.json).
Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $dist 'WarioLauncher.exe') -Force
Write-Host "Upload too: $(Join-Path $dist 'WarioLauncher.exe') (same release, file name WarioLauncher.exe)"

