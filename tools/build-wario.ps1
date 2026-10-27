# WarioRecomp: full Windows build (portable tools -> translation -> compilation -> test run).
# Usage: Build-WarioRecomp.cmd (or powershell -ExecutionPolicy Bypass -File tools\build-wario.ps1)
# Downloads only what is missing: llvm-mingw, CMake, Ninja, WiiCompiled dependencies and the .NET 8 SDK.
# Logs: build-logs\status.txt (progress) and build-logs\<step>.log. Unless -SkipRun, it ends with a 60 s
# test run whose output goes to build-logs\run.log / run.err.log.
[CmdletBinding()]
param(
    [int]$Jobs = 0,
    [switch]$SkipTools,
    [switch]$SkipTranslate,
    [switch]$SkipRun,
    [int]$RunSeconds = 60,
    # Extracted game folder (contains sys\main.dol). Empty = paths.dvd_root from Play\UserData\Config.toml,
    # which the launcher writes when it imports the disc.
    [string]$GameData = '',
    # Language of the build-logs\status.txt messages (the launcher passes its own).
    [ValidateSet('en', 'es')][string]$Lang = 'en'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fw = Join-Path $root 'fw'
$toolchain = Join-Path $root 'toolchain'
$logs = Join-Path $root 'build-logs'
$project = 'projects/wario/recomp.yml'
$build = Join-Path $root 'nb-win'
$play = Join-Path $root 'Play'
if ($Jobs -le 0) { $Jobs = [Math]::Max(1, [Environment]::ProcessorCount - 1) }
# Each translated-code shard needs ~1.5 GB of RAM to compile: limit the jobs by the PC's memory.
$ramGB = [Math]::Floor((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
$shardJobs = [Math]::Max(1, [Math]::Min($Jobs, [Math]::Floor(($ramGB - 6) / 2)))
[IO.Directory]::CreateDirectory($logs) | Out-Null
. (Join-Path $fw 'Launcher\NativeBuildFlags.ps1')

function L([string]$Es, [string]$En) { if ($Lang -eq 'es') { $Es } else { $En } }
function Set-Status([string]$Text) {
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Text
    Write-Host $line
    Add-Content -LiteralPath (Join-Path $logs 'status.txt') -Value $line
}
function Invoke-Step([string]$Name, [string]$Exe, [string[]]$Arguments, [string]$WorkDir) {
    $log = Join-Path $logs "$Name.log"
    Set-Status ("${Name}: " + (L "empezando" "started"))
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    Push-Location $WorkDir
    try { & $Exe @Arguments 2>&1 | ForEach-Object { "$_" } | Out-File -LiteralPath $log -Encoding utf8 }
    finally { Pop-Location; $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) {
        Set-Status ("${Name}: " + (L "FALLO (codigo $LASTEXITCODE), ver build-logs\$Name.log" "FAILED (code $LASTEXITCODE), see build-logs\$Name.log"))
        Get-Content -LiteralPath $log -Tail 40
        throw ("$Name " + (L "fallo" "failed"))
    }
    Set-Status "${Name}: ok"
}

Remove-Item -LiteralPath (Join-Path $logs 'status.txt') -ErrorAction SilentlyContinue
Set-Status (L "compilacion iniciada en $root con $Jobs procesos ($shardJobs para codigo traducido, RAM $ramGB GB)" "build started in $root with $Jobs processes ($shardJobs for translated code, RAM $ramGB GB)")

# Any error ends up in status.txt (the launcher shows it).
trap { Set-Status ("ERROR: " + $_.Exception.Message); exit 1 }

# Launcher self-update for launchers that cannot do it themselves (0.1.0 updates only the code and then runs
# this script). version.json "launcher" names the launcher version of that release; when the launcher next to
# the code is older, WarioLauncher.exe is downloaded from that release's files (tag v<launcher>). The running
# launcher is renamed to WarioLauncher.old.exe (a running .exe can be renamed, not deleted) and the new one
# takes its name; it is used from the next start, and the new launcher deletes the .old.exe. Never fatal.
function Update-LauncherBinary {
    $vj = Join-Path $root 'version.json'
    if (-not (Test-Path -LiteralPath $vj)) { return }
    $json = Get-Content -Raw -LiteralPath $vj
    $want = [regex]::Match($json, '"launcher"\s*:\s*"([^"]+)"').Groups[1].Value
    if (-not $want) { return }
    $exe = $null
    $proc = Get-Process WarioLauncher -ErrorAction SilentlyContinue | Where-Object { $_.Path } | Select-Object -First 1
    if ($proc -and ($proc.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase))) { $exe = $proc.Path }
    if (-not $exe) { foreach ($c in @((Join-Path $root 'WarioLauncher.exe'), (Join-Path $play 'WarioLauncher.exe'))) { if (Test-Path -LiteralPath $c) { $exe = $c; break } } }
    if (-not $exe) { return }
    $have = [version]'0.0.0'
    try { $fv = [version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion; $have = [version]::new($fv.Major, $fv.Minor, [Math]::Max(0, $fv.Build)) } catch { }
    $w = [version]$want; $w = [version]::new($w.Major, $w.Minor, [Math]::Max(0, $w.Build))
    if ($have -ge $w) { return }
    $repo = 'santiagovalencia109/Wario-Land-Shake-It-Native-PC'
    $ini = Join-Path $play 'UserData\launcher.ini'
    if (Test-Path -LiteralPath $ini) {
        $m = Select-String -LiteralPath $ini -Pattern '^\s*update_repo\s*=\s*(.+)$' | Select-Object -First 1
        if ($m) { $repo = $m.Matches[0].Groups[1].Value.Trim() }
    }
    $dir = Split-Path -Parent $exe
    $fresh = Join-Path $dir 'WarioLauncher.new.exe'
    $old = Join-Path $dir 'WarioLauncher.old.exe'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/$repo/releases/download/v$want/WarioLauncher.exe" -OutFile $fresh -Headers @{ 'User-Agent' = 'WarioLauncher' }
        $bytes = [IO.File]::ReadAllBytes($fresh)
        if ($bytes.Length -lt 100000 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw 'not an executable' }
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
        Move-Item -LiteralPath $exe -Destination $old
        try { Move-Item -LiteralPath $fresh -Destination $exe } catch { Move-Item -LiteralPath $old -Destination $exe; throw }
        Set-Status (L "launcher actualizado a $want (se usa al volver a abrirlo)" "launcher updated to $want (used from the next start)")
    } catch {
        Remove-Item -LiteralPath $fresh -ErrorAction SilentlyContinue
        Set-Status (L "aviso: no se pudo actualizar el launcher ($($_.Exception.Message)); baja el zip $want de GitHub" "warning: could not update the launcher ($($_.Exception.Message)); download the $want zip from GitHub")
    }
}
Update-LauncherBinary

# 0. Game: main.dol and the extracted data (used in place; the disc files are not copied)
$cfgPath = Join-Path $play 'UserData\Config.toml'
if (-not $GameData -and (Test-Path -LiteralPath $cfgPath)) {
    $m = Select-String -LiteralPath $cfgPath -Pattern '^\s*dvd_root\s*=\s*"(.*)"' | Select-Object -First 1
    if ($m) { $GameData = $m.Matches[0].Groups[1].Value -replace '\\\\', '\' }
}
$dol = Join-Path $root 'game\main.dol'
if (-not (Test-Path -LiteralPath $dol)) {
    if (-not $GameData -or -not (Test-Path -LiteralPath (Join-Path $GameData 'sys\main.dol'))) {
        throw (L 'No se encuentra el juego: importa el disco en el lanzador o pasa -GameData <carpeta DATA>.' 'Game not found: import the disc in the launcher or pass -GameData <DATA folder>.')
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'game') | Out-Null
    Copy-Item -LiteralPath (Join-Path $GameData 'sys\main.dol') -Destination $dol
}
if ((Get-FileHash -LiteralPath $dol -Algorithm SHA256).Hash -ne '053BE60DDE84C8042F45ED2F4BA80C17EFB2D20FEDF95A414C2C7E37D336912A') { throw (L 'main.dol no es RWLE01 (SHA-256 distinto)' 'main.dol is not RWLE01 (SHA-256 mismatch)') }

# 1. Tools and dependencies
if (-not $SkipTools) {
    if (-not (Test-Path -LiteralPath (Join-Path $toolchain 'llvm-mingw\bin\x86_64-w64-mingw32-clang++.exe'))) {
        Set-Status (L 'herramientas: descargando llvm-mingw, CMake y Ninja' 'tools: downloading llvm-mingw, CMake and Ninja')
        & (Join-Path $fw 'Launcher\Prepare-PortableTools.ps1') -Destination $toolchain *>&1 | ForEach-Object { "$_" } | Out-File -LiteralPath (Join-Path $logs 'tools.log') -Encoding utf8
    }
    if (-not (Test-Path -LiteralPath (Join-Path $fw 'Dependencies\dawn_prebuilt\bin\webgpu_dawn.dll'))) {
        Set-Status (L 'dependencias: descargando (Dawn, SDL3, etc.)' 'dependencies: downloading (Dawn, SDL3, etc.)')
        & (Join-Path $fw 'Launcher\Prepare-Dependencies.ps1') -Destination (Join-Path $fw 'Dependencies') *>&1 | ForEach-Object { "$_" } | Out-File -LiteralPath (Join-Path $logs 'dependencies.log') -Encoding utf8
    }
    foreach ($tool in @('llvm-mingw\bin\x86_64-w64-mingw32-clang++.exe', 'CMake\bin\cmake.exe', 'Ninja\ninja.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $toolchain $tool))) { throw (L "Falta $tool (ver build-logs\tools.log)" "Missing $tool (see build-logs\tools.log)") }
    }
    Set-Status (L 'herramientas y dependencias listas' 'tools and dependencies ready')
}

# 2. Translator. Releases ship toolchain\translator\Translator.Cli.exe (self-contained, no .NET needed);
#    otherwise (repository) it is built from fw\translator with the .NET 8 SDK (portable if needed).
$translatorExe = Join-Path $toolchain 'translator\Translator.Cli.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_ROLL_FORWARD = 'Major'
if (Test-Path -LiteralPath $translatorExe) {
    $trExe = $translatorExe; $trPre = @()
} else {
    $dotnetExe = $null
    $sys = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($sys) { $sdks = & $sys.Source --list-sdks 2>$null; if ($sdks -match '^8\.') { $dotnetExe = $sys.Source } }
    if (-not $dotnetExe) {
        $dotnetExe = Join-Path $toolchain 'dotnet\dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnetExe)) {
            Set-Status (L '.NET 8 SDK: descargando (portable, en toolchain\dotnet)' '.NET 8 SDK: downloading (portable, to toolchain\dotnet)')
            $installer = Join-Path $toolchain 'dotnet-install.ps1'
            New-Item -ItemType Directory -Force -Path $toolchain | Out-Null
            Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
            & $installer -Channel 8.0 -InstallDir (Join-Path $toolchain 'dotnet') -NoPath *>&1 | Out-File -LiteralPath (Join-Path $logs 'dotnet.log') -Encoding utf8
        }
    }
    $translator = Join-Path $fw 'translator\src\Translator.Cli\bin\Release\net8.0\Translator.Cli.dll'
    if (-not (Test-Path -LiteralPath $translator)) {
        Invoke-Step 'translator-build' $dotnetExe @('build', (Join-Path $fw 'translator\src\Translator.Cli\Translator.Cli.csproj'), '-c', 'Release') $root
    }
    $trExe = $dotnetExe; $trPre = @($translator)
}

# 3. Translation (the Wario runtime is already generated in runtime\)
if (-not $SkipTranslate) {
    Invoke-Step 'translate' $trExe @($trPre + @('translate-recursive', '0x80006124', '--project', $project,
        '--outdir', 'generated/functions', '--output-metadata', 'generated/base_translation_output.json',
        '--production-source-bundle', 'generated/base_translation_sources.bin',
        '--no-function-files', '--prune-stale', '--threads', "$Jobs")) $root
    Invoke-Step 'data-init' $trExe @($trPre + @('generate-data-init', '--project', $project)) $root
    Invoke-Step 'shards' $trExe @($trPre + @('emit-build-shards', '--project', $project,
        '--base-metadata', (Join-Path $root 'generated\base_translation_output.json'), '--base-functions-dir', (Join-Path $root 'generated\functions'),
        '--native-source-dir', (Join-Path $root 'runtime\src'), '--out', (Join-Path $root 'generated\build_shards'))) $root
}

# 4. Configure and build with the portable tools
# The port's icon (local-assets\wario.ico) replaces WiiCompiled's runtime icon.
$localIcon = Join-Path $root 'local-assets\wario.ico'
if (Test-Path -LiteralPath $localIcon) {
    $runtimeIcon = Join-Path $root 'runtime\assets\icon\nsmbw.ico'
    if (-not (Test-Path -LiteralPath $runtimeIcon) -or (Get-FileHash -LiteralPath $localIcon).Hash -ne (Get-FileHash -LiteralPath $runtimeIcon).Hash) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $runtimeIcon) | Out-Null
        Copy-Item -LiteralPath $localIcon -Destination $runtimeIcon -Force
    }
}
$env:PATH = Get-MkwToolchainPath $toolchain
$bin = Join-Path $toolchain 'llvm-mingw\bin'
$configure = Get-MkwNativeConfigureArguments `
    -SourceDirectory (Join-Path $root 'runtime') -BuildDirectory $build `
    -Ninja (Join-Path $toolchain 'Ninja\ninja.exe') `
    -CCompiler (Join-Path $bin 'x86_64-w64-mingw32-clang.exe') `
    -CxxCompiler (Join-Path $bin 'x86_64-w64-mingw32-clang++.exe') `
    -ResourceCompiler (Join-Path $bin 'x86_64-w64-mingw32-windres.exe') `
    -DependenciesDirectory (Join-Path $fw 'Dependencies') `
    -AdditionalArguments @("-DMKW_TRANSLATED_COMPILE_JOBS=$shardJobs")
$cmake = Join-Path $toolchain 'CMake\bin\cmake.exe'
Invoke-Step 'configure' $cmake $configure $root
Invoke-Step 'compile' $cmake @('--build', $build, '--target', 'WiiCompiled', '--parallel', "$Jobs") $root
Set-Status ((L "compilado: " "built: ") + "$build\WiiCompiled.exe")

# 5. Portable Play folder and an unattended test run
New-Item -ItemType Directory -Force -Path (Join-Path $play 'UserData') | Out-Null
Get-Process WarioRecomp -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item -LiteralPath (Join-Path $build 'WiiCompiled.exe') -Destination (Join-Path $play 'WarioRecomp.exe') -Force
Get-ChildItem -LiteralPath $build -Filter *.dll | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $play -Force }
Set-Content -LiteralPath (Join-Path $play 'portable.txt') -Value 'WarioRecomp portable' -Encoding ascii
$cfg = Join-Path $play 'UserData\Config.toml'
if (-not (Test-Path -LiteralPath $cfg) -and $GameData) {
    $escaped = $GameData -replace '\\', '\\'
    Set-Content -LiteralPath $cfg -Value "[paths]`ndvd_root = `"$escaped`"`n" -Encoding utf8
}
# The launcher compares this with version.json to tell whether the program is older than the code.
$versionJson = Join-Path $root 'version.json'
if (Test-Path -LiteralPath $versionJson) {
    $builtVersion = [regex]::Match((Get-Content -Raw -LiteralPath $versionJson), '"version"\s*:\s*"([^"]+)"').Groups[1].Value
    if ($builtVersion) { Set-Content -LiteralPath (Join-Path $play 'built_version.txt') -Value $builtVersion -Encoding ascii -NoNewline }
}
Set-Status (L 'copiado a Play\WarioRecomp.exe' 'copied to Play\WarioRecomp.exe')
if (-not $SkipRun) {
    Set-Content -LiteralPath (Join-Path $play 'autoexit.txt') -Value "$RunSeconds" -Encoding ascii
    Set-Status (L "prueba: arrancando $RunSeconds s" "test: running $RunSeconds s")
    $p = Start-Process -FilePath (Join-Path $play 'WarioRecomp.exe') -WorkingDirectory $play -PassThru `
        -RedirectStandardOutput (Join-Path $logs 'run.log') -RedirectStandardError (Join-Path $logs 'run.err.log')
    if (-not $p.WaitForExit(($RunSeconds + 60) * 1000)) { $p.Kill(); Set-Status (L 'prueba: no termino sola, cerrada' 'test: did not exit, closed') }
    Remove-Item -LiteralPath (Join-Path $play 'autoexit.txt') -ErrorAction SilentlyContinue
    Set-Status ((L "prueba terminada (codigo {0}); ver build-logs\run.log y run.err.log" "test finished (code {0}); see build-logs\run.log and run.err.log") -f $p.ExitCode)
}
Set-Status (L 'LISTO' 'DONE')
