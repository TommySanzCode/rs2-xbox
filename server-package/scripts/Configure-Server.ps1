param([ValidateRange(1,120)][int]$ProbeTimeoutSeconds = 10,
    [ValidateRange(1,300)][int]$SetupTimeoutSeconds = 90)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'RS2-Server.Common.ps1')

$controlLock = Open-RS2ControlLock
try {
    Write-Output '[1/4] Checking for an existing server...'
    if (Get-RS2RecordedProcess (Read-RS2Record)) {
        Write-Output 'Server already running. Stop it before applying options.'
        exit 0
    }
    if (@(Get-RS2WrapperProcesses).Count -gt 0) {
        throw 'An unrecorded server process exists for this folder. Inspect it before configuring.'
    }
    if (-not (Test-Path -LiteralPath $rs2Bun -PathType Leaf)) {
        throw 'Extract the complete server ZIP before starting: the bundled Bun runtime is missing.'
    }
    $address = Read-RS2AddressOption
    $detectedAddress = ''
    if ($address -eq 'auto') {
        Write-Output '[2/4] Detecting the PC network address...'
        try {
            $detectedAddress = (Invoke-RS2SystemProbe -Query address -TimeoutSeconds $ProbeTimeoutSeconds | ConvertFrom-Json).address
            if (-not $detectedAddress) { throw 'No address was returned.' }
        } catch {
            throw "Automatic address detection failed. Set server_address in options.ini to this PC's IPv4 address on the Xbox LAN and run Start-Server.cmd again. Details: $($_.Exception.Message)"
        }
    } else {
        Write-Output '[2/4] Using the manual server address; automatic discovery skipped.'
    }
    Write-Output '[3/4] Checking the bundled Bun runtime...'
    $version = Invoke-RS2BoundedProcess -Executable $rs2Bun -Arguments @('--version') -TimeoutSeconds $ProbeTimeoutSeconds -Stage 'Bundled Bun runtime'
    Write-Output "Bun $version"
    Write-Output '[4/4] Preparing the local account, keys and Xbox configuration...'
    $arguments = @('run', (Join-Path $PSScriptRoot 'initialize-server.mjs'))
    if ($detectedAddress) { $arguments += @('--detected-address', $detectedAddress) }
    Invoke-RS2BoundedProcess -Executable $rs2Bun -Arguments $arguments -TimeoutSeconds $SetupTimeoutSeconds -Stage 'Server configuration'
} finally {
    $controlLock.Dispose()
}
