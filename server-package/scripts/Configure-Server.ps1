$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RS2-Server.Common.ps1')

$controlLock = Open-RS2ControlLock
try {
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
    # Prefer the active default route. Explicit server_address bypasses detection
    # in the initializer, so a disconnected PC can still be configured manually.
    $detectedAddress = ''
    $routes = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Sort-Object @{ Expression = { $_.RouteMetric + $_.InterfaceMetric } })
    foreach ($route in $routes) {
        $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue |
            Where-Object { $_.AddressState -eq 'Preferred' -and -not $_.SkipAsSource -and
                $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' })
        if ($addresses.Count -gt 0) { $detectedAddress = $addresses[0].IPAddress; break }
    }
    $arguments = @('run', (Join-Path $PSScriptRoot 'initialize-server.mjs'))
    if ($detectedAddress) { $arguments += @('--detected-address', $detectedAddress) }
    & $rs2Bun @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Configuration failed. Correct the reported option and try again.' }
} finally {
    $controlLock.Dispose()
}
