param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$ServerZip,
    [Parameter(Mandatory)][string]$Xbox64Folder, [Parameter(Mandatory)][string]$Xbox128Folder,
    [Parameter(Mandatory)][string]$SoundFont)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a new artifact folder. Existing packages are never overwritten.' }
[void][IO.Directory]::CreateDirectory($out)
$hash = 'bc1a236a076264580fbf2ba665610dbd8b027a2ec713bf44f9a5c1bb82cee265'
if ((Get-FileHash -LiteralPath $ServerZip).Hash.ToLowerInvariant() -ne $hash) { throw 'Use the original clean Preview 1 server archive.' }
$app = Join-Path $out 'RS2-Xbox-Connect'
& dotnet publish (Join-Path $repo 'connect\src\Connect.App') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Windows app publish failed.' }
foreach ($folder in @('scripts','licenses')) { Copy-Item -LiteralPath (Join-Path $repo "connect\$folder") -Destination $app -Recurse }
Copy-Item -LiteralPath (Join-Path $repo 'connect\README.md') -Destination (Join-Path $app 'START-HERE.md')
[IO.File]::WriteAllText((Join-Path $app 'START-HERE.md'), [IO.File]::ReadAllText((Join-Path $app 'START-HERE.md')).Replace('(../docs/', '(docs/'), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $repo 'connect\THIRD-PARTY.md'),(Join-Path $repo 'CREDITS.md') -Destination $app
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination $app -Recurse
[void][IO.Directory]::CreateDirectory((Join-Path $app 'templates'))
Copy-Item -LiteralPath (Join-Path $repo 'xbox-config.example.ini'),(Join-Path $repo 'xbox-128-config.example.ini') -Destination (Join-Path $app 'templates')
$extract = Join-Path $out 'server-extract'
[IO.Compression.ZipFile]::ExtractToDirectory($ServerZip, $extract)
$serverSource = (Resolve-Path -LiteralPath (Join-Path $extract 'RS2-2004-Server')).Path
if (-not $serverSource.StartsWith($out + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid server staging path.' }
Move-Item -LiteralPath $serverSource -Destination (Join-Path $app 'server-template')
[IO.File]::WriteAllText((Join-Path $app 'INSTALL.cmd'), "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0scripts\Install-Connect.ps1`"`r`nif errorlevel 1 pause`r`n", [Text.Encoding]::ASCII)
$xboxRoot = Join-Path $app 'Xbox-clients'; [void][IO.Directory]::CreateDirectory($xboxRoot)
foreach ($profile in @(64,128)) {
    $source = if ($profile -eq 64) { $Xbox64Folder } else { $Xbox128Folder }
    $destination = Join-Path $xboxRoot "$profile-MB"
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $source).Path -Destination $destination -Recurse
    $config = [IO.File]::ReadAllText((Join-Path $destination 'config.ini'))
    if ($config -notmatch '(?m)^socketip\s*=\s*auto\s*$' -or $config -match '(?m)^(username|password|rsa_modulus|rsa_exponent)[ \t]*=[ \t]*[^ \t\r\n#;]') { throw 'Xbox folder must use a fresh public auto-discovery config with no credentials or RSA key.' }
    $xbe = [IO.File]::ReadAllBytes((Join-Path $destination 'default.xbe'))
    if ([Text.Encoding]::ASCII.GetString($xbe,0,4) -ne 'XBEH') { throw 'Invalid Xbox executable.' }
    $limited = ([BitConverter]::ToUInt32($xbe,0x124) -band 4) -ne 0
    if ($limited -ne ($profile -eq 64)) { throw 'Xbox executable memory flag does not match its package.' }
    if ($profile -eq 128) { Copy-Item -LiteralPath $SoundFont -Destination (Join-Path $destination 'TimGM6mb.sf2') }
    Copy-Item -LiteralPath (Join-Path $repo 'release-notices') -Destination $destination -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination $destination -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'CREDITS.md'),(Join-Path $repo 'docs\THIRD-PARTY.md') -Destination $destination
    & python (Join-Path $repo 'scripts\sanitize-xbox-paths.py') --input (Join-Path $source 'default.xbe') --output (Join-Path $destination 'default.xbe')
    if ($LASTEXITCODE -ne 0) { throw 'Xbox diagnostic path sanitation failed.' }
    [IO.File]::WriteAllText((Join-Path $destination 'CONNECT-START.txt'), "Connect direct preview. Copy this whole folder for a fresh Xbox install. Existing installs: back up config.ini; replace default.xbe and change ONLY socketip to auto to preserve graphics/audio/controls. Start Connect on the PC, press Start on Xbox, approve the matching code in Connect with a unique character, then press Start again. 128 MB requires suitable hardware/BIOS; it is not hardware-validated. See the bundled docs/ONLINE-DIRECT.md in the Windows package.")
}
$private = @(Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object { $_.Name -in @('.env','db.sqlite','private.pem','public.pem','setup.local.json','Xbox-config.ini','settings.protected') -or $_.Extension -in @('.rs2invite','.rs2relay','.rs2backup','.pfx','.log','.pdb') })
if ($private.Count) { throw 'Generated private files or debug artifacts were found in the package.' }
$zip = Join-Path $out 'RS2-Xbox-Connect-Direct-Windows.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($app,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
$names = @([IO.Path]::GetFileName($zip))
foreach ($profile in @(64,128)) {
    $name = "RS2-Xbox-Connect-Client-$profile-MB.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $xboxRoot "$profile-MB"),(Join-Path $out $name),[IO.Compression.CompressionLevel]::Optimal,$true); $names += $name
}
$checksums = $names | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath (Join-Path $out $_)).Hash.ToLowerInvariant(),$_ }
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Mode='direct'; Version='0.2.0-preview.1'; AppFolder=$app; AppZip=$zip; Assets=@($names)+@('SHA256SUMS.txt'); ServerArchiveSha256=$hash; InternetHardwareTested=$false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'build-manifest.json')
Write-Output "Direct preview packages ready: $out"
