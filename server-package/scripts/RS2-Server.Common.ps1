# Shared local process identity checks; never stop a process by PID alone.
$rs2Workspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$rs2Server = Join-Path $rs2Workspace 'Server225'
$rs2Engine = Join-Path $rs2Server 'engine'
$rs2Runtime = Join-Path $rs2Server 'runtime'
$rs2Bun = Join-Path $rs2Runtime 'bun-windows-x64\bun.exe'
$rs2Wrapper = Join-Path $rs2Server 'run-local.ts'
$rs2RecordPath = Join-Path $rs2Runtime 'server-process.json'

function Read-RS2Record {
    if (Test-Path -LiteralPath $rs2RecordPath) {
        try { return (Get-Content -LiteralPath $rs2RecordPath -Raw | ConvertFrom-Json) }
        catch { throw "Cannot read $rs2RecordPath. Inspect it before starting another server." }
    }
    return $null
}

function Get-RS2RecordedProcess($record) {
    if (-not $record) { return $null }
    $serverProcess = Get-Process -Id $record.pid -ErrorAction SilentlyContinue
    if (-not $serverProcess) { return $null }
    # PowerShell 7 may deserialize ISO JSON values as DateTime; Windows
    # PowerShell 5 leaves them as strings. Preserve subsecond precision in both.
    $recordedStart = if ($record.startTimeUtc -is [DateTime]) {
        $record.startTimeUtc
    } else {
        [DateTime]::Parse([string]$record.startTimeUtc, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
    }
    if ($serverProcess.StartTime.ToUniversalTime().Ticks -ne $recordedStart.ToUniversalTime().Ticks) { return $null }
    if ($serverProcess.Path -ne $rs2Bun -or $record.bunPath -ne $rs2Bun -or $record.wrapperPath -ne $rs2Wrapper) {
        return $null
    }
    return $serverProcess
}

function Get-RS2WrapperProcesses {
    # A clean first run needs no WMI at all. If Bun exists, preserve the orphan
    # check, but isolate potentially stalled Windows providers in a timed worker.
    if (@(Get-Process -Name bun -ErrorAction SilentlyContinue).Count -eq 0) { return @() }
    $json = Invoke-RS2SystemProbe -Query processes
    $result = $json | ConvertFrom-Json
    return @($result.matches | ForEach-Object { [pscustomobject]@{ ProcessId = [int]$_ } })
}

function ConvertTo-RS2ProcessArgument([string]$value) {
    # Windows argv quoting, not shell escaping. No command shell is involved.
    return '"' + [regex]::Replace($value, '(\\*)("|$)', {
        param($match)
        $slashes = $match.Groups[1].Value
        if ($match.Groups[2].Value -eq '"') { return $slashes + $slashes + '\"' }
        return $slashes + $slashes
    }) + '"'
}

function Invoke-RS2BoundedProcess {
    param([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds = 10, [string]$Stage)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = ($Arguments | ForEach-Object { ConvertTo-RS2ProcessArgument $_ }) -join ' '
    $info.WorkingDirectory = $rs2Workspace
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw "$Stage could not start." }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            # Only terminate the exact helper created above, never a game server.
            try { $process.Kill(); [void]$process.WaitForExit(2000) } catch { }
            throw "$Stage timed out after $TimeoutSeconds seconds. This setup helper was stopped; no running game server was stopped."
        }
        if (-not $stdout.Wait(2000) -or -not $stderr.Wait(2000)) { throw "$Stage output did not finish." }
        if ($process.ExitCode -ne 0) {
            $detail = $stderr.Result.Trim()
            throw "$Stage failed (exit $($process.ExitCode)). $detail"
        }
        return $stdout.Result.TrimEnd()
    } finally { $process.Dispose() }
}

function Invoke-RS2SystemProbe {
    param([ValidateSet('processes','address')][string]$Query, [int]$TimeoutSeconds = 10)
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    return Invoke-RS2BoundedProcess -Executable $powershell -Arguments @('-NoLogo','-NoProfile','-NonInteractive',
        '-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'Get-RS2SystemState.ps1'),
        '-Query',$Query,'-BunPath',$rs2Bun,'-WrapperPath',$rs2Wrapper) -TimeoutSeconds $TimeoutSeconds -Stage "Windows $Query check"
}

function Read-RS2AddressOption {
    $address = 'auto'; $seen = $false
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $rs2Workspace 'options.ini'))) {
        if ($line -match '^\s*server_address\s*=(.*)$') {
            if ($seen) { throw 'options.ini contains duplicate server_address entries.' }
            $address = $Matches[1].Trim(); $seen = $true
        }
    }
    if ($address -ne 'auto') {
        $parsed = $null
        if (-not [Net.IPAddress]::TryParse($address, [ref]$parsed) -or
            $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
            $parsed.ToString() -ne $address -or $parsed.Equals([Net.IPAddress]::Any)) {
            throw 'Set server_address in options.ini to auto or a complete IPv4 address for this PC.'
        }
    }
    return $address
}

function Test-RS2GameReady([int]$port) {
    # Avoid loading NetTCPIP/WMI merely to confirm readiness. The matching PID,
    # start time and run-specific ready record are checked by the caller first.
    $client = New-Object Net.Sockets.TcpClient
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $client.ConnectAsync('127.0.0.1', $port).Wait(1000)) { return $false }
        $stream = $client.GetStream(); $seed = New-Object byte[] 8; $read = 0
        while ($read -lt 8) {
            $remaining = 2000 - [int]$timer.ElapsedMilliseconds
            if ($remaining -le 0) { return $false }
            $stream.ReadTimeout = $remaining
            $count = $stream.Read($seed, $read, 8 - $read)
            if ($count -eq 0) { return $false }
            $read += $count
        }
        return $true
    } catch { return $false } finally { $client.Dispose() }
}

function Read-RS2Status($record) {
    $statusPath = Join-Path $rs2Runtime ("server-{0}.status.json" -f $record.runId)
    if (Test-Path -LiteralPath $statusPath) {
        try { return (Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json) }
        catch { return $null }
    }
    return $null
}

function Open-RS2ControlLock {
    [void][System.IO.Directory]::CreateDirectory($rs2Runtime)
    try {
        return [System.IO.File]::Open((Join-Path $rs2Runtime 'server-control.lock'),
            [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    } catch {
        throw 'Another server start/stop command is running. Wait for it to finish.'
    }
}

function Write-RS2Utf8([string]$path, [string]$value) {
    [System.IO.File]::WriteAllText($path, $value, (New-Object System.Text.UTF8Encoding($false)))
}
