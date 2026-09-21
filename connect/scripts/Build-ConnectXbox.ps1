param(
    [Parameter(Mandatory)][string]$NxdkPath,
    [Parameter(Mandatory)][string]$CacheFolder,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet(64,128)][int]$RamMB = 64
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Use a new build directory; existing Xbox builds are never overwritten.' }
[void][IO.Directory]::CreateDirectory($out)
foreach ($folder in @('src','scripts')) {
    $base = Join-Path $repo $folder
    foreach ($file in Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object { $_.Extension -notin @('.obj','.d','.o','.pyc','.exe') -and $_.FullName -notmatch '__pycache__' }) {
        $target = Join-Path $out ($folder + '\' + $file.FullName.Substring($base.Length + 1))
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)); Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
Copy-Item -LiteralPath (Join-Path $repo 'xbox.mk'),(Join-Path $repo 'xbox-config.example.ini'),(Join-Path $repo 'xbox-128-config.example.ini') -Destination $out
[void][IO.Directory]::CreateDirectory((Join-Path $out 'rom'))
Copy-Item -LiteralPath (Join-Path $repo 'rom\Roboto') -Destination (Join-Path $out 'rom') -Recurse
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $CacheFolder).Path -Destination (Join-Path $out 'rom\cache') -Recurse
$templateName = if ($RamMB -eq 64) { 'xbox-config.example.ini' } else { 'xbox-128-config.example.ini' }
$config = [IO.File]::ReadAllText((Join-Path $repo $templateName))
$config = [regex]::Replace($config, '(?m)^socketip\s*=.*$', 'socketip = auto')
[IO.File]::WriteAllText((Join-Path $out 'rom\config.ini'), $config, [Text.UTF8Encoding]::new($false))
& (Join-Path $out 'scripts\build-xbox.ps1') -NxdkPath $NxdkPath -RamMB $RamMB
if (-not $?) { throw 'Connect Xbox build failed.' }
Write-Output "Connect-enabled $RamMB MB client built in isolated folder. Transfer default.xbe plus the supplied config for a fresh install; preserve graphics/audio settings when updating an existing client."
