param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath (Join-Path $source 'RS2XboxConnect.exe'))) { throw 'Run INSTALL.cmd from the extracted Connect Windows package.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\RS2XboxConnect\0.2.0-preview'
if (Test-Path -LiteralPath $destination) { throw 'This preview is already installed. Stop Connect before installing a new version; choose a new versioned folder for development builds.' }
[void][IO.Directory]::CreateDirectory($destination)
Get-ChildItem -LiteralPath $source | Copy-Item -Destination $destination -Recurse
$exe = Join-Path $destination 'RS2XboxConnect.exe'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'RS2 Xbox Connect.lnk'))
$link.TargetPath = $exe; $link.WorkingDirectory = $destination; $link.Save()
$firewall = Join-Path $destination 'scripts\Set-ConnectFirewall.ps1'
try {
    $result = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $firewall + '"'),'-Program',('"' + $exe + '"')) -Verb RunAs -Wait -PassThru -WindowStyle Hidden
    if ($result.ExitCode -ne 0) { Write-Warning 'Firewall setup did not finish. Use Set up Windows firewall inside Connect.' }
} catch { Write-Warning 'Firewall setup was declined or unavailable. Use Set up Windows firewall inside Connect when ready.' }
if (-not $NoLaunch) { Start-Process -FilePath $exe -WorkingDirectory $destination -WindowStyle Hidden }
Write-Output 'Installed. Launch RS2 Xbox Connect from the Start menu. Worlds and private settings are stored separately from the application.'
