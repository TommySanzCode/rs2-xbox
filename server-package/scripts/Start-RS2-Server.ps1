param([ValidateRange(1, 120)][int]$WaitSeconds = 30)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RS2-Server.Common.ps1')

foreach ($requiredPath in @($rs2Bun, $rs2Wrapper, (Join-Path $rs2Engine 'src\app.ts'), (Join-Path $rs2Engine '.env'))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Missing server prerequisite: $requiredPath"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $rs2Engine 'node_modules') -PathType Container)) {
    throw 'The portable server dependencies have not been installed in Server225/engine/node_modules.'
}

$controlLock = Open-RS2ControlLock
try {
    $record = Read-RS2Record
    $serverProcess = Get-RS2RecordedProcess $record
    if ($serverProcess) {
        Write-Output "Checking readiness of the existing server process (PID $($serverProcess.Id))."
    } else {
        $orphaned = @(Get-RS2WrapperProcesses)
        if ($orphaned.Count -gt 0) {
            throw "An existing local server process has no matching PID record (PID $($orphaned.ProcessId -join ', ')). Inspect it before launching another copy."
        }

        $runId = [Guid]::NewGuid().ToString('D')
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $stdoutLog = Join-Path $rs2Runtime "server-$stamp-$runId.stdout.log"
        $stderrLog = Join-Path $rs2Runtime "server-$stamp-$runId.stderr.log"
        # Start-Process joins ArgumentList into a Windows command line. Quote the
        # complete file argument explicitly because the workspace contains spaces.
        $argumentLine = 'run "' + $rs2Wrapper + '" --run-id ' + $runId
        $previousPath = $env:PATH
        try {
            $env:PATH = (Split-Path -Parent $rs2Bun) + ';' + $previousPath
            $serverProcess = Start-Process -FilePath $rs2Bun -ArgumentList $argumentLine `
                -WorkingDirectory $rs2Engine -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
        } finally {
            $env:PATH = $previousPath
        }

        $record = [pscustomobject]@{
            pid = $serverProcess.Id
            startTimeUtc = $serverProcess.StartTime.ToUniversalTime().ToString('o')
            runId = $runId
            bunPath = $rs2Bun
            wrapperPath = $rs2Wrapper
            workingDirectory = $rs2Engine
            stdoutLog = $stdoutLog
            stderrLog = $stderrLog
        }
        Write-RS2Utf8 $rs2RecordPath ($record | ConvertTo-Json)
    }
    $stdoutLog = $record.stdoutLog
    $stderrLog = $record.stderrLog

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        if (-not (Get-RS2RecordedProcess $record)) {
            throw "Server exited during startup. Read $stderrLog and $stdoutLog."
        }
        $status = Read-RS2Status $record
        if ($status -and $status.status -eq 'stopping') {
            throw "Server is shutting down (PID $($record.pid)). Wait for it to stop before starting again. Read $stderrLog and $stdoutLog."
        }
        if ($status -and $status.status -eq 'ready') {
            $listener = @(Get-NetTCPConnection -State Listen -OwningProcess $record.pid -ErrorAction SilentlyContinue |
                Where-Object { $_.LocalPort -eq $status.nodePort })
            if ($listener.Count -gt 0) {
                Write-Output "RS2 revision 225 is ready on TCP port $($status.nodePort) (PID $($record.pid))."
                Write-Output "Log: $stdoutLog"
                Write-Output "Errors: $stderrLog"
                return
            }
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    Write-Output "Log: $stdoutLog"
    Write-Output "Errors: $stderrLog"
    throw "Readiness was not confirmed within $WaitSeconds seconds. The server process (PID $($record.pid)) was left running. Check the logs, then run Start-Server.cmd again to wait for readiness, or Stop-Server.cmd for graceful shutdown."
} finally {
    $controlLock.Dispose()
}
