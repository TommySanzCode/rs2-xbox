param(
    [string]$MsysRoot = 'C:\msys64',
    [string]$NxdkPath = (Join-Path $PSScriptRoot '..\.deps\nxdk'),
    [ValidateRange(1, 32)][int]$Jobs = 8,
    [ValidateSet(64, 128)][int]$RamMB = 64
)

$ErrorActionPreference = 'Stop'
$clientPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sdkPath = (Resolve-Path -LiteralPath $NxdkPath).Path
$bashPath = Join-Path $MsysRoot 'usr\bin\bash.exe'
if (-not (Test-Path -LiteralPath $bashPath)) {
    throw "MSYS2 bash was not found at $bashPath. Set -MsysRoot to your MSYS2 installation."
}

# nxdk's makefiles require paths without spaces. Use the existing NTFS short
# names so this project can stay in its normal Windows workspace folder.
$fileSystem = New-Object -ComObject Scripting.FileSystemObject
function ConvertTo-MsysBuildPath([string]$folder) {
    $shortPath = $fileSystem.GetFolder($folder).ShortPath
    if ($shortPath -match '\s' -or $shortPath -notmatch '^[A-Za-z]:\\') {
        throw "nxdk needs a local path without spaces. NTFS did not supply one for '$folder'. Use a checkout path without spaces."
    }
    return '/' + $shortPath.Substring(0, 1).ToLowerInvariant() + $shortPath.Substring(2).Replace('\', '/')
}

$clientBuildPath = ConvertTo-MsysBuildPath $clientPath
$sdkBuildPath = ConvertTo-MsysBuildPath $sdkPath
$previousSystem = $env:MSYSTEM
try {
    $env:MSYSTEM = 'MINGW64'
    & $bashPath --login "$clientBuildPath/scripts/build-xbox.sh" $clientBuildPath $sdkBuildPath $Jobs $RamMB
    if ($LASTEXITCODE -ne 0) {
        throw "Xbox build failed (exit $LASTEXITCODE). See build/xbox-build.log."
    }
} finally {
    $env:MSYSTEM = $previousSystem
}

$outputs = @('rom\default.xbe', 'client.iso')
foreach ($relativeOutput in $outputs) {
    $outputPath = Join-Path $clientPath $relativeOutput
    $outputFile = Get-Item -LiteralPath $outputPath
    $digest = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
    Write-Output "$relativeOutput : $($outputFile.Length) bytes, SHA256 $digest"
}
