# Builds the launcher (launcher-avalonia, Avalonia on .NET 10) as one self-contained WarioLauncher.exe (no .NET to
# install) into launcher-avalonia\bin\out and copies it to Play\WarioLauncher.exe. Needs the .NET 10 SDK.
# Log: build-logs\launcher.log
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root 'build-logs'
[IO.Directory]::CreateDirectory($logs) | Out-Null
$log = Join-Path $logs 'launcher.log'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'dotnet not found: install the .NET 10 SDK (https://dotnet.microsoft.com/download).' }
$sdks = & $dotnet.Source --list-sdks 2>$null
if (-not ($sdks -match '^(1[0-9]|[2-9][0-9])\.')) { throw 'The launcher needs the .NET 10 SDK (dotnet --list-sdks shows none 10.x or newer).' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$proj = Join-Path $root 'launcher-avalonia\WarioLauncher.csproj'
$out = Join-Path $root 'launcher-avalonia\bin\out'

$previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& $dotnet.Source publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $out 2>&1 |
    ForEach-Object { "$_" } | Out-File -LiteralPath $log -Encoding utf8
$code = $LASTEXITCODE
$ErrorActionPreference = $previous
if ($code -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $out 'WarioLauncher.exe'))) {
    Get-Content -LiteralPath $log -Tail 30
    throw "The launcher did not build (code $code), see build-logs\launcher.log"
}
$play = Join-Path $root 'Play'
New-Item -ItemType Directory -Force -Path $play | Out-Null
Get-Process WarioLauncher -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($play, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force
Copy-Item -LiteralPath (Join-Path $out 'WarioLauncher.exe') -Destination (Join-Path $play 'WarioLauncher.exe') -Force
# The old launcher (WinForms, .NET Framework) had a .config next to it.
Remove-Item -LiteralPath (Join-Path $play 'WarioLauncher.exe.config') -ErrorAction SilentlyContinue
$assets = Join-Path $root 'local-assets'
foreach ($b in @('launcher_banner.jpg', 'launcher_banner.png')) {
    $src = Join-Path $assets $b
    if (Test-Path -LiteralPath $src) {
        New-Item -ItemType Directory -Force -Path (Join-Path $play 'UserData') | Out-Null
        Copy-Item -LiteralPath $src -Destination (Join-Path $play ('UserData\' + $b)) -Force
    }
}
Write-Host "Launcher ready: $play\WarioLauncher.exe"
