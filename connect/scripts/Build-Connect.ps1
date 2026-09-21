param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'), [string]$ServerZip, [switch]$AppOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($out)
$downloads = Join-Path $out 'downloads'
[void][IO.Directory]::CreateDirectory($downloads)
function Get-CheckedDownload([string]$Url, [string]$Destination, [string]$Sha256) {
    if (-not (Test-Path -LiteralPath $Destination)) { Invoke-WebRequest -Uri $Url -OutFile $Destination }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) { throw "Checksum mismatch: $Destination" }
}
$frpZip = Join-Path $downloads 'frp_0.68.0_windows_amd64.zip'
$frpExtract = Join-Path $downloads 'frp-windows'
if (-not $AppOnly) {
    Get-CheckedDownload 'https://github.com/fatedier/frp/releases/download/v0.68.0/frp_0.68.0_windows_amd64.zip' $frpZip '959f13d0d5f17040c3e79c3d9885dc0f43e5503619ea81b123babc3daf4dbeb6'
    if (-not (Test-Path -LiteralPath $frpExtract)) { [IO.Compression.ZipFile]::ExtractToDirectory($frpZip, $frpExtract) }
}
if (-not $ServerZip) { $ServerZip = Join-Path $downloads 'RS2-2004-Server.zip' }
# Exact original release archive; do not bundle a live server or its private state.
$serverHash = 'bc1a236a076264580fbf2ba665610dbd8b027a2ec713bf44f9a5c1bb82cee265'
Get-CheckedDownload 'https://github.com/TommySanzCode/rs2-xbox/releases/download/v0.1.0-preview.1/RS2-2004-Server.zip' $ServerZip $serverHash
$stage = Join-Path $out ('stage-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $stage 'RS2-Xbox-Connect'
& dotnet publish (Join-Path $repo 'connect\src\Connect.App\Connect.App.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Connect publish failed.' }
[void][IO.Directory]::CreateDirectory((Join-Path $app 'frp'))
if (-not $AppOnly) {
    Copy-Item -LiteralPath (Join-Path $frpExtract 'frp_0.68.0_windows_amd64\frpc.exe') -Destination (Join-Path $app 'frp')
    Copy-Item -LiteralPath (Join-Path $frpExtract 'frp_0.68.0_windows_amd64\LICENSE') -Destination (Join-Path $app 'frp\LICENSE')
} else {
    [IO.File]::WriteAllText((Join-Path $app 'FRP-NOT-BUNDLED.txt'), 'DEVELOPER BUILD: the Windows frp download was blocked by local security. This artifact does not include frpc and cannot establish online tunnels. It is not the ready-to-play bundle. Do not disable security protection to run it. See docs/CONNECT-VALIDATION.md.')
}
Copy-Item -LiteralPath (Join-Path $repo 'connect\scripts') -Destination $app -Recurse
[void][IO.Directory]::CreateDirectory((Join-Path $app 'templates'))
Copy-Item -LiteralPath (Join-Path $repo 'xbox-config.example.ini'),(Join-Path $repo 'xbox-128-config.example.ini') -Destination (Join-Path $app 'templates')
$serverExtract = Join-Path $stage 'server-extract'
[IO.Compression.ZipFile]::ExtractToDirectory($ServerZip, $serverExtract)
Move-Item -LiteralPath (Join-Path $serverExtract 'RS2-2004-Server') -Destination (Join-Path $app 'server-template')
Copy-Item -LiteralPath (Join-Path $repo 'connect\README.md') -Destination (Join-Path $app 'START-HERE.md')
$startHere = Join-Path $app 'START-HERE.md'
[IO.File]::WriteAllText($startHere, [IO.File]::ReadAllText($startHere).Replace('(../docs/', '(docs/'))
Copy-Item -LiteralPath (Join-Path $repo 'connect\THIRD-PARTY.md') -Destination $app
Copy-Item -LiteralPath (Join-Path $repo 'connect\licenses') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'CREDITS.md') -Destination $app
# Build-only tooling is unnecessary in the portable app; included source is in GitHub/tag archives.
# A strict deny list catches accidental private-state additions to the clean template.
$privateFiles = @(Get-ChildItem -LiteralPath (Join-Path $app 'server-template') -Recurse -Force -File | Where-Object {
    $_.Name -in @('.env','db.sqlite','server-process.json','setup.local.json','Xbox-config.ini','private.pem','public.pem') -or $_.Name -like '*.log'
})
if ($privateFiles.Count -gt 0) { throw 'Portable template unexpectedly includes generated private state.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$appZip = Join-Path $out $(if ($AppOnly) { 'RS2-Xbox-Connect-Developer-Windows.zip' } else { 'RS2-Xbox-Connect-Windows.zip' })
if (Test-Path -LiteralPath $appZip) { throw 'Output ZIP already exists. Choose a new OutputDirectory.' }
[IO.Compression.ZipFile]::CreateFromDirectory($app, $appZip, [IO.Compression.CompressionLevel]::Optimal, $true)
$relay = Join-Path $stage 'RS2-Xbox-Connect-Relay'
[void][IO.Directory]::CreateDirectory($relay)
Get-ChildItem -LiteralPath (Join-Path $repo 'connect\relay') -File | Where-Object { $_.Name -ne '.env' } | Copy-Item -Destination $relay
Copy-Item -LiteralPath (Join-Path $repo 'connect\THIRD-PARTY.md') -Destination $relay
Copy-Item -LiteralPath (Join-Path $repo 'connect\licenses') -Destination $relay -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'docs\ONLINE-RELAY.md') -Destination (Join-Path $relay 'START-HERE.md')
$relayZip = Join-Path $out 'RS2-Xbox-Connect-Relay.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($relay, $relayZip, [IO.Compression.CompressionLevel]::Optimal, $true)
$checksums = @($appZip, $relayZip) | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf) }
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), $checksums, (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ AppFolder=$app; AppZip=$appZip; RelayZip=$relayZip; FrpVersion='0.68.0'; FrpBundled=(-not $AppOnly); ServerArchiveSha256=$serverHash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'build-manifest.json')
Write-Output "Build complete: $out"
