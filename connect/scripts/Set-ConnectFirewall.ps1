param([Parameter(Mandatory)][string]$Program, [ValidateRange(1024,65535)][int]$GamePort = 43594, [switch]$Remove)
$ErrorActionPreference = 'Stop'
$programPath = (Resolve-Path -LiteralPath $Program).Path
if ([IO.Path]::GetFileName($programPath) -ne 'RS2XboxConnect.exe') { throw 'Select the installed RS2XboxConnect.exe.' }
$identity = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Windows administrator approval is required for firewall setup.' }
# Names include an installation-path hash. Remove only this app installation's rules.
$hash = [Security.Cryptography.SHA256]::Create()
try { $suffix = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($programPath.ToLowerInvariant())))).Replace('-','').Substring(0,16) }
finally { $hash.Dispose() }
$prefix = 'RS2Connect-' + $suffix
foreach ($name in @('Game','Discovery','Login','Tunnel')) { Get-NetFirewallRule -Name "$prefix-$name" -ErrorAction SilentlyContinue | Remove-NetFirewallRule }
if ($Remove) { return }
# LAN-facing services accept local-subnet traffic only. Internet hosts expose
# only the authenticated TLS tunnel, on a network Windows marks Private.
New-NetFirewallRule -Name "$prefix-Game" -DisplayName 'RS2 Connect - legacy Xbox game' -Direction Inbound -Action Allow -Profile Private -Program $programPath -Protocol TCP -LocalPort $GamePort -RemoteAddress LocalSubnet | Out-Null
New-NetFirewallRule -Name "$prefix-Discovery" -DisplayName 'RS2 Connect - Xbox discovery' -Direction Inbound -Action Allow -Profile Private -Program $programPath -Protocol UDP -LocalPort 43596 -RemoteAddress LocalSubnet | Out-Null
New-NetFirewallRule -Name "$prefix-Login" -DisplayName 'RS2 Connect - approved Xbox login' -Direction Inbound -Action Allow -Profile Private -Program $programPath -Protocol TCP -LocalPort 43597 -RemoteAddress LocalSubnet | Out-Null
New-NetFirewallRule -Name "$prefix-Tunnel" -DisplayName 'RS2 Connect - invitation protected tunnel' -Direction Inbound -Action Allow -Profile Private -Program $programPath -Protocol TCP -LocalPort 43595 | Out-Null
Write-Output 'RS2 Connect Private-network rules installed. Firewall protection remains enabled.'
