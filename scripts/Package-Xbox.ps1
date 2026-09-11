param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$PackageName = 'RS2-2004-LAN',
    [switch]$IncludeLocalConfig
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
if ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $romPath 'default.xbe')), 0, 4) -ne 'XBEH') {
    throw 'Missing or invalid Xbox executable.'
}
$badPaths = @(Get-ChildItem -LiteralPath $romPath -Recurse | Where-Object {
    $_.Name.Length -gt 42 -or $_.Name -match '["*+,/:;<=>?\[\]\\|]' -or
    ($_.FullName.Substring($romPath.Length).Length + $PackageName.Length + 10) -gt 240
})
if ($badPaths.Count) { throw 'An asset path exceeds FATX filename/path limits.' }

New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $romPath -Force) {
    if ($item.Name -ne 'config.ini') { Copy-Item -LiteralPath $item.FullName -Destination $packagePath -Recurse }
}
$configSource = if ($IncludeLocalConfig) { Join-Path $romPath 'config.ini' } else { Join-Path $clientPath 'xbox-config.example.ini' }
Copy-Item -LiteralPath $configSource -Destination (Join-Path $packagePath 'config.ini')
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
Audio and controller text entry are not implemented. No Jagex account is used.
"@
[IO.File]::WriteAllText((Join-Path $packagePath 'READ-ME.txt'), $instructions, [Text.UTF8Encoding]::new($false))
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
