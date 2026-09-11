param([string]$SdkPath = (Join-Path $PSScriptRoot '..\.deps\nxdk'))

$ErrorActionPreference = 'Stop'
$revision = '29638d0b001f179b73c3513489af10ddc2986216'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Install Git first.' }
function Invoke-GitChecked([string[]]$Arguments) {
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Git dependency setup failed.' }
}
if (Test-Path -LiteralPath $SdkPath) {
    $actual = & git -C $SdkPath rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or $actual -ne $revision) {
        throw 'The SDK destination already exists with a different revision. Choose an empty -SdkPath.'
    }
    $changes = & git -C $SdkPath status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'The existing SDK has local changes; choose a separate -SdkPath.' }
} else {
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($SdkPath))
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Invoke-GitChecked -Arguments @('clone', '--no-checkout', 'https://github.com/XboxDev/nxdk.git', $SdkPath)
    Invoke-GitChecked -Arguments @('-C', $SdkPath, 'checkout', '--detach', $revision)
}
Invoke-GitChecked -Arguments @('-C', $SdkPath, 'submodule', 'update', '--init', '--recursive')
Write-Output "nxdk is ready at pinned revision $revision."
