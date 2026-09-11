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
    # Covers a previous launcher exiting between process creation and PID recording.
    return @(Get-CimInstance Win32_Process -Filter "Name = 'bun.exe'" | Where-Object {
        $_.ExecutablePath -eq $rs2Bun -and $_.CommandLine -and
        $_.CommandLine.IndexOf($rs2Wrapper, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
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
