param([Parameter(Mandatory)][ValidateSet('processes','address')][string]$Query,
    [string]$BunPath, [string]$WrapperPath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try {
    if ($Query -eq 'processes') {
        $processIds = @(Get-CimInstance Win32_Process -Filter "Name = 'bun.exe'" -OperationTimeoutSec 5 | ForEach-Object {
            if (-not $_.ExecutablePath -or -not $_.CommandLine) {
                throw 'Cannot inspect a running Bun process. Run the launcher as the same Windows user as that process; do not start another server until its identity is known.'
            }
            if ($_.ExecutablePath -eq $BunPath -and $_.CommandLine.IndexOf($WrapperPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) { [int]$_.ProcessId }
        })
        [pscustomobject]@{ matches = $processIds } | ConvertTo-Json -Compress
    } else {
        # Managed networking uses IP Helper instead of the NetTCPIP CIM provider.
        $candidates = @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | ForEach-Object {
            if ($_.OperationalStatus -ne [Net.NetworkInformation.OperationalStatus]::Up -or
                $_.NetworkInterfaceType -eq [Net.NetworkInformation.NetworkInterfaceType]::Loopback) { return }
            $properties = $_.GetIPProperties()
            $gateways = @($properties.GatewayAddresses | Where-Object {
                $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and -not $_.Address.Equals([Net.IPAddress]::Any)
            })
            if ($gateways.Count -eq 0) { return }
            $properties.UnicastAddresses | Where-Object {
                $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and
                -not [Net.IPAddress]::IsLoopback($_.Address) -and $_.Address.ToString() -notlike '169.254.*'
            } | ForEach-Object { $_.Address.ToString() }
        } | Select-Object -Unique)
        $address = ''
        if ($candidates.Count -gt 0) {
            $socket = New-Object Net.Sockets.Socket([Net.Sockets.AddressFamily]::InterNetwork, [Net.Sockets.SocketType]::Dgram, [Net.Sockets.ProtocolType]::Udp)
            try {
                # Consult Windows routing without sending a packet. The target is
                # an RFC 5737 documentation address, not an internet service.
                $destination = New-Object Net.IPAddress(,[byte[]](192,0,2,1))
                $socket.Connect((New-Object Net.IPEndPoint($destination,9)))
                $routed = $socket.LocalEndPoint.Address.ToString()
                if ($candidates -contains $routed) { $address = $routed }
            } catch { } finally { $socket.Dispose() }
            if (-not $address -and $candidates.Count -eq 1) { $address = $candidates[0] }
        }
        if (-not $address) { throw 'No unambiguous active IPv4 adapter was found. Set server_address in options.ini to the PC address on the Xbox LAN.' }
        [pscustomobject]@{ address = $address } | ConvertTo-Json -Compress
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
