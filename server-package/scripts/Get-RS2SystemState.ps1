param([Parameter(Mandatory)][ValidateSet('processes','address')][string]$Query,
    [string]$BunPath, [string]$WrapperPath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try {
    if ($Query -eq 'processes') {
        # Windows may report an executable using an 8.3 alias even when the
        # launcher uses its long name. Compare real file arguments, not strings.
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class RS2ProcessPaths {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern uint GetLongPathName(string path, StringBuilder output, uint length);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    public static string Canonical(string path) {
        var output = new StringBuilder(32768);
        uint length = GetLongPathName(path, output, (uint)output.Capacity);
        if (length == 0 || length >= output.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        return output.ToString();
    }
    public static string[] Arguments(string command) {
        int count; IntPtr memory = CommandLineToArgvW(command, out count);
        if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var result = new string[count];
            for (int i=0;i<count;i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory,i*IntPtr.Size));
            return result;
        } finally { LocalFree(memory); }
    }
}
'@
        $expectedBun = [RS2ProcessPaths]::Canonical($BunPath)
        $expectedWrapper = [RS2ProcessPaths]::Canonical($WrapperPath)
        $processIds = @(Get-CimInstance Win32_Process -Filter "Name = 'bun.exe'" -OperationTimeoutSec 5 | ForEach-Object {
            if (-not $_.ExecutablePath -or -not $_.CommandLine) {
                throw 'Cannot inspect a running Bun process. Run the launcher as the same Windows user as that process; do not start another server until its identity is known.'
            }
            if ([RS2ProcessPaths]::Canonical($_.ExecutablePath) -eq $expectedBun) {
                $wrapperMatch = $false
                foreach ($argument in [RS2ProcessPaths]::Arguments($_.CommandLine)) {
                    if ([IO.Path]::GetExtension($argument) -ieq '.ts' -and
                        [IO.Path]::GetPathRoot($argument) -eq [IO.Path]::GetPathRoot($expectedWrapper) -and [IO.File]::Exists($argument) -and
                        [RS2ProcessPaths]::Canonical($argument) -eq $expectedWrapper) { $wrapperMatch = $true; break }
                }
                if ($wrapperMatch) { [int]$_.ProcessId }
            }
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
