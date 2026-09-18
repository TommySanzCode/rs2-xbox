param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$PackageName = 'RS2-2004-LAN',
    [switch]$IncludeLocalConfig,
    [string]$SourceArchive,
    [ValidateSet(480, 720)][int]$VideoMode = 480
)

$ErrorActionPreference = 'Stop'
$workspacePath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$clientPath = if (Test-Path -LiteralPath (Join-Path $workspacePath 'Client3')) { Join-Path $workspacePath 'Client3' } else { $workspacePath }
$romPath = Join-Path $clientPath 'rom'
$distPath = Join-Path $clientPath 'dist'
$packagePath = Join-Path $distPath $PackageName
$zipPath = Join-Path $distPath ($PackageName + '-Xbox.zip')
if ((Test-Path -LiteralPath $packagePath) -or (Test-Path -LiteralPath $zipPath)) {
    throw 'This package already exists. Choose a new -PackageName to preserve the earlier build.'
}
$xbeBytes = [IO.File]::ReadAllBytes((Join-Path $romPath 'default.xbe'))
if ($xbeBytes.Length -lt 376 -or [Text.Encoding]::ASCII.GetString($xbeBytes, 0, 4) -ne 'XBEH') {
    throw 'Missing or invalid Xbox executable.'
}
$enhanced = ([BitConverter]::ToUInt32($xbeBytes, 292) -band 4) -eq 0
$manifestPath = Join-Path $clientPath 'build/xbox-build.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Build manifest missing. Build with scripts/build-xbox.ps1 first.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($enhanced -ne ($manifest.ram_mb -eq 128)) { throw 'XBE memory flag and build manifest disagree.' }
if ($VideoMode -eq 720 -and -not $enhanced) { throw '720p packaging requires the 128 MB build. The stock profile remains 480.' }
if ($IncludeLocalConfig -and $PSBoundParameters.ContainsKey('VideoMode')) {
    throw 'Set the video mode in your local config when including it, or omit -IncludeLocalConfig to package a blank preset.'
}
$rawHash = (Get-FileHash -LiteralPath (Join-Path $romPath 'default.xbe') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($rawHash -ne $manifest.artifacts.'rom/default.xbe'.sha256) { throw 'XBE does not match the build manifest.' }
if ($enhanced -and -not (Test-Path -LiteralPath (Join-Path $romPath 'TimGM6mb.sf2'))) {
    throw 'The enhanced package needs TimGM6mb.sf2. Run python scripts/prepare-xbox-audio.py.'
}
if ($SourceArchive -and -not (Test-Path -LiteralPath $SourceArchive -PathType Leaf)) {
    throw 'The supplied source archive was not found.'
}
$badPaths = @(Get-ChildItem -LiteralPath $romPath -Recurse | Where-Object {
    $_.Name.Length -gt 42 -or $_.Name -match '["*+,/:;<=>?\[\]\\|]' -or
    ($_.FullName.Substring($romPath.Length).Length + $PackageName.Length + 10) -gt 240
})
if ($badPaths.Count) { throw 'An asset path exceeds FATX filename/path limits.' }

New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $romPath -Force) {
    if ($item.Name -ne 'config.ini' -and ($enhanced -or $item.Name -ne 'TimGM6mb.sf2')) {
        Copy-Item -LiteralPath $item.FullName -Destination $packagePath -Recurse
    }
}
$templateName = if ($enhanced) { 'xbox-128-config.example.ini' } else { 'xbox-config.example.ini' }
$configSource = if ($IncludeLocalConfig) { Join-Path $romPath 'config.ini' } else { Join-Path $clientPath $templateName }
Copy-Item -LiteralPath $configSource -Destination (Join-Path $packagePath 'config.ini')
if ($enhanced -and -not $IncludeLocalConfig) {
    $blankConfig = [IO.File]::ReadAllText((Join-Path $packagePath 'config.ini'))
    $blankConfig = [regex]::Replace($blankConfig, '(?m)^xbox_video[ \t]*=[ \t]*480[ \t]*\r?$', "xbox_video = $VideoMode")
    [IO.File]::WriteAllText((Join-Path $packagePath 'config.ini'), $blankConfig, [Text.UTF8Encoding]::new($false))
}
& python (Join-Path $PSScriptRoot 'sanitize-xbox-paths.py') --input (Join-Path $romPath 'default.xbe') --output (Join-Path $packagePath 'default.xbe')
if ($LASTEXITCODE -ne 0) { throw 'Executable privacy sanitation failed.' }
Copy-Item -LiteralPath (Join-Path $clientPath 'licenses') -Destination (Join-Path $packagePath 'licenses') -Recurse
New-Item -ItemType Directory -Path (Join-Path $packagePath 'release-notices') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $packagePath 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $clientPath 'release-notices/xbox') -Destination (Join-Path $packagePath 'release-notices/xbox') -Recurse
Copy-Item -LiteralPath (Join-Path $clientPath 'CREDITS.md') -Destination $packagePath
Copy-Item -LiteralPath (Join-Path $clientPath 'docs/THIRD-PARTY.md') -Destination (Join-Path $packagePath 'docs/THIRD-PARTY.md')
$audioText = 'This stock 64 MB build has no audio.'
if ($enhanced) {
    Copy-Item -LiteralPath (Join-Path $clientPath 'docs/XBOX-128.md') -Destination (Join-Path $packagePath 'HELP-128MB.md')
    Copy-Item -LiteralPath (Join-Path $clientPath 'docs/XBOX-128.md') -Destination (Join-Path $packagePath 'docs/XBOX-128.md')
    Copy-Item -LiteralPath (Join-Path $clientPath 'xbox-128-config.example.ini') -Destination (Join-Path $packagePath 'OPTIONS.example.ini')
    $audioText = 'EXPERIMENTAL 128 MB build: music, jingles, effects, high detail, optional 720p. See HELP-128MB.md and config.ini. Requires expanded RAM exposed by your BIOS. Keep TimGM6mb.sf2 beside the XBE. Audio, gameplay and performance need testing on a 128 MB console.'
    if (-not $IncludeLocalConfig) { $audioText += " This package requests $VideoMode output. Keep that xbox_video value when copying server connection settings." }
}
$manifest.artifacts.'rom/default.xbe'.sha256 = (Get-FileHash -LiteralPath (Join-Path $packagePath 'default.xbe') -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest | Add-Member -NotePropertyName packaged_config -NotePropertyValue $(if ($IncludeLocalConfig) { 'local-private' } else { 'blank-example' })
if (-not $IncludeLocalConfig) { $manifest | Add-Member -NotePropertyName requested_video_mode -NotePropertyValue $VideoMode }
if ($SourceArchive) {
    Copy-Item -LiteralPath $SourceArchive -Destination (Join-Path $packagePath 'Source.zip')
    $manifest | Add-Member -NotePropertyName source_archive_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $SourceArchive -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $packagePath 'BUILD.json') -Encoding utf8
$instructions = @"
RuneScape 2 revision 225 - native original Xbox LAN package

Copy this entire folder to E:\Games\$PackageName\ and launch default.xbe.
Keep config.ini, cache, and Roboto beside the XBE. Use the complete new folder.

Configure the server address and a local test account in config.ini before login.
Use a compatible revision-225 server and matching game cache. Default TCP port: 43594.
On Xbox, press Start to submit the configured login. Left stick moves the cursor;
right stick rotates the camera. A left-clicks, B right-clicks.
Hold Black + left stick for a slower pointer. Back + Start logs out.
X is the Ctrl/run modifier; Y toggles the performance display.
The full interface fits onscreen. White cycles normal, large, and inset TV sizes.
Area filtering preserves thin text strokes and smooths the reduced image.
Keep the server PC on while playing.

Local configuration included: $($IncludeLocalConfig.IsPresent)
The default package uses the blank example configuration. If local configuration
was explicitly included, keep that package private because it contains login settings.
Extended gameplay and memory headroom still need hardware testing.
$audioText
Controller text entry is not implemented. No Jagex account is used.
If present, Source.zip contains this build's client source, build tools and notices.
"@
[IO.File]::WriteAllText((Join-Path $packagePath 'READ-ME.txt'), $instructions, [Text.UTF8Encoding]::new($false))
$badPaths = @(Get-ChildItem -LiteralPath $packagePath -Recurse | Where-Object {
    $_.Name.Length -gt 42 -or $_.Name -match '["*+,/:;<=>?\[\]\\|]' -or
    ($_.FullName.Substring($packagePath.Length).Length + $PackageName.Length + 10) -gt 240
})
if ($badPaths.Count) { throw 'A packaged asset/notice exceeds FATX filename/path limits.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packagePath, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)

$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
$verified = 0
try {
    $expectedFiles = @(Get-ChildItem -LiteralPath $packagePath -Recurse -File)
    $entries = @($archive.Entries | Where-Object { $_.Name })
    if ($entries.Count -ne $expectedFiles.Count) { throw 'ZIP file count mismatch.' }
    foreach ($entry in $entries) {
        $relativePath = $entry.FullName.Substring($PackageName.Length + 1).Replace('/', '\')
        $originalPath = Join-Path $packagePath $relativePath
        if ((Get-Item -LiteralPath $originalPath).Length -ne $entry.Length) { throw 'ZIP length mismatch.' }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($digest -ne (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash) {
            throw 'ZIP content checksum mismatch.'
        }
        $verified++
    }
} finally { $archive.Dispose() }

Write-Output "Verified $verified packaged files, FATX paths, and XBE header."
Write-Output "Package: $zipPath"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)"
